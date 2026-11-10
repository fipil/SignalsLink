using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.managedchute.transporting
{
    public static class ItemTransferFactory
    {
        // Zjednodušené API: vytvoř přenos podle toho, co je na input/output pozici.
        /// <summary>
        /// Builds the transfer for a pair of positions. <paramref name="outputSink"/> is the host's
        /// Output pin; pass it only from a host that has one (the Damper), and paper conditions may
        /// then drive it with <c>output N</c>. Left null — as the ManagedChute leaves it — condition
        /// resolution stays exactly as it has always been. <paramref name="devicePos"/> is the
        /// device itself; a registered end may need to know from which side it is approached.
        /// </summary>
        public static IItemTransfer CreateTransfer(ICoreAPI api, BlockPos inputPos, BlockPos outputPos, byte inputSlotSignal, byte outputSlotSignal, PaperConditionsEvaluator conditionsEvaluator, IConditionOutputSink outputSink = null, BlockPos devicePos = null)
        {
            IItemTransfer transfer = CreateCore(api, inputPos, outputPos, inputSlotSignal, outputSlotSignal, conditionsEvaluator, devicePos);

            IItemTransfer inner = transfer;
            while (inner is EndRefreshTransfer wrapped) inner = wrapped.Inner;

            switch (inner)
            {
                case InventorySourcedTransferBase inventorySourced: inventorySourced.OutputSink = outputSink; break;
                case WorldToInventoryTransfer worldSourced: worldSourced.OutputSink = outputSink; break;
            }

            return transfer;
        }

        private static IItemTransfer CreateCore(ICoreAPI api, BlockPos inputPos, BlockPos outputPos, byte inputSlotSignal, byte outputSlotSignal, PaperConditionsEvaluator conditionsEvaluator, BlockPos devicePos)
        {
            var blockAccess = api.World.BlockAccessor;

            // Ends another mod offers through the registry come first: they claim their block
            // explicitly, and are read and written through their view alone.
            ITransferEnd registeredSource = TransferEndRegistry.Find(api, inputPos, devicePos);
            ITransferEnd registeredTarget = TransferEndRegistry.Find(api, outputPos, devicePos);

            if (registeredTarget != null)
            {
                // The press top gives its mash by its own rules only, to a registered end too.
                if (registeredSource == null && FruitPressAccess.TopAt(blockAccess, inputPos) is BlockEntityFruitPress mashSource)
                {
                    return Wrap(api, new PressMashToInventoryTransfer(api, mashSource, registeredTarget.View, outputPos, outputSlotSignal, conditionsEvaluator), registeredTarget);
                }

                IInventory from = registeredSource?.View ?? SourceInventory(blockAccess, inputPos);
                if (from == null) return null; // never from the world
                return Wrap(api, Wrap(api, new InventoryToInventoryTransfer(api, from, registeredTarget.View, outputPos, inputSlotSignal, outputSlotSignal, conditionsEvaluator), registeredTarget), registeredSource);
            }

            if (registeredSource != null)
            {
                // A stand, the press top and a boiler keep their own way in for a registered source.
                IItemTransfer own = IntoOwnedTarget(api, blockAccess, registeredSource.View, outputPos, inputSlotSignal, conditionsEvaluator, out bool ownedTarget);
                if (ownedTarget) return Wrap(api, own, registeredSource);

                var into = AutomationAccess.EntityAt(blockAccess, outputPos) as IBlockEntityContainer;
                if (into?.Inventory == null) return null;
                return Wrap(api, new InventoryToInventoryTransfer(api, registeredSource.View, into.Inventory, outputPos, inputSlotSignal, outputSlotSignal, conditionsEvaluator), registeredSource);
            }

            var beIn = AutomationAccess.EntityAt(blockAccess, inputPos) as IBlockEntityContainer;

            // The fruit press through its top block is its mash: fruit in, mash out. Its bottom
            // block is a bucket stand, like the condenser. These ends are never written to or read
            // from by the plain routes below - a bucket in the mash slot is a broken press.
            if (FruitPressAccess.TopAt(blockAccess, inputPos) is BlockEntityFruitPress pressIn)
            {
                // Mash goes into a plain container only; a stand, a press or a boiler has no slot for it.
                IntoOwnedTarget(api, blockAccess, null, outputPos, inputSlotSignal, conditionsEvaluator, out bool mashIntoOwned);
                if (mashIntoOwned) return null;

                var mashTarget = AutomationAccess.EntityAt(blockAccess, outputPos) as IBlockEntityContainer;
                if (mashTarget?.Inventory == null) return null;
                return new PressMashToInventoryTransfer(api, pressIn, mashTarget.Inventory, outputPos, outputSlotSignal, conditionsEvaluator);
            }

            // A bucket stand on the input side is read as its bucket slot alone, through the usual
            // routes; the wrapper redraws the stand after a move.
            IBucketSlotEnd sourceEnd = BucketSlotEnds.At(blockAccess, inputPos);
            IInventory inInv = sourceEnd?.View ?? beIn?.Inventory;

            IItemTransfer owned = IntoOwnedTarget(api, blockAccess, inInv, outputPos, inputSlotSignal, conditionsEvaluator, out bool isOwnedTarget);
            if (isOwnedTarget) return Wrap(api, owned, sourceEnd);

            IItemTransfer transfer = CreateRouted(api, inputPos, outputPos, inputSlotSignal, outputSlotSignal, conditionsEvaluator, inInv, beIn != null);
            return Wrap(api, transfer, sourceEnd);
        }

        /// <summary>
        /// The dedicated transfer into a bucket stand, the press top or a boiler - the targets no
        /// plain route may write to. <paramref name="owned"/> says the target is one of them; the
        /// result is then null when there is nothing to carry from.
        /// </summary>
        private static IItemTransfer IntoOwnedTarget(ICoreAPI api, IBlockAccessor blockAccess, IInventory from, BlockPos outputPos, byte inputSlotSignal, PaperConditionsEvaluator conditionsEvaluator, out bool owned)
        {
            owned = true;

            IBucketSlotEnd targetEnd = BucketSlotEnds.At(blockAccess, outputPos);
            if (targetEnd != null)
            {
                return from == null ? null : new InventoryToBucketSlotTransfer(api, from, targetEnd, inputSlotSignal, conditionsEvaluator);
            }

            if (FruitPressAccess.TopAt(blockAccess, outputPos) is BlockEntityFruitPress pressOut)
            {
                return from == null ? null : new InventoryToPressMashTransfer(api, from, pressOut, inputSlotSignal, conditionsEvaluator);
            }

            // A boiler's firepit lives in its block entity, so the boiler is always built/fed this
            // way. Its single slot is liquid and belongs to the hose; no item path leads into it.
            if (AutomationAccess.EntityAt(blockAccess, outputPos) is BlockEntityBoiler boiler)
            {
                return from == null ? null : new InventoryToFirepitTransfer(api, from, boiler.Pos, inputSlotSignal, conditionsEvaluator, boiler.Inventory);
            }

            owned = false;
            return null;
        }

        private static IItemTransfer Wrap(ICoreAPI api, IItemTransfer transfer, ITransferEnd end)
        {
            if (transfer == null || end == null) return transfer;
            return new EndRefreshTransfer(api, transfer, end);
        }

        /// <summary>The inventory a plain source position offers: a bucket stand's slot or a container.</summary>
        private static IInventory SourceInventory(IBlockAccessor blockAccess, BlockPos inputPos)
        {
            IBucketSlotEnd sourceEnd = BucketSlotEnds.At(blockAccess, inputPos);
            if (sourceEnd != null) return sourceEnd.View;
            return (AutomationAccess.EntityAt(blockAccess, inputPos) as IBlockEntityContainer)?.Inventory;
        }

        private static IItemTransfer CreateRouted(ICoreAPI api, BlockPos inputPos, BlockPos outputPos, byte inputSlotSignal, byte outputSlotSignal, PaperConditionsEvaluator conditionsEvaluator, IInventory inInv, bool inIsContainer)
        {
            var blockAccess = api.World.BlockAccessor;

            // Special case: output points to an anvil -> use InventoryToAnvilTransfer
            var beAnvil = blockAccess.GetBlockEntity(outputPos) as BlockEntityAnvil;
            if (inInv != null && beAnvil != null)
            {
                return new InventoryToAnvilTransfer(api, inInv, beAnvil, inputSlotSignal, conditionsEvaluator);
            }

            // A firepit still under construction is built, not filled. Its block entity does have
            // an inventory, so without this it would fall into the plain inventory path and items
            // would be stuffed into a pit that has nowhere to put them yet. A FINISHED firepit is
            // deliberately left alone below - loading fuel into one is an ordinary transfer.
            if (inInv != null && FirepitConstruction.IsUnderConstruction(blockAccess.GetBlock(outputPos)))
            {
                return new InventoryToFirepitTransfer(api, inInv, outputPos, inputSlotSignal, conditionsEvaluator);
            }

            var beOut = AutomationAccess.EntityAt(blockAccess, outputPos) as IBlockEntityContainer;

            // Only carryable containers get moved as a whole block; a barrel or boiler on the input side
            // falls through to the regular inventory -> inventory transfer below.
            if (beOut?.Inventory != null && WorldToInventoryTransfer.IsPortablePlacedContainer(blockAccess, inputPos))
            {
                return new WorldToInventoryTransfer(api, inputPos, beOut.Inventory, outputSlotSignal, conditionsEvaluator, outputPos);
            }

            // A ground-storage pile on the output side is handled by the world transfer, not by the
            // plain inventory path: only the world transfer can create a new pile and grow the
            // column upward. (Picking up FROM a pile is unaffected - that is the beIn side.)
            if (inInv != null && blockAccess.GetBlockEntity(outputPos) is BlockEntityGroundStorage)
            {
                return new InventoryToWorldTransfer(api, inInv, inputSlotSignal, outputPos, outputSlotSignal, conditionsEvaluator);
            }

            // Ground-storage pile on the INPUT side: take from the TOP of the column, not from the
            // single pile the chute happens to point at, and refresh/remove the pile afterwards.
            if (beOut?.Inventory != null && blockAccess.GetBlockEntity(inputPos) is BlockEntityGroundStorage)
            {
                return new WorldToInventoryTransfer(api, inputPos, beOut.Inventory, outputSlotSignal, conditionsEvaluator, outputPos);
            }

            if (inInv != null && beOut?.Inventory != null)
            {
                // inventář -> inventář
                return new InventoryToInventoryTransfer(api, inInv, beOut.Inventory, outputPos, inputSlotSignal, outputSlotSignal, conditionsEvaluator);
            }

            if (inInv != null && beOut == null && beAnvil == null)
            {
                // inventář -> svět
                Block blockAtTarget = blockAccess.GetBlock(outputPos);
                bool canUseWorldTransfer =
                    blockAtTarget.Replaceable >= 6000 ||
                    blockAccess.GetBlockEntity<BlockEntityItemPile>(outputPos) != null;

                if (canUseWorldTransfer)
                {
                    return new InventoryToWorldTransfer(api, inInv, inputSlotSignal, outputPos, outputSlotSignal, conditionsEvaluator);
                }

                return null;
            }

            if (!inIsContainer && beOut?.Inventory != null)
            {
                // svět -> inventář (WorldToInventoryTransfer)
                return new WorldToInventoryTransfer(api, inputPos, beOut.Inventory, outputSlotSignal, conditionsEvaluator, outputPos);
            }

            return null;
        }
    }
}
