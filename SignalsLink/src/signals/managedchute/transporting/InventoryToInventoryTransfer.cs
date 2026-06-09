using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
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
        private readonly BlockPos targetPos;
        private readonly byte outputSlotSignal;

        public InventoryToInventoryTransfer(ICoreAPI api, IInventory sourceInv, IInventory targetInv, BlockPos targetPos, byte inputSlotSignal, byte outputSlotSignal, PaperConditionsEvaluator conditionsEvaluator) 
            : base(api, sourceInv, inputSlotSignal, conditionsEvaluator)
        {
            this.targetInv = targetInv;
            this.targetPos = targetPos;
            this.outputSlotSignal = outputSlotSignal;

            canTransferLiquids = HasAnyLiquidTargetSlot();
        }

        public override bool UsesAmountAsTriggerOnly => true;

        protected override void AddConditionContext(IDictionary<string, object> ctx)
        {
            ctx["targetInventory"] = targetInv;
        }

        protected override bool CanTransferSelection(ItemSlot slot, PaperConditionDirectives directives)
        {
            ItemStack liquidStack = GetLiquidStackForTransfer(slot?.Itemstack);
            if (liquidStack == null) return true;

            return GetTargetSlot(slot, directives) != null;
        }

        public TransferOperationResult TryMove(ItemStackMoveOperation opTemplate)
        {
            TransferSelection selection = GetTransferSelection();
            ItemSlot src = selection?.SourceSlot;
            if (src == null || src.Empty) return TransferOperationResult.None;

            ItemSlot dst = GetTargetSlot(src, selection.Directives);
            if (dst == null) return TransferOperationResult.None;

            decimal requestedAmount = selection.Directives.Amount ?? opTemplate.RequestedQuantity;

            TransferOperationResult liquidResult = TryMoveLiquidBetweenSlots(src, dst, requestedAmount, selection.Directives.HasAmountOverride);
            if (liquidResult.Success)
            {
                src.MarkDirty();
                dst.MarkDirty();
                return liquidResult;
            }

            if (ShouldRequireLiquidTransfer(src, dst))
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
                return new TransferOperationResult(moved, triggerCost, false);
            }

            return TransferOperationResult.None;
        }

        public int TryMoveOneItem(ItemStackMoveOperation opTemplate)
        {
            return (int)TryMove(opTemplate).MovedAmount;
        }

        private TransferOperationResult TryMoveLiquidBetweenSlots(ItemSlot src, ItemSlot dst, decimal requestedAmount, bool hasAmountOverride)
        {
            if (src.Itemstack == null || src.Itemstack.StackSize <= 0) return TransferOperationResult.None;
            if (!CanAcceptLiquid(dst)) return TransferOperationResult.None;

            decimal litersToMove = NormalizeLiquidAmount(requestedAmount);
            if (litersToMove <= 0) return TransferOperationResult.None;

            int moved = TryMoveLiquidStackIntoWatertightSlot(src, dst, litersToMove);
            if (moved > 0) return CreateLiquidTransferResult(src, moved, hasAmountOverride);

            moved = TryMoveLiquidContainerIntoWatertightSlot(src, dst, litersToMove);
            if (moved > 0) return CreateLiquidTransferResult(src, moved, hasAmountOverride);

            moved = TryMoveLiquidIntoBlockContainer(src, dst, litersToMove);
            if (moved > 0) return CreateLiquidTransferResult(src, moved, hasAmountOverride);

            return TransferOperationResult.None;
        }

        private int TryMoveLiquidStackIntoWatertightSlot(ItemSlot src, ItemSlot dst, decimal litersToMove)
        {
            ItemStack liquidStack = src.Itemstack;
            var liquidProps = BlockLiquidContainerBase.GetContainableProps(liquidStack);
            if (liquidProps == null || !liquidStack.Collectible.IsLiquid()) return 0;

            if (!TryGetRemainingLiquidCapacityLitres(dst, liquidStack, out float remainingLitres)) return 0;
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

            if (!TryGetRemainingLiquidCapacityLitres(dst, liquidStack, out float remainingLitres)) return 0;
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

        private int TryMoveLiquidIntoBlockContainer(ItemSlot src, ItemSlot dst, decimal litersToMove)
        {
            if (targetPos == null) return 0;

            Block targetBlock = api.World.BlockAccessor.GetBlock(targetPos);
            if (targetBlock is not ILiquidSink liquidSink) return 0;

            ItemStack liquidStack = GetLiquidStackForTransfer(src.Itemstack);
            if (liquidStack == null) return 0;

            int moved = liquidSink.TryPutLiquid(targetPos, liquidStack, (float)litersToMove);
            if (moved <= 0) return 0;

            if (src.Itemstack?.Block is BlockLiquidContainerBase liquidContainer)
            {
                int quantityPerContainer = moved / src.Itemstack.StackSize;
                if (quantityPerContainer <= 0) return 0;

                ItemStack taken = liquidContainer.TryTakeContent(src.Itemstack, quantityPerContainer);
                return taken == null ? 0 : taken.StackSize * src.Itemstack.StackSize;
            }

            src.TakeOut(moved);
            return moved;
        }

        private ItemSlot GetTargetSlot(ItemSlot fromSlot, SignalsLink.src.signals.paperConditions.PaperConditionDirectives directives)
        {
            byte effectiveTargetSlotSignal = directives?.TargetSlot ?? outputSlotSignal;
            ItemStack liquidStack = GetLiquidStackForTransfer(fromSlot?.Itemstack);

            // OutputSlot: 1-based index, 0 = první vhodný
            if (effectiveTargetSlotSignal > 0)
            {
                int index = effectiveTargetSlotSignal - 1;
                if (index >= 0 && index < targetInv.Count)
                {
                    ItemSlot explicitSlot = targetInv[index];
                    return liquidStack == null || CanAcceptLiquid(explicitSlot, index) ? explicitSlot : null;
                }
                return null;
            }

            // 0 → první vhodný slot (prázdný nebo slučitelný)
            ItemStack stack = fromSlot.Itemstack;
            if (stack == null) return null;

            ItemSlot liquidTargetSlot = GetLiquidTargetSlot(liquidStack);
            if (liquidTargetSlot != null) return liquidTargetSlot;

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

        private ItemSlot GetLiquidTargetSlot(ItemStack liquidStack)
        {
            if (liquidStack == null) return null;

            for (int i = 0; i < targetInv.Count; i++)
            {
                ItemSlot slot = targetInv[i];
                if (!CanAcceptLiquid(slot, i)) continue;
                if (TryGetRemainingLiquidCapacityLitres(slot, liquidStack, i, out float remainingLitres) && remainingLitres > 0) return slot;
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

        private bool ShouldRequireLiquidTransfer(ItemSlot src, ItemSlot dst)
        {
            if (GetLiquidStackForTransfer(src.Itemstack) == null) return false;
            return CanAcceptLiquid(dst);
        }

        private TransferOperationResult CreateLiquidTransferResult(ItemSlot src, int movedItems, bool hasAmountOverride)
        {
            var liquidStack = GetLiquidStackForTransfer(src.Itemstack);
            var liquidProps = BlockLiquidContainerBase.GetContainableProps(liquidStack);
            if (liquidProps == null || liquidProps.ItemsPerLitre <= 0) return TransferOperationResult.None;

            decimal movedLitres = decimal.Round(movedItems / (decimal)liquidProps.ItemsPerLitre, 2, MidpointRounding.ToZero);
            int triggerCost = hasAmountOverride ? 1 : (int)movedLitres;
            if (triggerCost <= 0) triggerCost = 1;

            return movedLitres > 0 ? new TransferOperationResult(movedLitres, triggerCost, true) : TransferOperationResult.None;
        }

        private bool CanAcceptLiquid(ItemSlot slot)
        {
            int slotIndex = slot == null ? -1 : targetInv.GetSlotId(slot);
            return CanAcceptLiquid(slot, slotIndex);
        }

        private bool TryGetRemainingLiquidCapacityLitres(ItemSlot slot, ItemStack liquidStack, out float remainingLitres)
        {
            int slotIndex = slot == null ? -1 : targetInv.GetSlotId(slot);
            return TryGetRemainingLiquidCapacityLitres(slot, liquidStack, slotIndex, out remainingLitres);
        }

        private bool TryGetRemainingLiquidCapacityLitres(ItemSlot slot, ItemStack liquidStack, int slotIndex, out float remainingLitres)
        {
            remainingLitres = 0;

            if (!CanAcceptLiquid(slot, slotIndex) || liquidStack == null) return false;

            var liquidProps = BlockLiquidContainerBase.GetContainableProps(liquidStack);
            if (liquidProps == null || liquidProps.ItemsPerLitre <= 0) return false;

            if (RequiresBlockLevelLiquidSink(slot, slotIndex))
            {
                float blockCapacityLitres = GetBlockLevelCapacityLitres();
                if (blockCapacityLitres <= 0) return false;

                float blockCurrentLitres = slot.StackSize / liquidProps.ItemsPerLitre;
                remainingLitres = blockCapacityLitres - blockCurrentLitres;
                return remainingLitres >= 0;
            }

            if (slot.Itemstack != null && !slot.Itemstack.Equals(api.World, liquidStack, GlobalConstants.IgnoredStackAttributes)) return false;

            float currentLitres = slot.StackSize / liquidProps.ItemsPerLitre;
            float capacityLitres = GetLiquidSlotCapacityLitres(slot);
            remainingLitres = capacityLitres - currentLitres;
            return true;
        }

        private bool CanAcceptLiquid(ItemSlot slot, int slotIndex)
        {
            if (slot == null) return false;
            if (slot is ItemSlotWatertight) return !IsSmeltingCookingTarget(slot) || HasCookingContainer();
            if (slot is ItemSlotLiquidOnly) return true;
            if (RequiresBlockLevelLiquidSink(slot, slotIndex)) return true;
            return false;
        }

        private bool RequiresBlockLevelLiquidSink(ItemSlot slot, int slotIndex)
        {
            if (slotIndex != 0) return false;
            if (slot is ItemSlotWatertight || slot is ItemSlotLiquidOnly) return false;

            return api.World.BlockAccessor.GetBlock(targetPos) is ILiquidSink;
        }

        private float GetBlockLevelCapacityLitres()
        {
            if (api.World.BlockAccessor.GetBlock(targetPos) is ILiquidInterface liquidInterface)
            {
                return liquidInterface.CapacityLitres;
            }

            return 0;
        }

        private static float GetLiquidSlotCapacityLitres(ItemSlot slot)
        {
            return slot switch
            {
                ItemSlotWatertight watertightSlot => watertightSlot.capacityLitres,
                ItemSlotLiquidOnly liquidOnlySlot => liquidOnlySlot.CapacityLitres,
                _ => 0
            };
        }

        private bool HasAnyLiquidTargetSlot()
        {
            if (outputSlotSignal > 0)
            {
                int index = outputSlotSignal - 1;
                if (index < 0 || index >= targetInv.Count) return false;

                return CanAcceptLiquid(targetInv[index]);
            }

            for (int i = 0; i < targetInv.Count; i++)
            {
                if (CanAcceptLiquid(targetInv[i])) return true;
            }

            return false;
        }
    }
}