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

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            var chargeBehavior = GetBehavior<BlockBehaviorTemporalCharge>();

            Block dropBlock = world.GetBlock(new AssetLocation("signalslink", "entitysensor-off-north-down"));
            if (dropBlock == null || dropBlock.IsMissing)
            {
                dropBlock = this; // fallback, kdyby něco selhalo
            }

            ItemStack stack = new ItemStack(dropBlock);

            if (chargeBehavior != null && world.BlockAccessor.GetBlockEntity(pos) is ITemporalChargeHolder be)
            {
                float charge = be.GetCurrentCharge();

                if (charge > 0)
                {
                    if (stack.Attributes == null)
                        stack.Attributes = new TreeAttribute();

                    stack.Attributes.SetFloat("storedCharge", charge);
                }

            }

            return new ItemStack[] { stack };
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            world.Api.Logger.Debug($"OnBlockInteractStart called on {world.Side}");

            if (world.Side == EnumAppSide.Server)
            {
                var chargeBehavior = GetBehavior<BlockBehaviorTemporalCharge>();

                if (chargeBehavior != null)
                {
                    var charged = chargeBehavior.TryCharge(world, byPlayer, blockSel);
                    if (charged)
                        return true;
                }
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
