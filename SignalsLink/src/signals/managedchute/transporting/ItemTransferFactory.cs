using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.managedchute.transporting
{
    public static class ItemTransferFactory
    {
        // Zjednodušené API: vytvoø pøenos podle toho, co je na input/output pozici.
        public static IItemTransfer CreateTransfer(ICoreAPI api, BlockPos inputPos, BlockPos outputPos, byte inputSlotSignal, byte outputSlotSignal)
        {
            var beIn = api.World.BlockAccessor.GetBlockEntity(inputPos) as BlockEntityContainer;
            var beOut = api.World.BlockAccessor.GetBlockEntity(outputPos) as BlockEntityContainer;

            if (beIn?.Inventory != null && beOut?.Inventory != null)
            {
                // inventáø -> inventáø
                return new InventoryToInventoryTransfer(api, beIn.Inventory, beOut.Inventory, inputSlotSignal, outputSlotSignal);
            }

            if (beIn?.Inventory != null && beOut == null)
            {
                // inventáø -> svìt
                Block blockAtTarget = api.World.BlockAccessor.GetBlock(outputPos);
                bool canUseWorldTransfer =
                    blockAtTarget.Replaceable >= 6000 ||
                    api.World.BlockAccessor.GetBlockEntity<BlockEntityItemPile>(outputPos) != null;

                if (canUseWorldTransfer)
                {
                    return new InventoryToWorldTransfer(api, beIn.Inventory, inputSlotSignal, outputPos, outputSlotSignal);
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