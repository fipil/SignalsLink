using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.yard
{
    /// <summary>
    /// The two questions about a storage yard that need a world to answer: which tiles it is made
    /// of, and what it is called.
    ///
    /// Kept apart from <see cref="YardArea"/> on purpose - the shape reasoning stays free of the
    /// game so it can be worked out on a table, and only the block lookups live here.
    /// </summary>
    public static class Yard
    {
        /// <summary>The whole area the tile at <paramref name="pos"/> belongs to.</summary>
        public static YardArea AreaAt(IWorldAccessor world, BlockPos pos)
        {
            return YardArea.FloodFill(pos, at => BlockYardTile.IsYardTile(world.BlockAccessor.GetBlock(at)));
        }

        /// <summary>
        /// What the yard is called: whatever the first sign standing in it says.
        ///
        /// Taken in the order the tiles are given, so with the filling order that answer never
        /// depends on where the flood fill happened to start.
        /// </summary>
        public static string ReadName(IWorldAccessor world, IReadOnlyList<BlockPos> tiles)
        {
            foreach (BlockPos tile in tiles)
            {
                if (world.BlockAccessor.GetBlockEntity(tile.UpCopy()) is not BEYardSign sign) continue;
                if (string.IsNullOrEmpty(sign.YardName)) continue;

                return sign.YardName;
            }

            return "";
        }
    }
}
