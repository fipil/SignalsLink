using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.managedchute.transporting
{
    public static class ItemTransferFactory
    {
        // Zjednodu�en� API: vytvo� p�enos podle toho, co je na input/output pozici.
        /// <summary>
        /// Builds the transfer for a pair of positions. <paramref name="outputSink"/> is the host's
        /// Output pin; pass it only from a host that has one (the Damper), and paper conditions may
        /// then drive it with <c>output N</c>. Left null — as the ManagedChute leaves it — condition
        /// resolution stays exactly as it has always been.
        /// </summary>
        public static IItemTransfer CreateTransfer(ICoreAPI api, BlockPos inputPos, BlockPos outputPos, byte inputSlotSignal, byte outputSlotSignal, PaperConditionsEvaluator conditionsEvaluator, IConditionOutputSink outputSink = null)
        {
            IItemTransfer transfer = CreateCore(api, inputPos, outputPos, inputSlotSignal, outputSlotSignal, conditionsEvaluator);

            switch (transfer)
            {
                case InventorySourcedTransferBase inventorySourced: inventorySourced.OutputSink = outputSink; break;
                case WorldToInventoryTransfer worldSourced: worldSourced.OutputSink = outputSink; break;
            }

            return transfer;
        }

        private static IItemTransfer CreateCore(ICoreAPI api, BlockPos inputPos, BlockPos outputPos, byte inputSlotSignal, byte outputSlotSignal, PaperConditionsEvaluator conditionsEvaluator)
        {
            var blockAccess = api.World.BlockAccessor;

            var beIn = blockAccess.GetBlockEntity(inputPos) as IBlockEntityContainer;

            // Special case: output points to an anvil -> use InventoryToAnvilTransfer
            var beAnvil = blockAccess.GetBlockEntity(outputPos) as BlockEntityAnvil;
            if (beIn?.Inventory != null && beAnvil != null)
            {
                return new InventoryToAnvilTransfer(api, beIn.Inventory, beAnvil, inputSlotSignal, conditionsEvaluator);
            }

            // A firepit still under construction is built, not filled. Its block entity does have
            // an inventory, so without this it would fall into the plain inventory path and items
            // would be stuffed into a pit that has nowhere to put them yet. A FINISHED firepit is
            // deliberately left alone below - loading fuel into one is an ordinary transfer.
            if (beIn?.Inventory != null && FirepitConstruction.IsUnderConstruction(blockAccess.GetBlock(outputPos)))
            {
                return new InventoryToFirepitTransfer(api, beIn.Inventory, outputPos, inputSlotSignal, conditionsEvaluator);
            }

            var beOut = blockAccess.GetBlockEntity(outputPos) as IBlockEntityContainer;

            // Only carryable containers get moved as a whole block; a barrel or boiler on the input side
            // falls through to the regular inventory -> inventory transfer below.
            if (beOut?.Inventory != null && WorldToInventoryTransfer.IsPortablePlacedContainer(blockAccess, inputPos))
            {
                return new WorldToInventoryTransfer(api, inputPos, beOut.Inventory, outputSlotSignal, conditionsEvaluator, outputPos);
            }

            // A ground-storage pile on the output side is handled by the world transfer, not by the
            // plain inventory path: only the world transfer can create a new pile and grow the
            // column upward. (Picking up FROM a pile is unaffected - that is the beIn side.)
            if (beIn?.Inventory != null && blockAccess.GetBlockEntity(outputPos) is BlockEntityGroundStorage)
            {
                return new InventoryToWorldTransfer(api, beIn.Inventory, inputSlotSignal, outputPos, outputSlotSignal, conditionsEvaluator);
            }

            // Ground-storage pile on the INPUT side: take from the TOP of the column, not from the
            // single pile the chute happens to point at, and refresh/remove the pile afterwards.
            if (beOut?.Inventory != null && blockAccess.GetBlockEntity(inputPos) is BlockEntityGroundStorage)
            {
                return new WorldToInventoryTransfer(api, inputPos, beOut.Inventory, outputSlotSignal, conditionsEvaluator, outputPos);
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
                return new WorldToInventoryTransfer(api, inputPos, beOut.Inventory, outputSlotSignal, conditionsEvaluator, outputPos);
            }

            return null;
        }
    }
}