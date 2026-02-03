using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.managedchute.transporting
{
    /// <summary>
    /// Special transfer: inventory -> anvil work item.
    /// Ignores outputSlotSignal. Uses AnvilAutoPlacer to put items onto the anvil.
    /// </summary>
    public class InventoryToAnvilTransfer : InventorySourcedTransferBase, IItemTransfer
    {
        private readonly BlockEntityAnvil targetAnvil;
        private readonly AnvilAutoPlacer autoPlacer;

        public InventoryToAnvilTransfer(ICoreAPI api, IInventory sourceInv, BlockEntityAnvil targetAnvil, byte inputSlotSignal, PaperConditionsEvaluator conditionsEvaluator)
            : base(api, sourceInv, inputSlotSignal, conditionsEvaluator)
        {
            this.targetAnvil = targetAnvil;
            this.autoPlacer = new AnvilAutoPlacer();
        }

        public int TryMoveOneItem(ItemStackMoveOperation opTemplate)
        {
            // Find source slot using base logic (signals + paper conditions)
            ItemSlot src = GetSourceSlot();
            if (src == null || src.Empty) return 0;

            // Anvil must exist and be empty, AnvilAutoPlacer validates more details
            if (targetAnvil == null) return 0;

            ItemStack stack = src.Itemstack;
            if (stack == null || stack.StackSize <= 0) return 0;

            // Use a single unit for placement; let auto placer handle recipe etc.
            var unitStack = stack.Clone();
            unitStack.StackSize = 1;

            if (!autoPlacer.TryPlaceAndSelectRecipe(targetAnvil, unitStack, out string _))
            {
                return 0;
            }

            // Placement succeeded -> decrease source stack and mark dirty
            src.TakeOut(1);
            src.MarkDirty();

            return 1;
        }
    }
}
