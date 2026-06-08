using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using SignalsLink.src.signals.paperConditions;

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

        public override bool UsesAmountAsTriggerOnly => true;

        protected override void AddConditionContext(IDictionary<string, object> ctx)
        {
            ctx["targetInventory"] = targetInv;
        }

        public TransferOperationResult TryMove(ItemStackMoveOperation opTemplate)
        {
            TransferSelection selection = GetTransferSelection();
            ItemSlot src = selection?.SourceSlot;
            if (src == null || src.Empty) return TransferOperationResult.None;

            ItemSlot dst = GetTargetSlot(src, selection.Directives);
            if (dst == null) return TransferOperationResult.None;

            decimal requestedAmount = selection.Directives.Amount ?? opTemplate.RequestedQuantity;

            TransferOperationResult liquidResult = TryMoveLiquidIntoSmeltingSlot(src, dst, requestedAmount);
            if (liquidResult.Success)
            {
                src.MarkDirty();
                dst.MarkDirty();
                return liquidResult;
            }

            if (ShouldRequireSmeltingLiquidTransfer(src, dst))
            {
                return TransferOperationResult.None;
            }

            ItemStackMoveOperation op = new ItemStackMoveOperation(
                opTemplate.World,
                opTemplate.MouseButton,
                opTemplate.Modifiers,
                opTemplate.CurrentPriority,
                GetItemTransferQuantity(src, requestedAmount)
            );

            int moved = src.TryPutInto(dst, ref op);
            if (moved > 0)
            {
                src.MarkDirty();
                dst.MarkDirty();
                int triggerCost = selection.Directives.HasAmountOverride ? 1 : moved;
                return new TransferOperationResult(moved, triggerCost);
            }

            return TransferOperationResult.None;
        }

        public int TryMoveOneItem(ItemStackMoveOperation opTemplate)
        {
            return (int)TryMove(opTemplate).MovedAmount;
        }

        private TransferOperationResult TryMoveLiquidIntoSmeltingSlot(ItemSlot src, ItemSlot dst, decimal requestedAmount)
        {
            if (!"smelting".EqualsFastIgnoreCase(targetInv.ClassName)) return TransferOperationResult.None;
            if (!IsSmeltingCookingTarget(dst)) return TransferOperationResult.None;
            if (!HasCookingContainer()) return TransferOperationResult.None;
            if (src.Itemstack == null || src.Itemstack.StackSize <= 0) return TransferOperationResult.None;

            decimal litersToMove = NormalizeLiquidAmount(requestedAmount);
            if (litersToMove <= 0) return TransferOperationResult.None;

            int moved = TryMoveLiquidStackIntoWatertightSlot(src, dst, litersToMove);
            if (moved > 0) return CreateLiquidTransferResult(src, moved);

            moved = TryMoveLiquidContainerIntoWatertightSlot(src, dst, litersToMove);
            if (moved > 0) return CreateLiquidTransferResult(src, moved);

            return TransferOperationResult.None;
        }

        private int TryMoveLiquidStackIntoWatertightSlot(ItemSlot src, ItemSlot dst, decimal litersToMove)
        {
            ItemStack liquidStack = src.Itemstack;
            var liquidProps = BlockLiquidContainerBase.GetContainableProps(liquidStack);
            if (liquidProps == null || !liquidStack.Collectible.IsLiquid()) return 0;

            if (dst.Itemstack != null && !dst.Itemstack.Equals(api.World, liquidStack, GlobalConstants.IgnoredStackAttributes)) return 0;

            float currentLitres = dst.StackSize / liquidProps.ItemsPerLitre;
            float remainingLitres = ((ItemSlotWatertight)dst).capacityLitres - currentLitres;
            if (remainingLitres <= 0) return 0;

            float allowedLitres = Math.Min((float)litersToMove, remainingLitres);
            int moveQuantity = Math.Min(src.StackSize, (int)(liquidProps.ItemsPerLitre * allowedLitres));
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

        private int TryMoveLiquidContainerIntoWatertightSlot(ItemSlot src, ItemSlot dst, decimal litersToMove)
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
            float toMoveLitres = Math.Min((float)litersToMove, Math.Min(remainingLitres, sourceLitres));
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

        private ItemSlot GetTargetSlot(ItemSlot fromSlot, SignalsLink.src.signals.paperConditions.PaperConditionDirectives directives)
        {
            byte effectiveTargetSlotSignal = directives?.TargetSlot ?? outputSlotSignal;

            // OutputSlot: 1-based index, 0 = první vhodný
            if (effectiveTargetSlotSignal > 0)
            {
                int index = effectiveTargetSlotSignal - 1;
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

        private bool IsSmeltingCookingTarget(ItemSlot slot)
        {
            if (slot is not ItemSlotWatertight) return false;

            int slotIndex = targetInv.GetSlotId(slot);
            return slotIndex >= 3 && slotIndex <= 6;
        }

        private static int GetItemTransferQuantity(ItemSlot src, decimal requestedAmount)
        {
            if (requestedAmount <= 0) return 0;

            int quantity = (int)decimal.Truncate(requestedAmount);
            if (quantity <= 0) quantity = 1;

            return Math.Min(src.StackSize, quantity);
        }

        private static decimal NormalizeLiquidAmount(decimal amount)
        {
            if (amount <= 0) return 0;
            return decimal.Round(amount, 2, MidpointRounding.ToZero);
        }

        private bool ShouldRequireSmeltingLiquidTransfer(ItemSlot src, ItemSlot dst)
        {
            if (!"smelting".EqualsFastIgnoreCase(targetInv.ClassName)) return false;
            if (!IsSmeltingCookingTarget(dst)) return false;
            if (!HasCookingContainer()) return false;

            return GetLiquidStackForTransfer(src.Itemstack) != null;
        }

        private TransferOperationResult CreateLiquidTransferResult(ItemSlot src, int movedItems)
        {
            var liquidStack = GetLiquidStackForTransfer(src.Itemstack);
            var liquidProps = BlockLiquidContainerBase.GetContainableProps(liquidStack);
            if (liquidProps == null || liquidProps.ItemsPerLitre <= 0) return TransferOperationResult.None;

            decimal movedLitres = decimal.Round(movedItems / (decimal)liquidProps.ItemsPerLitre, 2, MidpointRounding.ToZero);
            return movedLitres > 0 ? new TransferOperationResult(movedLitres, 1) : TransferOperationResult.None;
        }
    }
}