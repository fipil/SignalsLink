using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
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
            if (effectiveTargetSlotSignal > 0)
            {
                int index = effectiveTargetSlotSignal - 1;
                if (index >= 0 && index < targetInv.Count)
                {
                    ItemSlot slot = targetInv[index];
                    if (slot != null)
                    {
                        ItemStack one = fromStack.Clone();
                        one.StackSize = 1;

                        DummySlot dummy = new DummySlot(one);
                        int moved = dummy.TryPutInto(api.World, slot, 1);
                        if (moved > 0)
                        {
                            slot.MarkDirty();
                            return moved;
                        }
                    }
                }

                return 0;
            }

            for (int i = 0; i < targetInv.Count; i++)
            {
                ItemSlot slot = targetInv[i];
                if (slot == null) continue;

                ItemStack one = fromStack.Clone();
                one.StackSize = 1;

                DummySlot dummy = new DummySlot(one);
                int moved = dummy.TryPutInto(api.World, slot, 1);
                if (moved > 0)
                {
                    slot.MarkDirty();
                    return moved;
                }
            }

            return 0;
        }

        private bool TryGetPlacedLiquidContainer(out ItemStack containerStack)
        {
            containerStack = null;

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

        private bool TryGetMatchedDirectives(ItemStack stack, out PaperConditionDirectives directives)
        {
            directives = PaperConditionDirectives.Empty;
            if (!conditionsEvaluator.HasConditions) return true;

            var ctx = ItemConditionContextUtil.BuildContext(api.World, stack);
            ctx["targetInventory"] = targetInv;
            return conditionsEvaluator.Evaluate(stack, ctx, out byte _, out directives);
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