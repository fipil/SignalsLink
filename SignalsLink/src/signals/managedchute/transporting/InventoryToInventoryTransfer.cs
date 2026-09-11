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

        protected override bool AllowsLiquidContainers => true;

        protected override void AddConditionContext(IDictionary<string, object> ctx)
        {
            // The `in target` scope, the `target ... ifEmpty` directive and the `do seal`
            // action all require the ctx to know the target inventory.
            ctx["targetInventory"] = targetInv;

            // Block-state conditions (isBurning) ask about the block, not its inventory.
            if (targetPos != null) ctx["targetBlockPos"] = targetPos;
        }

        protected override bool CanTransferSelection(ItemSlot slot, PaperConditionDirectives directives)
        {
            if (!CanReachTarget(slot, directives)) return false;

            // An `amount N` batch is atomic - N pieces or nothing - so a block that cannot gather N
            // from the slots holding the same thing does no work at all, and a block that does no
            // work must not win the pass. This used to be checked only when the move was already
            // under way, by which time the block had won and the ones below it were never asked:
            // four medium hides in the chest were enough for `amount 12` to select the block, move
            // nothing, and starve the `amount 8` block below it of its turn.
            if (directives.IsAtomicAmount
                && GetAvailableMatchingSourceQuantity(slot, directives) < GetItemTransferQuantity(directives.Amount.Value))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Whether this slot has anywhere to go — the half of the check that says nothing about
        /// amounts.
        ///
        /// The gathering loop must ask THIS and never the full check: gathering is how the
        /// available amount gets counted in the first place, so calling back into a check that
        /// counts it would be endless. Splitting it in two is what makes that impossible rather
        /// than merely avoided.
        /// </summary>
        private bool CanReachTarget(ItemSlot slot, PaperConditionDirectives directives)
        {
            if (IsLooseLiquid(slot))
            {
                if (!CarriesLiquid) return false;

                return Liquid.GetTargetSlot(slot.Itemstack, EffectiveTargetSlot(directives)) != null;
            }

            return GetGenericTargetSlot(slot, EffectiveTargetSlot(directives)) != null;
        }

        /// <summary>
        /// May this device carry loose liquid at all? False by default: moving liquid without a
        /// hose is what the chute and the damper are deliberately not for. The dock sets it.
        /// </summary>
        public bool CarriesLiquid { get; set; }

        /// <summary>
        /// Liquid out of a slot rather than out of a hose. Measured in litres and counted by the
        /// target's capacity, so it cannot go through the ordinary stack path at all - which is
        /// why it used to be refused outright.
        /// </summary>
        private static bool IsLooseLiquid(ItemSlot slot)
        {
            return slot?.Itemstack?.Collectible?.GetType().Name == "ItemLiquidPortion";
        }

        private LiquidTransferService Liquid => liquid ??= new LiquidTransferService(api, targetInv, targetPos);

        private LiquidTransferService liquid;

        private TransferOperationResult TryMoveLiquid(TransferSelection selection, ItemSlot src, decimal litres)
        {
            ItemSlot dst = Liquid.GetTargetSlot(src.Itemstack, EffectiveTargetSlot(selection.Directives));
            if (dst == null) return TransferOperationResult.None;

            TransferOperationResult result =
                Liquid.TryMoveFromItemSlot(src, dst, litres, selection.Directives.IsAtomicAmount);

            if (!result.Success) return TransferOperationResult.None;

            src.MarkDirty();
            dst.MarkDirty();
            RunActionsAfterTransfer();

            return result;
        }

        public TransferOperationResult TryMove(ItemStackMoveOperation opTemplate)
        {
            TransferSelection selection = GetTransferSelection();
            ItemSlot src = selection?.SourceSlot;
            if (src == null || src.Empty) return TransferOperationResult.None;

            if (CarriesLiquid && IsLooseLiquid(src))
            {
                return TryMoveLiquid(selection, src,
                    selection.Directives.Amount ?? opTemplate.RequestedQuantity);
            }

            int effectiveTargetSlotSignal = EffectiveTargetSlot(selection.Directives);
            ItemSlot dst = GetGenericTargetSlot(src, effectiveTargetSlotSignal);
            if (dst == null) return TransferOperationResult.None;

            decimal requestedAmount = selection.Directives.Amount ?? opTemplate.RequestedQuantity;

            int requestedQuantity = GetItemTransferQuantity(requestedAmount);
            int available = GetAvailableMatchingSourceQuantity(src, selection.Directives);

            // A floor - `amount N` or `amount N+` - waits until the whole of it is there. A ceiling
            // - `amount N-` - never waits; fewer than ten is fewer than ten.
            if (selection.Directives.IsAtomicAmount && available < requestedQuantity)
            {
                return TransferOperationResult.None;
            }

            // And past the floor, `amount N+` reaches for everything: that is what makes it "clear
            // the lot in one go" rather than "ten at a time".
            if (selection.Directives.TakesEverythingAvailable && available > requestedQuantity)
            {
                requestedQuantity = available;
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
                RunActionsAfterTransfer();
                // Buffer model B: cost = pieces actually moved, so the Input buffer counts real
                // items (an `amount M` block subtracts M, not a flat 1 — no more multiplier).
                int triggerCost = moved;
                return new TransferOperationResult(moved, triggerCost, false);
            }

            return TransferOperationResult.None;
        }

        public int TryMoveOneItem(ItemStackMoveOperation opTemplate)
        {
            return (int)TryMove(opTemplate).MovedAmount;
        }

        /// <summary>Which target slot a block asks for: `target last`, `target N`, or the pin.</summary>
        private int EffectiveTargetSlot(PaperConditionDirectives directives)
        {
            if (directives == null) return outputSlotSignal;
            if (directives.TargetLast) return targetInv.Count;
            return directives.TargetSlot ?? outputSlotSignal;
        }

        private ItemSlot GetGenericTargetSlot(ItemSlot sourceSlot, int targetSlotSignal)
        {
            ItemStack stack = sourceSlot?.Itemstack;
            if (stack == null) return null;

            if (targetSlotSignal > 0)
            {
                int index = targetSlotSignal - 1;
                if (index >= 0 && index < targetInv.Count)
                {
                    ItemSlot slot = targetInv[index];
                    return CanMoveToTarget(sourceSlot, slot) ? slot : null;
                }

                return null;
            }

            for (int i = 0; i < targetInv.Count; i++)
            {
                ItemSlot slot = targetInv[i];

                if (slot.Empty && CanMoveToTarget(sourceSlot, slot)) return slot;

                if (slot.Itemstack != null &&
                    slot.Itemstack.Equals(api.World, stack, GlobalConstants.IgnoredStackAttributes) &&
                    slot.Itemstack.StackSize < slot.Itemstack.Collectible.MaxStackSize &&
                    CanMoveToTarget(sourceSlot, slot))
                {
                    return slot;
                }
            }

            return null;
        }

        private static bool CanMoveToTarget(ItemSlot sourceSlot, ItemSlot targetSlot)
        {
            return sourceSlot != null && targetSlot != null &&
                targetSlot.CanTakeFrom(sourceSlot, EnumMergePriority.DirectMerge) &&
                targetSlot.CanHold(sourceSlot);
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

        // Materialize the matching source slots EAGERLY (into a list), evaluated against the
        // current target state ONCE. This must not be lazy: while gathering the full `amount`
        // across several source slots we partially fill the target, and a `target N ifEmpty`
        // directive would then flip subsequent candidates onto a different block (target N+1),
        // dropping them from the gather. Building the list up front (before any item is moved)
        // keeps every matching slot bound to the block that was valid when the transfer started.
        private List<ItemSlot> GetMatchingSourceSlots(ItemSlot initialSourceSlot, PaperConditionDirectives directives)
        {
            List<ItemSlot> result = new List<ItemSlot>();
            if (initialSourceSlot?.Itemstack == null) return result;

            ItemStack initialStack = initialSourceSlot.Itemstack;

            result.Add(initialSourceSlot);

            for (int i = 0; i < sourceInv.Count; i++)
            {
                ItemSlot slot = sourceInv[i];
                if (slot == null || ReferenceEquals(slot, initialSourceSlot) || slot.Empty) continue;

                ItemStack stack = slot.Itemstack;
                if (stack?.Collectible != initialStack.Collectible) continue;

                if (!stack.Equals(api.World, initialStack, GlobalConstants.IgnoredStackAttributes)) continue;
                if (IsLiquidContainer(stack) && !AllowsLiquidContainers) continue;
                if (!TryGetMatchedDirectives(stack, out PaperConditionDirectives candidateDirectives)) continue;
                if (candidateDirectives.SourceSlot != directives.SourceSlot || candidateDirectives.TargetSlot != directives.TargetSlot || candidateDirectives.TargetGround != directives.TargetGround || candidateDirectives.TargetGroundHeight != directives.TargetGroundHeight || candidateDirectives.Amount != directives.Amount || candidateDirectives.RequireTargetEmpty != directives.RequireTargetEmpty) continue;
                if (!CanReachTarget(slot, candidateDirectives)) continue;

                result.Add(slot);
            }
            return result;
        }

    }
}