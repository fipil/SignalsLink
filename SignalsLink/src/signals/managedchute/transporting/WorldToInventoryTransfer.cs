using SignalsLink.src.signals.paperConditions;
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent; // nahoøe

namespace SignalsLink.src.signals.managedchute.transporting
{
    // Pøenos: svìt (item entity) -> inventáø.
    public class WorldToInventoryTransfer : IItemTransfer
    {
        private readonly ICoreAPI api;
        private readonly BlockPos sourcePos;
        private readonly IInventory targetInv;
        private readonly byte targetSlotSignal;
        protected readonly PaperConditionsEvaluator conditionsEvaluator;

        public WorldToInventoryTransfer(ICoreAPI api, BlockPos sourcePos, IInventory targetInv, byte targetSlotSignal, PaperConditionsEvaluator conditionsEvaluator)
        {
            this.api = api;
            this.sourcePos = sourcePos;
            this.targetInv = targetInv;
            this.targetSlotSignal = targetSlotSignal;
            this.conditionsEvaluator = conditionsEvaluator;
        }

        public int TryMoveOneItem(ItemStackMoveOperation opTemplate)
        {
            // Najdi jednu vhodnou item entitu v okolí sourcePos
            EntityItem entity = FindItemEntityNearSource();
            if (entity == null || entity.Itemstack == null || entity.Itemstack.StackSize <= 0) return 0;

            ItemStack stack = entity.Itemstack;

            // Pokus se vložit 1 kus do inventáøe
            int moved = TryPutOneIntoInventory(stack);
            if (moved <= 0) return 0;

            // Snížíme stack v entitì
            stack.StackSize -= moved;
            if (stack.StackSize <= 0)
            {
                entity.Die(EnumDespawnReason.PickedUp);
            }
            else
            {
                entity.Itemstack = stack;
            }

            return moved;
        }

        private EntityItem FindItemEntityNearSource()
        {
            IWorldAccessor world = api.World;

            // 3×3×3 blokù okolo sourcePos => svìtový AABB pøes celé bloky
            var min = new Vec3d(sourcePos.X - 1, sourcePos.Y - 1, sourcePos.Z - 1);
            var max = new Vec3d(sourcePos.X + 2, sourcePos.Y + 2, sourcePos.Z + 2);

            EntityItem found = null;

            world.GetEntitiesInsideCuboid(min.AsBlockPos, max.AsBlockPos, e =>
            {
                if (e is not EntityItem itemEntity) return false;

                var stack = itemEntity.Itemstack;
                if (stack == null || stack.StackSize <= 0) return false;

                // ignorovat liquid containery + itemy nesplòující podmínky
                if (IsLiquidContainer(stack) || !IsConditionMet(stack)) return false;

                found = itemEntity;
                return true; // stop
            });

            return found;
        }

        private int TryPutOneIntoInventory(ItemStack fromStack)
        {
            // explicitní slot
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
                        int moved = dummy.TryPutInto(api.World, slot, 1); // použij overload s quantity
                        if (moved > 0)
                        {
                            slot.MarkDirty();
                            return moved;
                        }
                    }
                }

                return 0;
            }

            // první vhodný slot
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

        private bool IsLiquidContainer(ItemStack stack)
        {
            if (stack?.Collectible == null) return false;
            if (stack.Collectible is BlockLiquidContainerBase) return true;
            if (stack.Collectible is ILiquidInterface) return true;
            // ItemLiquidPortion is internal, so check by type name string instead
            if (stack.Collectible.GetType().Name == "ItemLiquidPortion") return true;
            return false;
        }

        protected bool IsConditionMet(ItemStack stack)
        {
            if (conditionsEvaluator.HasConditions)
            {
                var ctx = ItemConditionContextUtil.BuildContext(api.World, stack);
                return conditionsEvaluator.Evaluate(stack, ctx, out byte blockIndex);
            }
            return true;
        }
    }
}