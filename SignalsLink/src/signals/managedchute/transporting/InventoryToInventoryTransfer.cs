using Vintagestory.API.Common;

namespace SignalsLink.src.signals.managedchute.transporting
{
    // Základní přenos: inventář -> inventář.
    // Respektuje 1-based InputSlot/OutputSlot signály (0 = default chování).
    public class InventoryToInventoryTransfer : IItemTransfer
    {
        private readonly ICoreAPI api;
        private readonly IInventory sourceInv;
        private readonly IInventory targetInv;
        private readonly byte inputSlotSignal;
        private readonly byte outputSlotSignal;

        public InventoryToInventoryTransfer(ICoreAPI api, IInventory sourceInv, IInventory targetInv, byte inputSlotSignal, byte outputSlotSignal)
        {
            this.api = api;
            this.sourceInv = sourceInv;
            this.targetInv = targetInv;
            this.inputSlotSignal = inputSlotSignal;
            this.outputSlotSignal = outputSlotSignal;
        }

        public int TryMoveOneItem(ItemStackMoveOperation opTemplate)
        {
            ItemSlot src = GetSourceSlot();
            if (src == null || src.Empty) return 0;

            // Zkopíruj operation – šablonu si neničíme
            ItemStackMoveOperation op = new ItemStackMoveOperation(
                opTemplate.World,
                opTemplate.MouseButton,
                opTemplate.Modifiers,
                opTemplate.CurrentPriority,
                opTemplate.RequestedQuantity
            );

            ItemSlot dst = GetTargetSlot(src);
            if (dst == null) return 0;

            int moved = src.TryPutInto(dst, ref op);
            if (moved > 0)
            {
                src.MarkDirty();
                dst.MarkDirty();
            }

            return moved;
        }

        private ItemSlot GetSourceSlot()
        {
            // InputSlot: 1-based index, 0 = „výchozí“ (poslední ne-prázdný / poslední slot)
            if (inputSlotSignal > 0)
            {
                int index = inputSlotSignal - 1;
                if (index >= 0 && index < sourceInv.Count)
                {
                    return sourceInv[index];
                }
                return null;
            }

            // 0 → poslední ne-prázdný, fallback poslední slot
            ItemSlot lastNonEmpty = null;
            for (int i = sourceInv.Count - 1; i >= 0; i--)
            {
                if (!sourceInv[i].Empty)
                {
                    lastNonEmpty = sourceInv[i];
                    break;
                }
            }

            if (lastNonEmpty != null) return lastNonEmpty;
            return sourceInv.Count > 0 ? sourceInv[sourceInv.Count - 1] : null;
        }

        private ItemSlot GetTargetSlot(ItemSlot fromSlot)
        {
            // OutputSlot: 1-based index, 0 = první vhodný
            if (outputSlotSignal > 0)
            {
                int index = outputSlotSignal - 1;
                if (index >= 0 && index < targetInv.Count)
                {
                    return targetInv[index];
                }
                return null;
            }

            // 0 → první vhodný slot (prázdný nebo slučitelný)
            ItemStack stack = fromSlot.Itemstack;
            if (stack == null) return null;

            for (int i = 0; i < targetInv.Count; i++)
            {
                ItemSlot slot = targetInv[i];

                if (slot.Empty) return slot;

                if (slot.Itemstack != null &&
                    slot.Itemstack.Collectible == stack.Collectible &&
                    slot.Itemstack.StackSize < slot.Itemstack.Collectible.MaxStackSize)
                {
                    return slot;
                }
            }

            return null;
        }
    }
}