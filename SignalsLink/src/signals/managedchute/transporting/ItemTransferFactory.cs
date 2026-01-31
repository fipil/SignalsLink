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
            if (beIn?.Inventory == null) return null;

            var beOut = api.World.BlockAccessor.GetBlockEntity(outputPos) as BlockEntityContainer;
            if (beOut?.Inventory != null)
            {
                // Inventáø -> inventáø
                return new InventoryToInventoryTransfer(api, beIn.Inventory, beOut.Inventory, inputSlotSignal, outputSlotSignal);
            }

            // Inventáø -> svìt (vzduch/replaceable)
            return new InventoryToWorldTransfer(api, beIn.Inventory, inputSlotSignal, outputPos);
        }
    }
}