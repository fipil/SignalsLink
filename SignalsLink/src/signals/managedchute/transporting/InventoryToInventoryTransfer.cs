using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.managedchute.transporting
{
    // Základní přenos: inventář -> inventář.
    // Respektuje 1-based InputSlot/OutputSlot signály (0 = default chování).
    public class InventoryToInventoryTransfer : InventorySourcedTransferBase, IItemTransfer
    {
        private readonly IInventory targetInv;
        private readonly byte outputSlotSignal;

        public InventoryToInventoryTransfer(ICoreAPI api, IInventory sourceInv, IInventory targetInv, byte inputSlotSignal, byte outputSlotSignal, PaperConditionsEvaluator conditionsEvaluator) 
            : base(api, sourceInv, inputSlotSignal, conditionsEvaluator)
        {
            this.targetInv = targetInv;
            this.outputSlotSignal = outputSlotSignal;

            if ("smelting".EqualsFastIgnoreCase(targetInv.ClassName) && (outputSlotSignal == 0 || (outputSlotSignal >= 4 && outputSlotSignal <= 7)))
                canTransferLiquids = true;
        }

        public int TryMoveOneItem(ItemStackMoveOperation opTemplate)
        {
            ItemSlot src = GetSourceSlot();
            if (src == null || src.Empty) return 0;

            ItemSlot dst = GetTargetSlot(src);
            if (dst == null) return 0;

            int liquidMoved = TryMoveLiquidIntoSmeltingSlot(src, dst);
            if (liquidMoved > 0)
            {
                src.MarkDirty();
                dst.MarkDirty();
                return 1;
            }

            // Zkopíruj operation – šablonu si neničíme
            ItemStackMoveOperation op = new ItemStackMoveOperation(
                opTemplate.World,
                opTemplate.MouseButton,
                opTemplate.Modifiers,
                opTemplate.CurrentPriority,
                opTemplate.RequestedQuantity 
            );

            int moved = src.TryPutInto(dst, ref op);
            if (moved > 0)
            {
                src.MarkDirty();
                dst.MarkDirty();
            }

            return moved;
        }

        private int TryMoveLiquidIntoSmeltingSlot(ItemSlot src, ItemSlot dst)
        {
            if (!"smelting".EqualsFastIgnoreCase(targetInv.ClassName)) return 0;
            if (outputSlotSignal > 0 && (outputSlotSignal < 4 || outputSlotSignal > 7)) return 0;
            if (!HasCookingContainer()) return 0;
            if (dst is not ItemSlotWatertight) return 0;
            if (src.Itemstack == null || src.Itemstack.StackSize <= 0) return 0;

            int moved = TryMoveLiquidStackIntoWatertightSlot(src, dst);
            if (moved > 0) return moved;

            moved = TryMoveLiquidContainerIntoWatertightSlot(src, dst);
            if (moved > 0) return moved;

            return 0;
        }

        private int TryMoveLiquidStackIntoWatertightSlot(ItemSlot src, ItemSlot dst)
        {
            ItemStack liquidStack = src.Itemstack;
            var liquidProps = BlockLiquidContainerBase.GetContainableProps(liquidStack);
            if (liquidProps == null || !liquidStack.Collectible.IsLiquid()) return 0;

            if (dst.Itemstack != null && !dst.Itemstack.Equals(api.World, liquidStack, GlobalConstants.IgnoredStackAttributes)) return 0;

            float currentLitres = dst.StackSize / liquidProps.ItemsPerLitre;
            float remainingLitres = ((ItemSlotWatertight)dst).capacityLitres - currentLitres;
            if (remainingLitres <= 0) return 0;

            int moveQuantity = Math.Min(src.StackSize, (int)(liquidProps.ItemsPerLitre * Math.Min(1f, remainingLitres)));
            if (moveQuantity <= 0) return 0;

            if (dst.Empty)
            {
                ItemStack movedStack = liquidStack.Clone();
                movedStack.StackSize = moveQuantity;
                dst.Itemstack = movedStack;
            }
            else
            {
                dst.Itemstack.StackSize += moveQuantity;
            }

            src.TakeOut(moveQuantity);
            return moveQuantity;
        }

        private int TryMoveLiquidContainerIntoWatertightSlot(ItemSlot src, ItemSlot dst)
        {
            BlockLiquidContainerBase liquidContainer = src.Itemstack?.Block as BlockLiquidContainerBase;
            if (liquidContainer == null) return 0;

            ItemStack liquidStack = liquidContainer.GetContent(src.Itemstack);
            if (liquidStack == null) return 0;

            var liquidProps = BlockLiquidContainerBase.GetContainableProps(liquidStack);
            if (liquidProps == null) return 0;

            if (dst.Itemstack != null && !dst.Itemstack.Equals(api.World, liquidStack, GlobalConstants.IgnoredStackAttributes)) return 0;

            float currentLitres = dst.StackSize / liquidProps.ItemsPerLitre;
            float remainingLitres = ((ItemSlotWatertight)dst).capacityLitres - currentLitres;
            if (remainingLitres <= 0) return 0;

            float sourceLitres = liquidStack.StackSize / liquidProps.ItemsPerLitre;
            float toMoveLitres = Math.Min(1f, Math.Min(remainingLitres, sourceLitres));
            if (toMoveLitres <= 0) return 0;

            int quantityPerContainer = (int)(liquidProps.ItemsPerLitre * toMoveLitres / src.Itemstack.StackSize);
            if (quantityPerContainer <= 0) return 0;

            ItemStack taken = liquidContainer.TryTakeContent(src.Itemstack, quantityPerContainer);
            if (taken == null || taken.StackSize <= 0) return 0;

            taken.StackSize *= src.Itemstack.StackSize;

            if (dst.Empty)
            {
                dst.Itemstack = taken;
            }
            else
            {
                dst.Itemstack.StackSize += taken.StackSize;
            }

            return taken.StackSize;
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

            ItemSlot smeltingLiquidSlot = GetSmeltingLiquidTargetSlot(stack);
            if (smeltingLiquidSlot != null) return smeltingLiquidSlot;

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

        private ItemSlot GetSmeltingLiquidTargetSlot(ItemStack sourceStack)
        {
            if (!"smelting".EqualsFastIgnoreCase(targetInv.ClassName)) return null;
            if (!HasCookingContainer()) return null;

            ItemStack liquidStack = GetLiquidStackForTransfer(sourceStack);
            if (liquidStack == null) return null;

            int maxIndex = Math.Min(6, targetInv.Count - 1);

            for (int i = 3; i <= maxIndex; i++)
            {
                ItemSlot slot = targetInv[i];
                if (slot is not ItemSlotWatertight watertightSlot) continue;

                if (slot.Empty) return slot;

                if (!slot.Itemstack.Equals(api.World, liquidStack, GlobalConstants.IgnoredStackAttributes)) continue;

                var liquidProps = BlockLiquidContainerBase.GetContainableProps(liquidStack);
                if (liquidProps == null) return null;

                float currentLitres = slot.StackSize / liquidProps.ItemsPerLitre;
                if (currentLitres < watertightSlot.capacityLitres) return slot;
            }

            return null;
        }

        private ItemStack GetLiquidStackForTransfer(ItemStack sourceStack)
        {
            if (sourceStack == null) return null;
            if (sourceStack.Collectible.IsLiquid()) return sourceStack;

            BlockLiquidContainerBase liquidContainer = sourceStack.Block as BlockLiquidContainerBase;
            return liquidContainer?.GetContent(sourceStack);
        }

        private bool HasCookingContainer()
        {
            return targetInv is InventorySmelting smeltingInventory && smeltingInventory.HaveCookingContainer;
        }
    }
}