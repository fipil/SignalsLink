using System.Collections.Generic;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.managedchute.transporting
{
    /// <summary>
    /// Special transfer: inventory -> anvil work item.
    /// Ignores outputSlotSignal. Uses AnvilAutoPlacer to put items onto the anvil.
    ///
    /// Paper conditions support:
    /// - `in source` conditions pick the source slot (via the base class),
    /// - `in target` conditions see the anvil as a synthetic one-slot inventory holding its
    ///   current work item, so `inventoryEmpty` / `inventoryFilled` / code conditions all work,
    /// - `amount N` places N units and is **atomic** — either all N land on the anvil, or nothing
    ///   is placed and nothing is taken from the source.
    /// </summary>
    public class InventoryToAnvilTransfer : InventorySourcedTransferBase, IItemTransfer
    {
        private readonly BlockEntityAnvil targetAnvil;
        private readonly AnvilAutoPlacer autoPlacer;

        // Cached one-slot view of the anvil work item, used as the `in target` inventory.
        private InventoryGeneric anvilTargetInv;

        public InventoryToAnvilTransfer(ICoreAPI api, IInventory sourceInv, BlockEntityAnvil targetAnvil, byte inputSlotSignal, PaperConditionsEvaluator conditionsEvaluator)
            : base(api, sourceInv, inputSlotSignal, conditionsEvaluator)
        {
            this.targetAnvil = targetAnvil;
            this.autoPlacer = new AnvilAutoPlacer();
        }

        /// <summary>
        /// The anvil is not an IInventory, so `in target` conditions had nothing to look at.
        /// Expose its work item as a throw-away one-slot inventory (read-only for conditions).
        /// </summary>
        protected override void AddConditionContext(IDictionary<string, object> ctx)
        {
            ctx["targetInventory"] = BuildAnvilTargetInventory();
            if (targetAnvil?.Pos != null) ctx["targetBlockPos"] = targetAnvil.Pos;
        }

        private IInventory BuildAnvilTargetInventory()
        {
            // NOTE: the inventory id MUST contain a dash — VS derives className/instanceId from it
            // by splitting on '-', so an id without one throws IndexOutOfRangeException.
            if (anvilTargetInv == null)
            {
                anvilTargetInv = new InventoryGeneric(1, "signalslink-anviltarget", api);
            }

            // Refresh the single slot from the anvil's current work item (cached instance, so we
            // don't allocate a new inventory on every condition evaluation).
            ItemStack workItem = targetAnvil?.WorkItemStack;
            anvilTargetInv[0].Itemstack = workItem?.Clone();
            anvilTargetInv[0].MarkDirty();

            return anvilTargetInv;
        }

        public TransferOperationResult TryMove(ItemStackMoveOperation opTemplate)
        {
            if (targetAnvil == null) return TransferOperationResult.None;

            // Source slot chosen by the signals + paper conditions (incl. any `in target` ones).
            TransferSelection selection = GetTransferSelection();
            ItemSlot src = selection?.SourceSlot;
            if (src == null || src.Empty) return TransferOperationResult.None;

            ItemStack stack = src.Itemstack;
            if (stack == null || stack.StackSize <= 0) return TransferOperationResult.None;

            // `amount N` = how many units to place (default 1, i.e. the previous behaviour).
            int requestedUnits = 1;
            decimal? amount = selection.Directives?.Amount;
            if (amount.HasValue)
            {
                requestedUnits = (int)decimal.Truncate(amount.Value);
                if (requestedUnits < 1) requestedUnits = 1;
            }

            // Atomic: the selected slot must hold the whole amount, otherwise we don't start.
            if (src.StackSize < requestedUnits) return TransferOperationResult.None;

            ItemStack input = stack.Clone();
            input.StackSize = requestedUnits;

            // Atomic placement: on partial success the anvil is rolled back and nothing is taken.
            if (!autoPlacer.TryPlaceUnitsAtomic(targetAnvil, input, requestedUnits, out int placed, out string _))
            {
                return TransferOperationResult.None;
            }

            src.TakeOut(placed);
            src.MarkDirty();

            // Buffer model B: the cost is the number of pieces actually moved.
            return new TransferOperationResult(placed, placed, false);
        }

        public int TryMoveOneItem(ItemStackMoveOperation opTemplate)
        {
            int carried = MoveOneItem(opTemplate);

            // The block that carried may also have said to DO something; now that the goods have
            // really moved is when it means it.
            if (carried > 0) RunActionsAfterTransfer();

            return carried;
        }

        private int MoveOneItem(ItemStackMoveOperation opTemplate)
        {
            return (int)TryMove(opTemplate).MovedAmount;
        }
    }
}
