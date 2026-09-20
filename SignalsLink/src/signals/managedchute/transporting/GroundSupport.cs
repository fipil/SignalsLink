using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.managedchute.transporting
{
    /// <summary>
    /// Is there a footing under this position — can something be set down here?
    ///
    /// The static <c>SideSolid</c> flag alone is not enough. A pile of firewood declares
    /// <c>upSolid</c> in the item's GroundStorable properties, so its up-solidity is a property of
    /// what is in the pile rather than of the block, and the block answers for itself through
    /// <c>CanAttachBlockAt</c> — which is what a player's own placement asks. Without that second
    /// question nothing could ever be put down on a stack of firewood, which is exactly where a
    /// charcoal pit wants its firepit built.
    /// </summary>
    public static class GroundSupport
    {
        public static bool HasFooting(IWorldAccessor world, BlockPos pos)
        {
            if (world?.BlockAccessor == null || pos == null) return false;

            BlockPos below = pos.DownCopy();
            Block block = world.BlockAccessor.GetBlock(below);
            if (block == null) return false;

            if (block.SideSolid[BlockFacing.UP.Index]) return true;

            return block.CanAttachBlockAt(world.BlockAccessor, block, below, BlockFacing.UP);
        }
    }
}
