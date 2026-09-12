using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace SignalsLink.YTT.src.probe
{
    /// <summary>
    /// Layer three: does the other mod actually SAVE what we write? If it stopped, every call
    /// still succeeds and a player's goods vanish at the next reload.
    ///
    /// Checked twice: <see cref="IsWired"/> before anything moves, and
    /// <see cref="VerifyFirstTransfer"/> after the first one. No rollback - putting goods back
    /// would itself depend on saving working.
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

        /// <summary>Asked once per inventory, before it is ever written to.</summary>
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
                // A check that cannot be made is not a licence to carry on.
                return Distrust("the save-wiring check failed: " + e.Message);
            }
        }

        /// <summary>Exactly one comparison is ever made; after it, stop serialising wagons.</summary>
        public bool NeedsSnapshot => Trusted && !firstTransferVerified;

        /// <summary>What the vehicle's saved goods look like right now, or null when there is nothing.</summary>
        public string Snapshot(Entity entity)
        {
            return Describe(entity?.WatchedAttributes?[InventoryTreeKey] as ITreeAttribute);
        }

        /// <summary>
        /// From the BYTES the tree would be saved as. NOT <c>ToString()</c>: TreeAttribute does not
        /// override it, so every snapshot would read the same and the check would always fail.
        /// </summary>
        public static string Describe(ITreeAttribute tree)
        {
            if (tree == null) return null;

            using MemoryStream buffer = new MemoryStream();

            using (BinaryWriter writer = new BinaryWriter(buffer, Encoding.UTF8, true))
            {
                tree.ToBytes(writer);
            }

            using SHA1 sha = SHA1.Create();

            return Convert.ToHexString(sha.ComputeHash(buffer.ToArray())).Substring(0, 12).ToLowerInvariant();
        }

        /// <summary>Once, after the first real transfer: what is saved must have changed too.</summary>
        public void VerifyFirstTransfer(Entity entity, string before)
        {
            if (firstTransferVerified || !Trusted) return;

            string after = Snapshot(entity);

            if (before != null && before == after)
            {
                // One vehicle that did not change proves nothing: several are marked at once and
                // most of them were simply not the one written to. Only a long run of them saying
                // the same is evidence that nothing is being saved at all.
                if (++unchanged < UnchangedBeforeDistrust) return;

                Distrust("goods were moved in or out of a vehicle and nothing was written down."
                    + " Whatever is on that vehicle now will be lost when the world is reloaded.");
                return;
            }

            firstTransferVerified = true;

            api.Logger.Notification("[SignalsLink.YTT] first transfer was saved; carrying on.");
        }

        /// <summary>
        /// How many vehicles may be marked, and show no change, before this concludes that nothing
        /// is being saved. High enough to survive a train being marked whole, low enough that a
        /// mod which really stopped saving is caught within seconds.
        /// </summary>
        private const int UnchangedBeforeDistrust = 40;

        private int unchanged;

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
