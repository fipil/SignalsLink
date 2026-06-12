using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using SignalsLink.src.signals.paperConditions;

namespace SignalsLink.src.signals.managedchute.transporting
{
    public class InventoryToInventoryTransfer : InventorySourcedTransferBase, IItemTransfer
    {
        private readonly IInventory targetInv;
        private readonly byte outputSlotSignal;
        private readonly LiquidTransferService liquidTransferService;

        public InventoryToInventoryTransfer(ICoreAPI api, IInventory sourceInv, IInventory targetInv, BlockPos targetPos, byte inputSlotSignal, byte outputSlotSignal, PaperConditionsEvaluator conditionsEvaluator)
            : base(api, sourceInv, inputSlotSignal, conditionsEvaluator)
        {
            this.targetInv = targetInv;
            this.outputSlotSignal = outputSlotSignal;
            liquidTransferService = new LiquidTransferService(api, targetInv, targetPos);
            canTransferLiquids = liquidTransferService.HasAnyLiquidTargetSlot(outputSlotSignal);
        }

        public override bool UsesAmountAsTriggerOnly => true;

        protected override void AddConditionContext(IDictionary<string, object> ctx)
        {
            liquidTransferService.AddConditionContext(ctx);
        }

        protected override bool CanTransferSelection(ItemSlot slot, PaperConditionDirectives directives)
        {
            ItemStack liquidStack = liquidTransferService.GetLiquidStackForTransfer(slot?.Itemstack);
            if (liquidStack == null) return true;

            byte effectiveTargetSlotSignal = directives?.TargetSlot ?? outputSlotSignal;
            return liquidTransferService.GetTargetSlot(slot.Itemstack, effectiveTargetSlotSignal) != null;
        }

        public TransferOperationResult TryMove(ItemStackMoveOperation opTemplate)
        {
            TransferSelection selection = GetTransferSelection();
            ItemSlot src = selection?.SourceSlot;
            if (src == null || src.Empty) return TransferOperationResult.None;

            byte effectiveTargetSlotSignal = selection.Directives?.TargetSlot ?? outputSlotSignal;
            ItemSlot dst = liquidTransferService.GetTargetSlot(src.Itemstack, effectiveTargetSlotSignal) ?? GetGenericTargetSlot(src.Itemstack, effectiveTargetSlotSignal);
            if (dst == null) return TransferOperationResult.None;

            decimal requestedAmount = selection.Directives.Amount ?? opTemplate.RequestedQuantity;

            TransferOperationResult liquidResult = liquidTransferService.TryMoveFromItemSlot(src, dst, requestedAmount, selection.Directives.HasAmountOverride);
            if (liquidResult.Success)
            {
                src.MarkDirty();
                dst.MarkDirty();
                return liquidResult;
            }

            if (liquidTransferService.RequiresLiquidTransfer(src.Itemstack, dst))
            {
                return TransferOperationResult.None;
            }

            ItemStackMoveOperation op = new ItemStackMoveOperation(
                opTemplate.World,
                opTemplate.MouseButton,
                opTemplate.Modifiers,
                opTemplate.CurrentPriority,
                GetItemTransferQuantity(requestedAmount)
            );

            int moved = TryMoveItemsFromMatchingSourceSlots(src, dst, ref op, selection.Directives);
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

        private ItemSlot GetGenericTargetSlot(ItemStack stack, byte targetSlotSignal)
        {
            if (stack == null) return null;

            if (targetSlotSignal > 0)
            {
                int index = targetSlotSignal - 1;
                if (index >= 0 && index < targetInv.Count)
                {
                    return targetInv[index];
                }

                return null;
            }

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

        private static int GetItemTransferQuantity(decimal requestedAmount)
        {
            if (requestedAmount <= 0) return 0;

            int quantity = (int)decimal.Truncate(requestedAmount);
            if (quantity <= 0) quantity = 1;

            return quantity;
        }

        private int TryMoveItemsFromMatchingSourceSlots(ItemSlot initialSourceSlot, ItemSlot dst, ref ItemStackMoveOperation op, PaperConditionDirectives directives)
        {
            int movedTotal = 0;
            int requestedQuantity = op.RequestedQuantity;

            foreach (ItemSlot candidate in GetMatchingSourceSlots(initialSourceSlot, directives))
            {
                if (movedTotal >= requestedQuantity) break;

                int remaining = requestedQuantity - movedTotal;
                if (remaining <= 0) break;

                var candidateOp = new ItemStackMoveOperation(
                    op.World,
                    op.MouseButton,
                    op.Modifiers,
                    op.CurrentPriority,
                    remaining
                );

                int movedNow = candidate.TryPutInto(dst, ref candidateOp);
                if (movedNow <= 0) continue;

                movedTotal += movedNow;
                candidate.MarkDirty();
            }

            op.MovedQuantity = movedTotal;
            return movedTotal;
        }

        private IEnumerable<ItemSlot> GetMatchingSourceSlots(ItemSlot initialSourceSlot, PaperConditionDirectives directives)
        {
            if (initialSourceSlot?.Itemstack == null) yield break;

            ItemStack initialStack = initialSourceSlot.Itemstack;

            yield return initialSourceSlot;

            for (int i = 0; i < sourceInv.Count; i++)
            {
                ItemSlot slot = sourceInv[i];
                if (slot == null || ReferenceEquals(slot, initialSourceSlot) || slot.Empty) continue;

                ItemStack stack = slot.Itemstack;
                if (stack?.Collectible != initialStack.Collectible) continue;
                if (!stack.Equals(api.World, initialStack, GlobalConstants.IgnoredStackAttributes)) continue;
                if (IsLiquidContainer(stack)) continue;
                if (!TryGetMatchedDirectives(stack, out PaperConditionDirectives candidateDirectives)) continue;
                if (candidateDirectives.TargetSlot != directives.TargetSlot || candidateDirectives.Amount != directives.Amount || candidateDirectives.RequireTargetEmpty != directives.RequireTargetEmpty) continue;
                if (!CanTransferSelection(slot, candidateDirectives)) continue;

                yield return slot;
            }
        }
    }
}