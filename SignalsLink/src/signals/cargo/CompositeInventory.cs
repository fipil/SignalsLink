using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace SignalsLink.src.signals.cargo
{
    /// <summary>
    /// Several holds shown to the paper as one inventory. Nothing is copied - the slots are the
    /// holds' own.
    ///
    /// Order matters: holds are laid out nearest-first and transfers walk slots in index order, so
    /// filling starts at the near end.
    /// </summary>
    public class CompositeInventory : InventoryBase
    {
        private readonly List<ItemSlot> slots = new List<ItemSlot>();
        private readonly List<ICargoHold> owners = new List<ICargoHold>();

        public CompositeInventory(ICoreAPI api)
            // The id must contain a dash - VS derives className/instanceId by splitting on it.
            : base("signalslink-composite", "composite", api)
        {
        }

        /// <summary>Call in the order the device should work through the holds.</summary>
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

        /// <summary>Which hold a slot came from.</summary>
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

        // Nothing to save: the holds persist themselves.
        public override void FromTreeAttributes(ITreeAttribute tree)
        {
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
        }
    }
}
