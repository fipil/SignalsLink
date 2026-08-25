using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.managedchute.transporting
{
    public static class ItemTransferFactory
    {
        // Zjednodu�en� API: vytvo� p�enos podle toho, co je na input/output pozici.
        public static IItemTransfer CreateTransfer(ICoreAPI api, BlockPos inputPos, BlockPos outputPos, byte inputSlotSignal, byte outputSlotSignal, PaperConditionsEvaluator conditionsEvaluator)
        {
            var blockAccess = api.World.BlockAccessor;

            var beIn = blockAccess.GetBlockEntity(inputPos) as IBlockEntityContainer;
            var inputBlock = blockAccess.GetBlock(inputPos);

            // Special case: output points to an anvil -> use InventoryToAnvilTransfer
            var beAnvil = blockAccess.GetBlockEntity(outputPos) as BlockEntityAnvil;
            if (beIn?.Inventory != null && beAnvil != null)
            {
                return new InventoryToAnvilTransfer(api, beIn.Inventory, beAnvil, inputSlotSignal, conditionsEvaluator);
            }

            var beOut = blockAccess.GetBlockEntity(outputPos) as IBlockEntityContainer;

            if (inputBlock is BlockLiquidContainerBase && beOut?.Inventory != null)
            {
                return new WorldToInventoryTransfer(api, inputPos, beOut.Inventory, outputSlotSignal, conditionsEvaluator);
            }

            if (beIn?.Inventory != null && beOut?.Inventory != null)
            {
                // invent�� -> invent��
                return new InventoryToInventoryTransfer(api, beIn.Inventory, beOut.Inventory, outputPos, inputSlotSignal, outputSlotSignal, conditionsEvaluator);
            }

            if (beIn?.Inventory != null && beOut == null && beAnvil == null)
            {
                // invent�� -> sv�t
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
                // sv�t -> invent�� (WorldToInventoryTransfer)
                return new WorldToInventoryTransfer(api, inputPos, beOut.Inventory, outputSlotSignal, conditionsEvaluator);
            }

            return null;
        }
    }
}