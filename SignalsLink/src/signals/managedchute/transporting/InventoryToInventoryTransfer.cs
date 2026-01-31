using Vintagestory.API.Common;

namespace SignalsLink.src.signals.managedchute.transporting
{
    // Základní přenos: inventář -> inventář.
    // Respektuje 1-based InputSlot/OutputSlot signály (0 = default chování).
    public class InventoryToInventoryTransfer : InventorySourcedTransferBase, IItemTransfer
    {
        private readonly ICoreAPI api;
        private readonly IInventory targetInv;
        private readonly byte outputSlotSignal;


        public InventoryToInventoryTransfer(ICoreAPI api, IInventory sourceInv, IInventory targetInv, byte inputSlotSignal, byte outputSlotSignal): base(sourceInv, inputSlotSignal)
        {
            this.api = api;
            this.targetInv = targetInv;
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