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
        private readonly LiquidTransferService liquidTransferService;

        public WorldToInventoryTransfer(ICoreAPI api, BlockPos sourcePos, IInventory targetInv, byte targetSlotSignal, PaperConditionsEvaluator conditionsEvaluator)
        {
            this.api = api;
            this.sourcePos = sourcePos;
            this.targetInv = targetInv;
            this.targetSlotSignal = targetSlotSignal;
            this.conditionsEvaluator = conditionsEvaluator;
            liquidTransferService = new LiquidTransferService(api, targetInv, null);
        }

        public TransferOperationResult TryMove(ItemStackMoveOperation opTemplate)
        {
            ItemSlot liquidTargetSlot = liquidTransferService.GetTargetSlot(CreatePreferredWorldLiquidProbeStack(), targetSlotSignal);
            if (liquidTargetSlot != null)
            {
                TransferOperationResult liquidResult = liquidTransferService.TryMoveFromWorldSource(sourcePos, liquidTargetSlot, opTemplate.RequestedQuantity, false);
                if (liquidResult.Success)
                {
                    liquidTargetSlot.MarkDirty();
                    return liquidResult;
                }
            }

            EntityItem entity = FindItemEntityNearSource();
            if (entity == null || entity.Itemstack == null || entity.Itemstack.StackSize <= 0) return TransferOperationResult.None;

            ItemStack stack = entity.Itemstack;
            int moved = TryPutOneIntoInventory(stack);
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

        private int TryPutOneIntoInventory(ItemStack fromStack)
        {
            if (targetSlotSignal > 0)
            {
                int index = targetSlotSignal - 1;
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

        private ItemStack CreatePreferredWorldLiquidProbeStack()
        {
            Item item = api.World.GetItem(new AssetLocation("game", "waterportion"));
            return item == null ? null : new ItemStack(item, 1);
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
            if (conditionsEvaluator.HasConditions)
            {
                var ctx = ItemConditionContextUtil.BuildContext(api.World, stack);
                liquidTransferService.AddConditionContext(ctx);
                return conditionsEvaluator.Evaluate(stack, ctx, out byte _);
            }

            return true;
        }
    }
}