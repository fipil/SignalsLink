using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.managedchute.transporting
{
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
        }

        public override bool UsesAmountAsTriggerOnly => true;

        protected override void AddConditionContext(IDictionary<string, object> ctx)
        {
            // The `in target` scope, the `target ... ifEmpty` directive and the `do seal`
            // action all require the ctx to know the target inventory.
            ctx["targetInventory"] = targetInv;
        }

        public TransferOperationResult TryMove(ItemStackMoveOperation opTemplate)
        {
            ExecuteMatchingActions();

            TransferSelection selection = GetTransferSelection();
            ItemSlot src = selection?.SourceSlot;
            if (src == null || src.Empty) return TransferOperationResult.None;

            byte effectiveTargetSlotSignal = selection.Directives?.TargetSlot ?? outputSlotSignal;
            ItemSlot dst = GetGenericTargetSlot(src.Itemstack, effectiveTargetSlotSignal);
            if (dst == null) return TransferOperationResult.None;

            decimal requestedAmount = selection.Directives.Amount ?? opTemplate.RequestedQuantity;

            int requestedQuantity = GetItemTransferQuantity(requestedAmount);
            if (selection.Directives.HasAmountOverride && GetAvailableMatchingSourceQuantity(src, selection.Directives) < requestedQuantity)
            {
                return TransferOperationResult.None;
            }

            ItemStackMoveOperation op = new ItemStackMoveOperation(
                opTemplate.World,
                opTemplate.MouseButton,
                opTemplate.Modifiers,
                opTemplate.CurrentPriority,
                requestedQuantity
            );

            int moved = TryMoveItemsFromMatchingSourceSlots(src, dst, ref op, selection.Directives);
            if (moved > 0)
            {
                src.MarkDirty();
                dst.MarkDirty();
                ExecuteMatchingActions();
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

        private int GetAvailableMatchingSourceQuantity(ItemSlot initialSourceSlot, PaperConditionDirectives directives)
        {
            int totalQuantity = 0;

            foreach (ItemSlot slot in GetMatchingSourceSlots(initialSourceSlot, directives))
            {
                totalQuantity += slot.StackSize;
            }

            return totalQuantity;
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

        private void ExecuteMatchingActions()
        {
            if (!conditionsEvaluator.HasConditions) return;

            var ctx = BuildActionContext();
            var actions = conditionsEvaluator.GetMatchingActions(null, ctx);
            for (int i = 0; i < actions.Count; i++)
            {
                actions[i].Execute(ctx);
            }
        }

        private IDictionary<string, object> BuildActionContext()
        {
            var ctx = new Dictionary<string, object>
            {
                ["sourceInventory"] = sourceInv,
                ["targetInventory"] = targetInv,
                ["inventory"] = sourceInv
            };

            if (targetPos != null)
            {
                ctx["targetBlockPos"] = targetPos;
                BlockEntityBarrel barrel = api.World.BlockAccessor.GetBlockEntity(targetPos) as BlockEntityBarrel;
                if (barrel != null)
                {
                    ctx["targetBlockEntity"] = barrel;
                }
            }

            AddConditionContext(ctx);
            return ctx;
        }
    }
}