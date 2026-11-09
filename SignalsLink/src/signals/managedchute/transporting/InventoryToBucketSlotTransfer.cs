using System.Collections.Generic;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;

namespace SignalsLink.src.signals.managedchute.transporting
{
    /// <summary>
    /// Puts a bucket on a bucket stand (<see cref="IBucketSlotEnd"/>). One container at a time,
    /// only while the stand is empty, only what the stand would take from a player's hand.
    /// `in target` sees the stand as a one-slot inventory.
    /// </summary>
    public class InventoryToBucketSlotTransfer : InventorySourcedTransferBase, IItemTransfer
    {
        private readonly IBucketSlotEnd end;

        public InventoryToBucketSlotTransfer(ICoreAPI api, IInventory sourceInv, IBucketSlotEnd end, byte inputSlotSignal, PaperConditionsEvaluator conditionsEvaluator)
            : base(api, sourceInv, inputSlotSignal, conditionsEvaluator)
        {
            this.end = end;
        }

        protected override bool AllowsLiquidContainers => true;

        protected override IInventory TargetInventory => end.View;

        protected override void AddConditionContext(IDictionary<string, object> ctx)
        {
            ctx["targetInventory"] = end.View;
            if (end.Pos != null) ctx["targetBlockPos"] = end.Pos;
        }

        protected override bool CanTransferSelection(ItemSlot slot, PaperConditionDirectives directives)
        {
            return end.Slot.Empty && end.Accepts(slot?.Itemstack);
        }

        public TransferOperationResult TryMove(ItemStackMoveOperation opTemplate)
            => RunTransferPass(opTemplate, MoveSelected);

        private TransferOperationResult MoveSelected()
        {
            ItemSlot src = GetTransferSelection()?.SourceSlot;
            if (src == null || src.Empty) return TransferOperationResult.None;
            if (!end.Slot.Empty || !end.Accepts(src.Itemstack)) return TransferOperationResult.None;

            ItemStack taken = src.TakeOut(1);
            if (taken == null) return TransferOperationResult.None;

            end.Slot.Itemstack = taken;
            end.Slot.MarkDirty();
            src.MarkDirty();
            end.Changed(api);

            return new TransferOperationResult(1, 1, false);
        }

        public int TryMoveOneItem(ItemStackMoveOperation opTemplate)
            => (int)TryMove(opTemplate).MovedAmount;
    }
}
