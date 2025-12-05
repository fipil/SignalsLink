using signals.src.transmission;
using SignalsLink.src.signals.behaviours;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.entitysensor
{
    public class EntitySensor : BlockConnection
    {
        public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            // Najdi behavior
            var chargeBehavior = GetBehavior<BlockBehaviorTemporalCharge>();

            if (world.Side == EnumAppSide.Server && chargeBehavior != null)
            {
                if (world.BlockAccessor.GetBlockEntity(pos) is ITemporalChargeHolder be)
                {
                    float charge = be.GetCurrentCharge();
                    ItemStack stack = new ItemStack(this);

                    if (charge > 0)
                    {
                        if (stack.Attributes == null)
                            stack.Attributes = new TreeAttribute();

                        stack.Attributes.SetFloat("storedCharge", charge);
                    }

                    world.SpawnItemEntity(stack, pos.ToVec3d().Add(0.5, 0.5, 0.5));
                    return; // Nepouštěj base, aby nedropnul dvakrát
                }
            }

            base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            var chargeBehavior = GetBehavior<BlockBehaviorTemporalCharge>();

            if (chargeBehavior != null)
            {
                return chargeBehavior.TryCharge(world, byPlayer, blockSel);
            }

            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack byItemStack)
        {
            base.OnBlockPlaced(world, blockPos, byItemStack);

            var chargeBehavior = GetBehavior<BlockBehaviorTemporalCharge>();

            if (world.Side == EnumAppSide.Server && chargeBehavior != null && byItemStack?.Attributes != null)
            {
                float storedCharge = byItemStack.Attributes.GetFloat("storedCharge", 0f);

                if (storedCharge > 0 && world.BlockAccessor.GetBlockEntity(blockPos) is ITemporalChargeHolder holder)
                {
                    holder.SetCurrentCharge(storedCharge);
                }
            }
        }
    }
}
