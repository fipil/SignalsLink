using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.managedchute.transporting
{
    public class WorldToInventoryTransfer : IItemTransfer
    {
        private readonly ICoreAPI api;
        private readonly BlockPos sourcePos;
        private readonly IInventory targetInv;
        private readonly byte targetSlotSignal;
        private readonly PaperConditionsEvaluator conditionsEvaluator;

        // Where the target inventory lives. Optional only because a caller may not know it; with
        // it, `in target isBurning` and `do seal` work in this direction too.
        private readonly BlockPos targetPos;

        public WorldToInventoryTransfer(ICoreAPI api, BlockPos sourcePos, IInventory targetInv, byte targetSlotSignal, PaperConditionsEvaluator conditionsEvaluator, BlockPos targetPos = null)
        {
            this.api = api;
            this.sourcePos = sourcePos;
            this.targetInv = targetInv;
            this.targetSlotSignal = targetSlotSignal;
            this.conditionsEvaluator = conditionsEvaluator;
            this.targetPos = targetPos;
        }

        /// <summary>
        /// One evaluation pass. The paper is the outer loop and the ways of picking something up
        /// out of the world are the inner one, so a block higher on the paper gets to try every
        /// pickup before a block below it is asked at all — the same order of priority the other
        /// devices follow.
        /// </summary>
        public TransferOperationResult TryMove(ItemStackMoveOperation opTemplate)
        {
            IReadOnlyList<ConditionBlock> blocks = conditionsEvaluator?.GetBlocks();

            if (blocks == null || blocks.Count == 0)
            {
                OutputSink?.ApplyOutput(0, 0);   // no paper, nothing can drive the pin
                return TryPickUp(null);
            }

            TransferOperationResult moved = TransferOperationResult.None;
            IDictionary<string, object> outputCtx = null;

            DriverResult result = ConditionDriver.Run(
                blocks,
                false,
                block =>
                {
                    outputCtx ??= BuildOutputContext();
                    bool holds = block.OutputConditionsHold(outputCtx);

                    if (ConditionDebug.Enabled)
                    {
                        ConditionDebug.Log("  output block value=" + block.OutputValue + " holds=" + holds
                            + " | " + ConditionDebug.Describe(outputCtx, "targetInventory"));
                    }

                    return holds;
                },
                block =>
                {
                    moved = TryPickUp(block);
                    return moved.Success;
                });

            OutputSink?.ApplyOutput(0, result.GetOutput());
            return moved;
        }

        /// <summary>
        /// Every way of taking something out of the world, tried in turn for ONE block of the
        /// paper. <paramref name="block"/> null means there is no paper at all.
        /// </summary>
        private TransferOperationResult TryPickUp(ConditionBlock block)
        {
            TransferOperationResult fromPile = TryTakeFromGroundStorageColumn(block);
            if (fromPile.Success) return fromPile;

            TransferOperationResult fromLayers = TryTakeFromLayeredBlock(block);
            if (fromLayers.Success) return fromLayers;

            if (TryGetPlacedLiquidContainer(out ItemStack containerStack))
            {
                return TryMovePlacedLiquidContainer(containerStack, block);
            }

            EntityItem entity = FindItemEntityNearSource(block);
            if (entity == null || entity.Itemstack == null || entity.Itemstack.StackSize <= 0) return TransferOperationResult.None;

            ItemStack stack = entity.Itemstack;
            if (!TryGetMatchedDirectives(stack, out PaperConditionDirectives directives, null, block) || !directives.Evaluate(BuildDirectiveContext())) return TransferOperationResult.None;

            int moved = TryPutOneIntoInventory(stack, EffectiveTargetSlot(directives));
            if (moved <= 0) return TransferOperationResult.None;

            stack.StackSize -= moved;
            if (stack.StackSize <= 0)
            {
                entity.Die(EnumDespawnReason.PickedUp);
            }
            else
            {
                entity.Itemstack = stack;
            }

            return new TransferOperationResult(moved, moved, false);
        }

        /// <summary>
        /// Takes one item off the TOP of a ground-storage column standing on the source position.
        /// Physically you take from the top of a stack, and the chute only ever points at the
        /// bottom block of the column. Also refreshes the pile mesh (otherwise the pile keeps
        /// rendering its old size) and removes the block once it runs empty.
        /// </summary>
        private TransferOperationResult TryTakeFromGroundStorageColumn(ConditionBlock block)
        {
            List<BlockEntityGroundStorage> column = GetColumnTopDown();
            if (column.Count == 0) return TransferOperationResult.None;

            BlockEntityGroundStorage top = column[0];
            ItemSlot topSlot = top.Inventory?[0];
            if (topSlot == null || topSlot.Empty) return TransferOperationResult.None;

            ItemStack stack = topSlot.Itemstack;
            if (!TryGetMatchedDirectives(stack, out PaperConditionDirectives directives, top.Inventory, block)) return TransferOperationResult.None;
            if (!directives.Evaluate(BuildDirectiveContext())) return TransferOperationResult.None;

            // `amount N` takes a whole batch at once, spanning several piles of the column if
            // needed (mirrors the placing side). Whether it waits for the whole of N is what the
            // mark says: a plain amount and `N+` are floors, `N-` is a ceiling and never waits.
            int batch = 1;
            if (directives.Amount.HasValue)
            {
                batch = (int)decimal.Truncate(directives.Amount.Value);
                if (batch < 1) batch = 1;

                int available = 0;
                foreach (BlockEntityGroundStorage p in column) available += p.TotalStackSize;

                if (directives.IsAtomicAmount && available < batch) return TransferOperationResult.None;
                if (directives.TakesEverythingAvailable && available > batch) batch = available;
            }

            int targetSignal = EffectiveTargetSlot(directives);
            int movedTotal = 0;

            foreach (BlockEntityGroundStorage pile in column)
            {
                if (movedTotal >= batch) break;

                ItemSlot slot = pile.Inventory?[0];
                if (slot == null || slot.Empty) continue;

                // A pile of something else ends the run - we never mix items into one batch.
                if (!slot.Itemstack.Equals(api.World, stack, GlobalConstants.IgnoredStackAttributes)) break;

                int want = System.Math.Min(batch - movedTotal, slot.StackSize);
                int moved = TryPutIntoInventory(slot.Itemstack, targetSignal, want);
                if (moved <= 0) break; // target is full

                slot.Itemstack.StackSize -= moved;
                if (slot.Itemstack.StackSize <= 0) slot.Itemstack = null;
                slot.MarkDirty();
                movedTotal += moved;

                if (pile.TotalStackSize <= 0)
                {
                    // Don't leave an empty ghost pile behind.
                    api.World.BlockAccessor.SetBlock(0, pile.Pos);
                    api.World.BlockAccessor.MarkBlockDirty(pile.Pos);
                }
                else
                {
                    pile.MarkDirty(true); // redraw, or the pile keeps its old visual size
                }
            }

            if (movedTotal <= 0) return TransferOperationResult.None;
            return new TransferOperationResult(movedTotal, movedTotal, false);
        }

        #region Layered blocks (charcoal)

        /// <summary>
        /// Takes charcoal — or anything else built as a layered block — off the top of the column
        /// standing on the source position.
        ///
        /// A layered block is not a pile and needs its own path: charcoalpile has no block entity
        /// and no inventory at all, only a variant group counting its layers
        /// (<c>attributes.layerGroupCode</c>), which is why neither the ground-storage nor the
        /// item-entity route can see it. A layer is the unit that can be removed, so <c>amount N</c>
        /// — which counts PIECES — is converted into whole layers here, and a layer is never broken
        /// apart: it is taken whole or left alone, so nothing is lost when the target runs out of
        /// room halfway through one.
        /// </summary>
        private TransferOperationResult TryTakeFromLayeredBlock(ConditionBlock paperBlock)
        {
            List<BlockPos> column = GetLayeredColumnTopDown(out Block block, out string layerGroup);
            if (column.Count == 0) return TransferOperationResult.None;

            ItemStack layerStack = GetLayerDrop(block);
            if (layerStack?.Collectible == null || layerStack.StackSize <= 0) return TransferOperationResult.None;

            if (!TryGetMatchedDirectives(layerStack, out PaperConditionDirectives directives, null, paperBlock)) return TransferOperationResult.None;
            if (!directives.Evaluate(BuildDirectiveContext())) return TransferOperationResult.None;

            int perLayer = layerStack.StackSize;
            int requested = perLayer; // no directive: one layer per attempt, so it drains gradually

            if (directives.Amount.HasValue)
            {
                requested = (int)decimal.Truncate(directives.Amount.Value);
                if (requested < 1) requested = 1;

                // Same three forms as everywhere else; see PaperConditionDirectives.AmountMode.
                int available = 0;
                foreach (BlockPos p in column) available += GetLayerCount(api.World.BlockAccessor.GetBlock(p), layerGroup) * perLayer;

                if (directives.IsAtomicAmount && available < requested) return TransferOperationResult.None;
                if (directives.TakesEverythingAvailable && available > requested) requested = available;
            }

            int targetSignal = EffectiveTargetSlot(directives);
            int movedTotal = 0;

            foreach (BlockPos pos in column)
            {
                while (movedTotal < requested)
                {
                    Block cur = api.World.BlockAccessor.GetBlock(pos);
                    int layers = GetLayerCount(cur, layerGroup);
                    if (layers <= 0) break;

                    if (RoomFor(layerStack, targetSignal) < perLayer) return LayerResult(movedTotal);

                    int moved = TryPutIntoInventory(layerStack, targetSignal, perLayer);
                    if (moved <= 0) return LayerResult(movedTotal);

                    movedTotal += moved;
                    RemoveOneLayer(pos, cur, layers, layerGroup);
                }

                if (movedTotal >= requested) break;
            }

            return LayerResult(movedTotal);
        }

        private static TransferOperationResult LayerResult(int movedTotal)
        {
            return movedTotal > 0 ? new TransferOperationResult(movedTotal, movedTotal, false) : TransferOperationResult.None;
        }

        /// <summary>
        /// The contiguous column of one and the same layered block standing on the source position,
        /// topmost first — a stack is taken from the top. <paramref name="block"/> is the bottom
        /// one, which is what the drops and the layer group are read from.
        /// </summary>
        private List<BlockPos> GetLayeredColumnTopDown(out Block block, out string layerGroup)
        {
            block = null;
            layerGroup = null;

            IBlockAccessor ba = api.World.BlockAccessor;
            List<BlockPos> column = new List<BlockPos>();

            for (int dy = 0; dy < 64; dy++)
            {
                BlockPos pos = sourcePos.AddCopy(0, dy, 0);
                Block cur = ba.GetBlock(pos);
                string group = cur?.Attributes?["layerGroupCode"].AsString(null);
                if (group == null) break;

                if (block == null)
                {
                    block = cur;
                    layerGroup = group;
                }
                else if (cur.FirstCodePart() != block.FirstCodePart())
                {
                    break; // a different material ends the column; one batch never mixes items
                }

                column.Add(pos);
            }

            column.Reverse();
            return column;
        }

        private static int GetLayerCount(Block block, string layerGroup)
        {
            string value = block?.Variant?[layerGroup];
            return value != null && int.TryParse(value, out int layers) ? layers : 0;
        }

        /// <summary>
        /// What one layer is worth. The JSON drops of a layered block describe a single layer — the
        /// game multiplies them by the layer count when the whole block is broken — so one entry
        /// taken as-is is exactly one layer of yield.
        /// </summary>
        private static ItemStack GetLayerDrop(Block block)
        {
            BlockDropItemStack[] drops = block?.Drops;
            if (drops == null || drops.Length == 0) return null;

            return drops[0].GetNextItemStack();
        }

        /// <summary>
        /// Peels one layer off, or removes the block once its last layer is gone. SetBlock notifies
        /// the neighbours, so anything resting on top (charcoal carries UnstableFalling) collapses
        /// down by itself — which is what keeps a column feeding the endpoint below it.
        /// </summary>
        private void RemoveOneLayer(BlockPos pos, Block block, int layers, string layerGroup)
        {
            IBlockAccessor ba = api.World.BlockAccessor;

            if (layers <= 1)
            {
                ba.SetBlock(0, pos);
                ba.MarkBlockDirty(pos);
                return;
            }

            Block thinner = api.World.GetBlock(block.CodeWithVariant(layerGroup, (layers - 1).ToString()));
            if (thinner == null) return;

            ba.SetBlock(thinner.BlockId, pos);
            ba.MarkBlockDirty(pos);
        }

        /// <summary>
        /// How many pieces the target could still take. Asked BEFORE a layer is removed, because a
        /// layer cannot be put back once it is gone.
        /// </summary>
        /// <summary>Which target slot a block asks for: `target last`, `target N`, or the pin.</summary>
        private int EffectiveTargetSlot(PaperConditionDirectives directives)
        {
            if (directives == null) return targetSlotSignal;
            if (directives.TargetLast) return targetInv?.Count ?? 0;
            return directives.TargetSlot ?? targetSlotSignal;
        }

        private int RoomFor(ItemStack stack, int effectiveTargetSlotSignal)
        {
            if (effectiveTargetSlotSignal > 0)
            {
                int index = effectiveTargetSlotSignal - 1;
                if (index < 0 || index >= targetInv.Count) return 0;
                return RoomInSlot(targetInv[index], stack);
            }

            int room = 0;
            for (int i = 0; i < targetInv.Count; i++) room += RoomInSlot(targetInv[i], stack);
            return room;
        }

        private int RoomInSlot(ItemSlot slot, ItemStack stack)
        {
            if (slot == null || stack?.Collectible == null) return 0;
            if (!slot.CanHold(new DummySlot(stack))) return 0;
            if (slot.Empty) return stack.Collectible.MaxStackSize;
            if (!slot.Itemstack.Equals(api.World, stack, GlobalConstants.IgnoredStackAttributes)) return 0;

            return System.Math.Max(0, slot.Itemstack.Collectible.MaxStackSize - slot.StackSize);
        }

        #endregion

        /// <summary>The contiguous ground-storage column standing on the source position, topmost
        /// pile first - items are taken off the top of a stack.</summary>
        private List<BlockEntityGroundStorage> GetColumnTopDown()
        {
            IBlockAccessor ba = api.World.BlockAccessor;
            List<BlockEntityGroundStorage> piles = new List<BlockEntityGroundStorage>();

            for (int dy = 0; dy < 64; dy++)
            {
                if (ba.GetBlockEntity(sourcePos.AddCopy(0, dy, 0)) is not BlockEntityGroundStorage pile) break;
                piles.Add(pile);
            }

            piles.Reverse();
            return piles;
        }

        private TransferOperationResult TryMovePlacedLiquidContainer(ItemStack containerStack, ConditionBlock block)
        {
            if (!TryGetMatchedDirectives(containerStack, out PaperConditionDirectives directives, null, block) || !directives.Evaluate(BuildDirectiveContext())) return TransferOperationResult.None;

            int moved = TryPutOneIntoInventory(containerStack, EffectiveTargetSlot(directives));
            if (moved <= 0) return TransferOperationResult.None;

            api.World.BlockAccessor.SetBlock(0, sourcePos);
            api.World.BlockAccessor.MarkBlockModified(sourcePos);
            return new TransferOperationResult(moved, moved, false);
        }

        public int TryMoveOneItem(ItemStackMoveOperation opTemplate)
        {
            return (int)TryMove(opTemplate).MovedAmount;
        }

        public void EvaluateOutputs()
        {
            RunOutputRail();
        }

        // NOTE: TryMove runs the output rail as part of its own pass; RunOutputRail below is for
        // the ticks where the host cannot carry anything at all.

        /// <summary>
        /// The output rail of one evaluation pass. Picking things up out of the world is not slot
        /// based, so the action rail below is still the older per-candidate walk (see the note in
        /// paper-conditions-rules-v2.md); the pin, however, is computed the v2 way — from the
        /// current state on every pass, reading 0 when no output block holds, instead of freezing
        /// on whatever last moved it.
        /// </summary>
        private void RunOutputRail()
        {
            if (OutputSink == null) return;

            IReadOnlyList<ConditionBlock> blocks = conditionsEvaluator?.GetBlocks();
            if (blocks == null || blocks.Count == 0)
            {
                OutputSink.ApplyOutput(0, 0);
                return;
            }

            IDictionary<string, object> ctx = null;

            DriverResult result = ConditionDriver.Run(
                blocks,
                true,   // the action rail lives outside the driver here
                block =>
                {
                    ctx ??= BuildOutputContext();
                    bool holds = block.OutputConditionsHold(ctx);

                    if (ConditionDebug.Enabled)
                    {
                        ConditionDebug.Log("  output block value=" + block.OutputValue + " holds=" + holds
                            + " | " + ConditionDebug.Describe(ctx, "targetInventory"));
                    }

                    return holds;
                },
                null);

            OutputSink.ApplyOutput(0, result.GetOutput());
        }

        private IDictionary<string, object> BuildOutputContext()
        {
            return BuildConditionContext(null, null);
        }

        /// <summary>
        /// The two ends as the conditions see them. The source here is a spot in the world rather
        /// than an inventory, which is why it is given as a position - block-state conditions
        /// (`isBurning`) asked `in source` then have something to look at.
        /// </summary>
        private IDictionary<string, object> BuildConditionContext(ItemStack stack, IInventory sourceInv)
        {
            return ConditionContext.Build(api, stack, sourceInv, sourcePos, targetInv, targetPos);
        }

        private EntityItem FindItemEntityNearSource(ConditionBlock block)
        {
            IWorldAccessor world = api.World;

            var min = new Vec3d(sourcePos.X - 1, sourcePos.Y - 1, sourcePos.Z - 1);
            var max = new Vec3d(sourcePos.X + 2, sourcePos.Y + 2, sourcePos.Z + 2);

            EntityItem found = null;

            world.GetEntitiesInsideCuboid(min.AsBlockPos, max.AsBlockPos, e =>
            {
                if (e is not EntityItem itemEntity) return false;

                var stack = itemEntity.Itemstack;
                if (stack == null || stack.StackSize <= 0) return false;
                if (IsLiquidContainer(stack) || !IsConditionMet(stack, block)) return false;

                found = itemEntity;
                return true;
            });

            return found;
        }

        private int TryPutOneIntoInventory(ItemStack fromStack, int effectiveTargetSlotSignal)
        {
            return TryPutIntoInventory(fromStack, effectiveTargetSlotSignal, 1);
        }

        /// <summary>
        /// Moves up to <paramref name="maxCount"/> pieces into the target inventory. A batch usually
        /// does not fit one slot (a chest slot caps at the item's max stack size), so without a
        /// specific target slot it is spread over as many slots as needed.
        /// </summary>
        private int TryPutIntoInventory(ItemStack fromStack, int effectiveTargetSlotSignal, int maxCount)
        {
            if (fromStack == null || maxCount < 1) return 0;

            if (effectiveTargetSlotSignal > 0)
            {
                int index = effectiveTargetSlotSignal - 1;
                if (index < 0 || index >= targetInv.Count) return 0;
                return PutIntoSlot(targetInv[index], fromStack, maxCount);
            }

            int movedTotal = 0;
            for (int i = 0; i < targetInv.Count && movedTotal < maxCount; i++)
            {
                movedTotal += PutIntoSlot(targetInv[i], fromStack, maxCount - movedTotal);
            }

            return movedTotal;
        }

        private int PutIntoSlot(ItemSlot slot, ItemStack fromStack, int count)
        {
            if (slot == null || count < 1) return 0;

            ItemStack portion = fromStack.Clone();
            portion.StackSize = count;

            DummySlot dummy = new DummySlot(portion);
            int moved = dummy.TryPutInto(api.World, slot, count);
            if (moved > 0) slot.MarkDirty();

            return moved;
        }

        /// <summary>
        /// Whether the block at <paramref name="pos"/> may be picked up whole as an item. Barrel, boiler and
        /// condenser derive from BlockLiquidContainerBase too, but their block entities hold state (item slot,
        /// Sealed/CurrentRecipe, fuel, distillation) that moving them as an item would silently destroy.
        /// </summary>
        public static bool IsPortablePlacedContainer(IBlockAccessor blockAccessor, BlockPos pos)
        {
            if (blockAccessor.GetBlock(pos) is not BlockLiquidContainerBase) return false;
            return blockAccessor.GetBlockEntity(pos) is BlockEntityBucket;
        }

        private bool TryGetPlacedLiquidContainer(out ItemStack containerStack)
        {
            containerStack = null;

            if (!IsPortablePlacedContainer(api.World.BlockAccessor, sourcePos)) return false;
            if (api.World.BlockAccessor.GetBlock(sourcePos) is not BlockLiquidContainerBase container) return false;

            containerStack = new ItemStack(container, 1);
            container.SetContent(containerStack, container.GetContent(sourcePos)?.Clone());
            return true;
        }

        private bool IsLiquidContainer(ItemStack stack)
        {
            if (stack?.Collectible == null) return false;
            if (stack.Collectible is BlockLiquidContainerBase) return true;
            if (stack.Collectible is ILiquidInterface) return true;
            if (stack.Collectible.GetType().Name == "ItemLiquidPortion") return true;
            return false;
        }

        private bool IsConditionMet(ItemStack stack, ConditionBlock block)
        {
            return TryGetMatchedDirectives(stack, out _, null, block);
        }

        /// <summary>
        /// The host's Output pin, when it has one. Null for the ManagedChute, which has none.
        /// </summary>
        public IConditionOutputSink OutputSink { get; set; }

        /// <param name="block">
        /// The block of the paper currently being tried. The driver has already chosen it, so the
        /// question here is only whether it accepts this particular candidate. Null means there is
        /// no paper at all, and everything is accepted.
        /// </param>
        private bool TryGetMatchedDirectives(ItemStack stack, out PaperConditionDirectives directives, IInventory sourceInv = null, ConditionBlock block = null)
        {
            directives = PaperConditionDirectives.Empty;
            if (!conditionsEvaluator.HasConditions || block == null) return true;

            IDictionary<string, object> ctx = BuildConditionContext(stack, sourceInv);

            if (block.IsOutputBlock || !block.TryMatch(stack, ctx)) return false;

            directives = block.Directives;
            return true;
        }

        private IDictionary<string, object> BuildDirectiveContext()
        {
            return BuildConditionContext(null, null);
        }
    }
}