using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using SignalsLink.src.signals.cargo;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SignalsLink.YTT.src.train
{
    /// <summary>
    /// A minecart with something hung on it.
    ///
    /// Nothing like a boxcar and nothing reflective about it. A cart carries a chest, a crate or a
    /// basket on a vanilla <c>attachable</c> point, and the goods live inside that ITEM as a held
    /// bag - not in an inventory of the vehicle's own. All of it is Vintage Story's, so this needs
    /// no probe and no fingerprint.
    ///
    /// The one thing that has to be got right is WHOSE copy of the goods is written to. The game
    /// keeps a long-lived workspace per hung container, and that workspace's inventory is what the
    /// player sees and edits; it is filled once and never re-reads the item afterwards. Writing to
    /// the item instead - which is where the goods really live - saved the cargo and left the
    /// player looking at a chest that never changed. So this goes through the workspace, the same
    /// way a player's mouse does, and the workspace writes it down.
    /// </summary>
    public static class YttCart
    {
        /// <summary>The vanilla behavior a cart hangs its load on.</summary>
        public const string AttachableBehavior = "attachable";

        /// <summary>Which stack each workspace was last filled from; see <see cref="NeedsReload"/>.</summary>
        private static readonly ConditionalWeakTable<AttachedContainerWorkspace, ItemStack> filledFrom = new ConditionalWeakTable<AttachedContainerWorkspace, ItemStack>();

        public static bool IsCart(Entity entity)
        {
            return entity?.GetBehavior<EntityBehaviorAttachable>() != null;
        }

        /// <summary>
        /// One hold per container hanging on the cart. Attachment points holding something that is
        /// not a container - a lantern, a conductor's locust - simply have none, which is what
        /// keeps this from turning every hook into cargo.
        /// </summary>
        public static IEnumerable<ICargoHold> HoldsOf(Entity entity)
        {
            EntityBehaviorAttachable attachable = entity?.GetBehavior<EntityBehaviorAttachable>();
            if (attachable?.Inventory == null) yield break;

            for (int i = 0; i < attachable.Inventory.Count; i++)
            {
                ItemSlot hook = attachable.Inventory[i];
                if (hook?.Itemstack == null) continue;

                CollectibleBehaviorHeldBag bag = BagOn(hook.Itemstack.Collectible);
                if (bag == null) continue;

                // storeInv is how a cart writes its hooks into the entity; the game hands it to the
                // workspace for exactly this, and calls it after every change of its own.
                AttachedContainerWorkspace workspace = bag.getOrCreateContainerWorkspace(i, entity, attachable.storeInv);

                if (NeedsReload(workspace.WrapperInv, Filled(workspace), hook.Itemstack))
                {
                    if (!workspace.TryLoadInv(hook, i, entity)) continue;

                    filledFrom.Remove(workspace);
                    filledFrom.Add(workspace, hook.Itemstack);
                }

                if (workspace.WrapperInv == null || workspace.WrapperInv.Count == 0) continue;

                yield return new CartHold(workspace, attachable.storeInv, i);
            }
        }

        /// <summary>
        /// The container behavior on a thing, if it is one.
        ///
        /// With inheritance, and that is not a detail: nothing in the game carries
        /// CollectibleBehaviorHeldBag itself. A chest carries BoatableGenericTypedContainer and a
        /// crate carries BoatableCrate, both subclasses of it. Asked for the exact type this finds
        /// nothing at all, and every cart in the world is empty.
        /// </summary>
        public static CollectibleBehaviorHeldBag BagOn(CollectibleObject collectible)
        {
            return collectible?.GetCollectibleBehavior<CollectibleBehaviorHeldBag>(true);
        }

        /// <summary>
        /// Must the workspace be filled from the item again?
        ///
        /// Only when it has never been filled, or when a different container now hangs there -
        /// filling it again throws away the slots the player may have open, and every later change
        /// comes back through those slots anyway.
        /// </summary>
        public static bool NeedsReload(IInventory wrapper, ItemStack filledFrom, ItemStack hanging)
        {
            return wrapper == null || filledFrom == null || !ReferenceEquals(filledFrom, hanging);
        }

        private static ItemStack Filled(AttachedContainerWorkspace workspace)
        {
            return filledFrom.TryGetValue(workspace, out ItemStack stack) ? stack : null;
        }
    }

    /// <summary>One container on a cart, shown to the paper as an inventory.</summary>
    public sealed class CartHold : ICargoHold
    {
        private readonly AttachedContainerWorkspace workspace;
        private readonly Action save;

        public CartHold(AttachedContainerWorkspace workspace, Action save, int index)
        {
            this.workspace = workspace;
            this.save = save;

            Code = "cart" + index;
        }

        public string Code { get; }

        /// <summary>The very inventory the player opens, so both see the same goods.</summary>
        public IInventory Inventory => workspace.WrapperInv;

        /// <summary>A cart drives away like any other vehicle: slots, not a place.</summary>
        public BlockPos Pos => null;

        public bool IsContainer => true;

        public bool IsEmpty => workspace.WrapperInv == null || workspace.WrapperInv.Empty;

        /// <summary>
        /// What the game does itself whenever one of these slots changes: write the slot back into
        /// the container item, then tell the cart to save. Leaving out either one loses the cargo
        /// at the next reload.
        /// </summary>
        public void MarkDirty()
        {
            InventoryGeneric inventory = workspace.WrapperInv;
            if (inventory == null) return;

            for (int i = 0; i < inventory.Count; i++)
            {
                if (inventory[i] is ItemSlotBagContent content) workspace.BagInventory.SaveSlotIntoBag(content);

                // And send the slot to anyone who has the chest open.
                inventory.MarkSlotDirty(i);
            }

            save?.Invoke();
        }
    }
}
