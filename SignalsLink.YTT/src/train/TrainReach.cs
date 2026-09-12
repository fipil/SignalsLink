using Vintagestory.API.MathTools;

namespace SignalsLink.YTT.src.train
{
    /// <summary>
    /// Whether a vehicle is where the header says it should be, as plain geometry - no world, no
    /// entity, so the counting can be checked on a table.
    /// </summary>
    public static class TrainReach
    {
        /// <param name="distance">
        /// Blocks off the device, counted by stepping away from it. Null means any distance on
        /// that side.
        /// </param>
        public static bool Covers(double minX, double maxX, double minZ, double maxZ,
            Vec3d centre, BlockFacing direction, int? distance)
        {
            if (direction == null || centre == null) return true;

            bool alongZ = direction == BlockFacing.NORTH || direction == BlockFacing.SOUTH;

            // North is -Z in Vintage Story, and west is -X.
            bool towardsLess = direction == BlockFacing.NORTH || direction == BlockFacing.WEST;

            double min = alongZ ? minZ : minX;
            double max = alongZ ? maxZ : maxX;
            double from = alongZ ? centre.Z : centre.X;

            // Without a distance: any part of the body on that side. A vehicle standing across
            // the device satisfies every direction, which is the honest answer - it IS on
            // every side.
            if (distance == null) return towardsLess ? min < from : max > from;

            // With one: the block that many steps off the device, and whether the vehicle stands
            // on it. The slack is the vehicle's own width - two blocks on standard gauge, one on
            // a mine cart - so a miscount of one survives on the big trains and not on the small.
            double target = towardsLess ? from - distance.Value : from + distance.Value;

            return min <= target + 0.5 && max >= target - 0.5;
        }
    }
}
