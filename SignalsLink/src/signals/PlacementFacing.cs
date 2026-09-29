using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals
{
    /// <summary>
    /// Which way a block should face when it is set down.
    ///
    /// Worked out here rather than taken from <c>SuggestedHVOrientation</c>, which answers a
    /// different question and turns our blocks the wrong way round - the sign showed its back and
    /// the dock its wire anchors. A device whose front is the side you work at has to face the
    /// person who put it there.
    /// </summary>
    public static class PlacementFacing
    {
        /// <summary>The horizontal direction from the placed block towards the player.</summary>
        public static BlockFacing TowardsPlayer(IPlayer player, BlockSelection selection)
        {
            if (player?.Entity == null || selection == null) return BlockFacing.NORTH;

            BlockPos target = selection.DidOffset
                ? selection.Position.AddCopy(selection.Face.Opposite)
                : selection.Position;

            return FromOffset(
                player.Entity.Pos.X + player.Entity.LocalEyePos.X - target.X - selection.HitPosition.X,
                player.Entity.Pos.Z + player.Entity.LocalEyePos.Z - target.Z - selection.HitPosition.Z);
        }

        /// <summary>The cardinal direction an offset points in. North is -Z, west is -X.</summary>
        public static BlockFacing FromOffset(double dx, double dz) => Math.Abs(dx) > Math.Abs(dz)
            ? (dx > 0 ? BlockFacing.EAST : BlockFacing.WEST)
            : (dz > 0 ? BlockFacing.SOUTH : BlockFacing.NORTH);
    }
}
