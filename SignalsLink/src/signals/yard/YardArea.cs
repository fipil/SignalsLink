using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.yard
{
    /// <summary>
    /// One connected run of yard tiles, worked out on demand.
    ///
    /// Nothing about the shape is ever saved: the world already holds the truth — which blocks are
    /// tiles — and everything else follows from it. That is what lets the tiles stay free of block
    /// entities.
    ///
    /// Any connected shape counts. Players build around terrain, track and buildings, so demanding
    /// a rectangle would mean demanding a cleared square first. What is checked is the SIZE.
    /// </summary>
    public sealed class YardArea
    {
        /// <summary>Below this it is a path, not a yard.</summary>
        public const int MinTiles = 4;

        /// <summary>
        /// A hard ceiling, and one that is reported rather than silently truncated. A flood fill
        /// that stops half way would leave the player with an area that behaves differently from
        /// the one they can see.
        /// </summary>
        public const int MaxTiles = 400;

        public IReadOnlyList<BlockPos> Tiles { get; }
        public bool TooLarge { get; }

        public bool IsValid => !TooLarge && Tiles.Count >= MinTiles;

        private YardArea(List<BlockPos> tiles, bool tooLarge)
        {
            Tiles = tiles;
            TooLarge = tooLarge;
        }

        /// <summary>
        /// Every tile reachable from <paramref name="start"/> by steps between touching sides.
        ///
        /// The test is handed in rather than taken from the world, which keeps this a piece of
        /// plain reasoning about a shape - and testable without a running game.
        /// </summary>
        public static YardArea FloodFill(BlockPos start, Func<BlockPos, bool> isTile)
        {
            var tiles = new List<BlockPos>();

            if (start == null || isTile == null || !isTile(start)) return new YardArea(tiles, false);

            var seen = new HashSet<BlockPos>();
            var queue = new Queue<BlockPos>();

            seen.Add(start);
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                BlockPos pos = queue.Dequeue();
                tiles.Add(pos);

                // One over the ceiling is enough to know, and it stops a mad flood fill dead.
                if (tiles.Count > MaxTiles) return new YardArea(tiles, true);

                foreach (BlockPos next in Neighbours(pos))
                {
                    if (!seen.Add(next)) continue;
                    if (!isTile(next)) continue;

                    queue.Enqueue(next);
                }
            }

            return new YardArea(tiles, false);
        }

        /// <summary>
        /// The tiles in the order they get filled: nearest to the device first, ties broken by x
        /// and then z.
        ///
        /// A rectangle makes this obvious; an L-shape does not, so it is spelled out. The player
        /// then sees the yard fill from their end rather than from wherever the flood fill happened
        /// to start.
        /// </summary>
        public IReadOnlyList<BlockPos> OrderedFrom(BlockPos from)
        {
            var ordered = new List<BlockPos>(Tiles);

            ordered.Sort((a, b) =>
            {
                int byDistance = SquareDistance(a, from).CompareTo(SquareDistance(b, from));
                if (byDistance != 0) return byDistance;

                int byX = a.X.CompareTo(b.X);
                return byX != 0 ? byX : a.Z.CompareTo(b.Z);
            });

            return ordered;
        }

        private static long SquareDistance(BlockPos a, BlockPos b)
        {
            if (b == null) return 0;

            long dx = a.X - b.X;
            long dy = a.Y - b.Y;
            long dz = a.Z - b.Z;

            return dx * dx + dy * dy + dz * dz;
        }

        /// <summary>The four tiles that share a side. A yard is flat, so it never steps up or down.</summary>
        private static IEnumerable<BlockPos> Neighbours(BlockPos pos)
        {
            yield return new BlockPos(pos.X + 1, pos.Y, pos.Z, pos.dimension);
            yield return new BlockPos(pos.X - 1, pos.Y, pos.Z, pos.dimension);
            yield return new BlockPos(pos.X, pos.Y, pos.Z + 1, pos.dimension);
            yield return new BlockPos(pos.X, pos.Y, pos.Z - 1, pos.dimension);
        }
    }
}
