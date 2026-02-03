using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.managedchute.transporting
{
    public static class ItemTransferFactory
    {
        // Zjednodušené API: vytvoø pøenos podle toho, co je na input/output pozici.
        public static IItemTransfer CreateTransfer(ICoreAPI api, BlockPos inputPos, BlockPos outputPos, byte inputSlotSignal, byte outputSlotSignal, PaperConditionsEvaluator conditionsEvaluator)
        {
            var blockAccess = api.World.BlockAccessor;

            var beIn = blockAccess.GetBlockEntity(inputPos) as BlockEntityContainer;

            // Special case: output points to an anvil -> use InventoryToAnvilTransfer
            var beAnvil = blockAccess.GetBlockEntity(outputPos) as BlockEntityAnvil;
            if (beIn?.Inventory != null && beAnvil != null)
            {
                return new InventoryToAnvilTransfer(api, beIn.Inventory, beAnvil, inputSlotSignal, conditionsEvaluator);
            }

            var beOut = blockAccess.GetBlockEntity(outputPos) as BlockEntityContainer;

            if (beIn?.Inventory != null && beOut?.Inventory != null)
            {
                // inventáø -> inventáø
                return new InventoryToInventoryTransfer(api, beIn.Inventory, beOut.Inventory, inputSlotSignal, outputSlotSignal, conditionsEvaluator);
            }

            if (beIn?.Inventory != null && beOut == null && beAnvil == null)
            {
                // inventáø -> svìt
                Block blockAtTarget = blockAccess.GetBlock(outputPos);
                bool canUseWorldTransfer =
                    blockAtTarget.Replaceable >= 6000 ||
                    blockAccess.GetBlockEntity<BlockEntityItemPile>(outputPos) != null;

                if (canUseWorldTransfer)
                {
                    return new InventoryToWorldTransfer(api, beIn.Inventory, inputSlotSignal, outputPos, outputSlotSignal, conditionsEvaluator);
                }

                return null;
            }

            if (beIn == null && beOut?.Inventory != null)
            {
                // svìt -> inventáø (WorldToInventoryTransfer)
                return new WorldToInventoryTransfer(api, inputPos, beOut.Inventory, outputSlotSignal);
            }

            return null;
        }
    }
}