using System.Collections.Generic;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.managedchute.transporting
{
    /// <summary>
    /// Builds a firepit that is already standing at the target, stage by stage. Chosen by the
    /// factory the same way the anvil is — the target is recognised by what is there, not by a
    /// signal — but it only does anything when the paper says <c>target firepit</c>.
    ///
    /// Starting a firepit on bare ground is the other half and lives in
    /// <see cref="InventoryToWorldTransfer"/>, because there the target is open air.
    /// </summary>
    public class InventoryToFirepitTransfer : InventorySourcedTransferBase, IItemTransfer
    {
        private readonly BlockPos targetPos;

        public InventoryToFirepitTransfer(ICoreAPI api, IInventory sourceInv, BlockPos targetPos, byte inputSlotSignal, PaperConditionsEvaluator conditionsEvaluator)
            : base(api, sourceInv, inputSlotSignal, conditionsEvaluator)
        {
            this.targetPos = targetPos;
        }

        protected override void AddConditionContext(IDictionary<string, object> ctx)
        {
            if (targetPos != null) ctx["targetBlockPos"] = targetPos;
        }

        /// <summary>Only a stack that the next stage actually calls for is worth selecting.</summary>
        protected override bool CanTransferSelection(ItemSlot slot, PaperConditionDirectives directives)
        {
            if (!directives.TargetFirepit) return false;

            Block target = api.World.BlockAccessor.GetBlock(targetPos);
            return FirepitConstruction.Matches(target, slot?.Itemstack);
        }

        public TransferOperationResult TryMove(ItemStackMoveOperation opTemplate)
        {
            TransferSelection selection = GetTransferSelection();
            ItemSlot src = selection?.SourceSlot;
            if (src == null || src.Empty) return TransferOperationResult.None;
            if (!selection.Directives.TargetFirepit) return TransferOperationResult.None;

            if (!FirepitConstruction.Advance(api.World, targetPos, src.Itemstack)) return TransferOperationResult.None;

            // One stage costs exactly one item; a firepit is not something you pour in.
            src.TakeOut(1);
            src.MarkDirty();

            return new TransferOperationResult(1, 1, false);
        }

        public int TryMoveOneItem(ItemStackMoveOperation opTemplate)
        {
            return (int)TryMove(opTemplate).MovedAmount;
        }
    }
}
