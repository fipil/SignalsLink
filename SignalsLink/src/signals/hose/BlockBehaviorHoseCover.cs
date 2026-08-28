using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.hose
{
    /// <summary>
    /// Placement behavior for the hose valve, adapted from the Signals
    /// <c>BlockBehaviorCoverWithDirection</c>. Difference: on a WALL or CEILING the valve has no
    /// free rotation around the mount normal — it is always mounted pins-up (orientation = "up").
    /// Only floor placement (side = down, the drain / výlevka) keeps the projected orientation,
    /// which selects the horizontal neighbour it pours into.
    /// </summary>
    public class BlockBehaviorHoseCover : BlockBehavior
    {
        public string orientationCode => "orientation";
        public string sideCode => "side";

        public bool handleDrop;

        public BlockBehaviorHoseCover(Block block) : base(block) { }

        public override void Initialize(JsonObject properties)
        {
            handleDrop = properties["handleDrop"].AsBool(true);
            base.Initialize(properties);
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, ref float dropQuantityMultiplier, ref EnumHandling handled)
        {
            if (!handleDrop)
            {
                handled = EnumHandling.PassThrough;
                return null;
            }

            handled = EnumHandling.PreventDefault;
            AssetLocation baseBlock = block.CodeWithVariants(new string[] { orientationCode, sideCode }, new string[] { "north", "down" });
            return new ItemStack[] { new ItemStack(world.BlockAccessor.GetBlock(baseBlock)) };
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos, ref EnumHandling handled)
        {
            handled = EnumHandling.PreventDefault;
            AssetLocation baseBlock = block.CodeWithVariants(new string[] { orientationCode, sideCode }, new string[] { "north", "down" });
            return new ItemStack(world.BlockAccessor.GetBlock(baseBlock));
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref EnumHandling handling, ref string failureCode)
        {
            handling = EnumHandling.PreventDefault;
            Block orientedBlock = GetOrientedBlock(world, byPlayer, blockSel);
            if (orientedBlock == null) { failureCode = "requireattachable"; return false; }

            if (orientedBlock.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
            {
                world.BlockAccessor.SetBlock(orientedBlock.BlockId, blockSel.Position, itemstack);
                return true;
            }
            return false;
        }

        public override bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling, ref string failureCode)
        {
            BlockFacing oppositeFace = blockSel.Face.Opposite;
            BlockPos attachingBlockPos = blockSel.Position.AddCopy(oppositeFace);
            Block attachBlock = world.BlockAccessor.GetBlock(attachingBlockPos);

            if (attachBlock.CanAttachBlockAt(world.BlockAccessor, this.block, attachingBlockPos, blockSel.Face))
            {
                return true;
            }

            failureCode = "requireattachable";
            return false;
        }

        public Block GetOrientedBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            // side = the face the valve mounts against (the host sits there).
            BlockFacing side = blockSel.Face.Opposite;

            string orientation;
            if (side == BlockFacing.DOWN)
            {
                // Floor drain (výlevka): orient by the player's horizontal look direction, so it can
                // be turned to pour into any of the four neighbouring blocks.
                BlockFacing[] hv = Block.SuggestedHVOrientation(byPlayer, blockSel);
                BlockFacing dir = hv != null && hv.Length > 0 && hv[0] != null ? hv[0] : BlockFacing.NORTH;
                orientation = dir.Code;
            }
            else
            {
                // Wall or ceiling: always mounted pins-up (no free rotation around the mount normal).
                orientation = "up";
            }

            AssetLocation oBlock = block.CodeWithVariants(
                new Dictionary<string, string> { { orientationCode, orientation }, { sideCode, side.Code } });
            return world.BlockAccessor.GetBlock(oBlock);
        }
    }
}
