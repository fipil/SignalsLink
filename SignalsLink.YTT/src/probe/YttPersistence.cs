using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace SignalsLink.YTT.src.probe
{
    /// <summary>
    /// <b>Layer three: does it actually SAVE?</b>
    ///
    /// The first two layers ask what the other mod looks like. They cannot answer the one question
    /// that really matters here: after goods are written into a wagon and the slot is marked dirty,
    /// does anything write that down? The other mod does it by hooking the inventory's
    /// <c>SlotModified</c> event on the server. If that ever stops - if saving became explicit, say
    /// - then every call still succeeds, nothing throws, and <b>a player's goods vanish at the next
    /// reload</b>. No amount of looking at names catches that.
    ///
    /// So it is checked twice over.
    ///
    /// <b>Before anything moves</b> - the event's invocation list is read and asked whether anyone
    /// from the other mod is listening. Reading a field-like event's backing field mutates nothing
    /// and costs one lookup per inventory.
    ///
    /// <b>After the first real transfer</b> - the saved tree is compared with what it was. Goods
    /// have just changed, so it MUST have changed too. If it has not, the bridge stands down for
    /// the rest of the session rather than carrying on quietly losing things.
    ///
    /// There is no rollback of that first transfer, and that is deliberate: putting goods back
    /// means knowing exactly what moved and being sure the putting-back is itself saved - and the
    /// thing under suspicion is precisely whether saving works. Half a rollback would turn one lost
    /// stack into two. The check before anything moves is the one meant to catch this; this is the
    /// net under it.
    /// </summary>
    public sealed class YttPersistence
    {
        private const BindingFlags Anywhere =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>Where the other mod keeps a vehicle's goods.</summary>
        public const string InventoryTreeKey = "sgstorageInv";

        private readonly Assembly other;
        private readonly ICoreAPI api;

        private readonly HashSet<string> checkedInventories = new HashSet<string>();
        private bool firstTransferVerified;

        /// <summary>False once something has been found wrong. Nothing is moved after that.</summary>
        public bool Trusted { get; private set; } = true;

        public YttPersistence(ICoreAPI api, Assembly other)
        {
            this.api = api;
            this.other = other;
        }

        /// <summary>
        /// Is somebody from the other mod listening for changes to this inventory? Asked once per
        /// inventory, before it is ever written to.
        /// </summary>
        public bool IsWired(InventoryBase inventory)
        {
            if (!Trusted) return false;
            if (inventory == null) return false;
            if (checkedInventories.Contains(inventory.InventoryID)) return true;

            try
            {
                FieldInfo backing = typeof(InventoryBase).GetField("SlotModified", Anywhere);

                if (backing?.GetValue(inventory) is not Delegate handler)
                {
                    return Distrust("nothing is listening for changes to " + inventory.InventoryID
                        + ", so goods put there would not be saved");
                }

                foreach (Delegate one in handler.GetInvocationList())
                {
                    if (one.Method?.DeclaringType?.Assembly != other) continue;

                    checkedInventories.Add(inventory.InventoryID);
                    return true;
                }

                return Distrust("no handler from Yang's Transport Tycoon is listening for changes to "
                    + inventory.InventoryID + ", so goods put there would not be saved");
            }
            catch (Exception e)
            {
                // A check that cannot be made is not a licence to carry on: this is the failure
                // mode that costs a player their cargo.
                return Distrust("the save-wiring check failed: " + e.Message);
            }
        }

        /// <summary>What the vehicle's saved goods look like right now, or null when there is nothing.</summary>
        public string Snapshot(Entity entity)
        {
            if (entity?.WatchedAttributes?[InventoryTreeKey] is not ITreeAttribute tree) return null;

            return tree.ToString();
        }

        /// <summary>
        /// Called once, right after the first real transfer, with the snapshot taken before it. The
        /// goods have changed, so what is saved must have changed too.
        /// </summary>
        public void VerifyFirstTransfer(Entity entity, string before)
        {
            if (firstTransferVerified || !Trusted) return;

            firstTransferVerified = true;
            string after = Snapshot(entity);

            if (before != null && before == after)
            {
                Distrust("goods were moved in or out of a vehicle and nothing was written down."
                    + " Whatever is on that vehicle now will be lost when the world is reloaded.");
                return;
            }

            api.Logger.Notification("[SignalsLink.YTT] first transfer was saved; carrying on.");
        }

        private bool Distrust(string what)
        {
            if (Trusted)
            {
                api.Logger.Error("[SignalsLink.YTT] standing down: " + what
                    + " No more goods will be moved to or from trains this session.");
            }

            Trusted = false;
            return false;
        }
    }
}
