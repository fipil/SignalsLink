using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.manageddock
{
    /// <summary>
    /// The freight dock: a crate with two signal anchors, which loads and unloads whatever is
    /// standing next to it.
    ///
    /// It is set down turned, the way a crate or a chest is, and it turns its anchors and its
    /// working corner towards whoever put it there - reaching round the back of a crate to plug a
    /// wire in would be a poor joke.
    /// </summary>
    public class BlockManagedDock : Block
    {
        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            if (!base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode)) return false;

            // SuggestedHVOrientation answers with the direction from the block TOWARDS whoever is
            // placing it, so this is the working corner turned to face them. Taking its opposite -
            // which is what this did at first - buries the anchors round the back.
            BlockFacing[] horizontal = SuggestedHVOrientation(byPlayer, blockSel);
            BlockFacing facing = horizontal != null && horizontal.Length > 0 && horizontal[0] != null
                ? horizontal[0]
                : BlockFacing.NORTH;

            Block oriented = world.BlockAccessor.GetBlock(CodeWithVariant("side", facing.Code));
            if (oriented != null) world.BlockAccessor.ExchangeBlock(oriented.BlockId, blockSel.Position);

            return true;
        }

        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);

            // Somebody laid or broke a block next door - it may well be the paving.
            (world.BlockAccessor.GetBlockEntity(pos) as BEManagedDock)?.OnNeighbourBlockChange(neibpos);
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            // The paper behavior gets its say first (it runs on the block, before this) and only
            // takes over when the player is holding paper. Everything else opens the crate.
            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEManagedDock dock)
            {
                return dock.OnPlayerRightClick(byPlayer, blockSel);
            }

            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }
    }
}
