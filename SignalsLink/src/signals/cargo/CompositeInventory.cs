using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace SignalsLink.src.signals.cargo
{
    /// <summary>
    /// Several holds shown to the paper as one inventory, in a fixed order.
    ///
    /// This is what makes "unload the whole train" or "fill the yard" a single action rather than a
    /// fight with the one-action-per-pass rule: the holder offers one inventory, the paper works on
    /// it exactly as it works on a chest, and gathering across slots is machinery that already
    /// exists.
    ///
    /// Nothing is copied. Where the holds carry live slots, a change here is a change in the wagon
    /// they came from.
    ///
    /// <b>Order is the whole trick.</b> Holds are laid out nearest-first and the transfer code
    /// walks slots in index order, so filling starts at the near end and works outwards. That one
    /// rule gives a train "fill the nearest wagon, do not top up half-stacks in distant ones" and a
    /// yard "fill the nearest column to its height before starting another" - two wanted
    /// behaviours, no special cases. Choosing a slot by best fit across the whole thing would break
    /// both.
    /// </summary>
    public class CompositeInventory : InventoryBase
    {
        private readonly List<ItemSlot> slots = new List<ItemSlot>();
        private readonly List<ICargoHold> owners = new List<ICargoHold>();

        public CompositeInventory(ICoreAPI api)
            // NOTE: the id must contain a dash - VS derives className/instanceId by splitting on it.
            : base("signalslink-composite", "composite", api)
        {
        }

        /// <summary>Appends one hold. Call in the order the device should work through them.</summary>
        public void Append(ICargoHold hold)
        {
            if (hold?.Inventory == null) return;

            for (int i = 0; i < hold.Inventory.Count; i++)
            {
                ItemSlot slot = hold.Inventory[i];
                if (slot == null) continue;

                slots.Add(slot);
                owners.Add(hold);
            }
        }

        /// <summary>
        /// Which hold a slot came from - how the device turns a slot the paper chose back into the
        /// wagon or the column of piles it belongs to.
        /// </summary>
        public ICargoHold HoldOf(int slotId)
        {
            return slotId < 0 || slotId >= owners.Count ? null : owners[slotId];
        }

        public override int Count => slots.Count;

        public override ItemSlot this[int slotId]
        {
            get => slotId < 0 || slotId >= slots.Count ? null : slots[slotId];
            set
            {
                if (slotId < 0 || slotId >= slots.Count || value == null) return;
                slots[slotId] = value;
            }
        }

        /// <summary>
        /// Nothing to save. The holds persist themselves - this is a view assembled for one pass
        /// and thrown away again.
        /// </summary>
        public override void FromTreeAttributes(ITreeAttribute tree)
        {
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
        }
    }
}
