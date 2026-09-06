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

        public WorldToInventoryTransfer(ICoreAPI api, BlockPos sourcePos, IInventory targetInv, byte targetSlotSignal, PaperConditionsEvaluator conditionsEvaluator)
        {
            this.api = api;
            this.sourcePos = sourcePos;
            this.targetInv = targetInv;
            this.targetSlotSignal = targetSlotSignal;
            this.conditionsEvaluator = conditionsEvaluator;
        }

        public TransferOperationResult TryMove(ItemStackMoveOperation opTemplate)
        {
            TransferOperationResult fromPile = TryTakeFromGroundStorageColumn();
            if (fromPile.Success) return fromPile;

            if (TryGetPlacedLiquidContainer(out ItemStack containerStack))
            {
                return TryMovePlacedLiquidContainer(containerStack);
            }

            EntityItem entity = FindItemEntityNearSource();
            if (entity == null || entity.Itemstack == null || entity.Itemstack.StackSize <= 0) return TransferOperationResult.None;

            ItemStack stack = entity.Itemstack;
            if (!TryGetMatchedDirectives(stack, out PaperConditionDirectives directives) || !directives.Evaluate(BuildDirectiveContext())) return TransferOperationResult.None;

            int moved = TryPutOneIntoInventory(stack, directives.TargetSlot ?? targetSlotSignal);
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
        private TransferOperationResult TryTakeFromGroundStorageColumn()
        {
            List<BlockEntityGroundStorage> column = GetColumnTopDown();
            if (column.Count == 0) return TransferOperationResult.None;

            BlockEntityGroundStorage top = column[0];
            ItemSlot topSlot = top.Inventory?[0];
            if (topSlot == null || topSlot.Empty) return TransferOperationResult.None;

            ItemStack stack = topSlot.Itemstack;
            if (!TryGetMatchedDirectives(stack, out PaperConditionDirectives directives, top.Inventory)) return TransferOperationResult.None;
            if (!directives.Evaluate(BuildDirectiveContext())) return TransferOperationResult.None;

            // `amount N` takes a whole batch at once, spanning several piles of the column if needed
            // (mirrors the placing side). It is atomic on the source: the column must hold all of N.
            int batch = 1;
            if (directives.Amount.HasValue)
            {
                batch = (int)decimal.Truncate(directives.Amount.Value);
                if (batch < 1) batch = 1;

                int available = 0;
                foreach (BlockEntityGroundStorage p in column) available += p.TotalStackSize;
                if (available < batch) return TransferOperationResult.None;
            }

            byte targetSignal = directives.TargetSlot ?? targetSlotSignal;
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

        private TransferOperationResult TryMovePlacedLiquidContainer(ItemStack containerStack)
        {
            if (!TryGetMatchedDirectives(containerStack, out PaperConditionDirectives directives) || !directives.Evaluate(BuildDirectiveContext())) return TransferOperationResult.None;

            int moved = TryPutOneIntoInventory(containerStack, directives.TargetSlot ?? targetSlotSignal);
            if (moved <= 0) return TransferOperationResult.None;

            api.World.BlockAccessor.SetBlock(0, sourcePos);
            api.World.BlockAccessor.MarkBlockModified(sourcePos);
            return new TransferOperationResult(moved, moved, false);
        }

        public int TryMoveOneItem(ItemStackMoveOperation opTemplate)
        {
            return (int)TryMove(opTemplate).MovedAmount;
        }

        private EntityItem FindItemEntityNearSource()
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
                if (IsLiquidContainer(stack) || !IsConditionMet(stack)) return false;

                found = itemEntity;
                return true;
            });

            return found;
        }

        private int TryPutOneIntoInventory(ItemStack fromStack, byte effectiveTargetSlotSignal)
        {
            return TryPutIntoInventory(fromStack, effectiveTargetSlotSignal, 1);
        }

        /// <summary>
        /// Moves up to <paramref name="maxCount"/> pieces into the target inventory. A batch usually
        /// does not fit one slot (a chest slot caps at the item's max stack size), so without a
        /// specific target slot it is spread over as many slots as needed.
        /// </summary>
        private int TryPutIntoInventory(ItemStack fromStack, byte effectiveTargetSlotSignal, int maxCount)
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

        private bool IsConditionMet(ItemStack stack)
        {
            return TryGetMatchedDirectives(stack, out _);
        }

        /// <summary>
        /// The host's Output pin, when it has one. Null for the ManagedChute, which has none.
        /// </summary>
        public IConditionOutputSink OutputSink { get; set; }

        private bool TryGetMatchedDirectives(ItemStack stack, out PaperConditionDirectives directives, IInventory sourceInv = null)
        {
            directives = PaperConditionDirectives.Empty;
            if (!conditionsEvaluator.HasConditions) return true;

            var ctx = ItemConditionContextUtil.BuildContext(api.World, stack);
            ctx["targetInventory"] = targetInv;
            if (sourceInv != null)
            {
                ctx["sourceInventory"] = sourceInv;
                ctx["inventory"] = sourceInv;
            }
            return ConditionResolution.ResolveDirectives(conditionsEvaluator, OutputSink, stack, ctx, out directives);
        }

        private IDictionary<string, object> BuildDirectiveContext()
        {
            return new Dictionary<string, object>
            {
                ["targetInventory"] = targetInv
            };
        }
    }
}