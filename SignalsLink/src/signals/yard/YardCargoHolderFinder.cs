using System;
using System.Collections.Generic;
using SignalsLink.src.signals.cargo;
using SignalsLink.src.signals.managedchute.transporting;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.yard
{
    /// <summary>What <c>load yard north</c> — or <c>load yard north coal</c> — asked for.</summary>
    public sealed class YardSelector : ICargoSelector
    {
        /// <summary>Which way to look, or null for "all round".</summary>
        public BlockFacing Direction { get; }

        /// <summary>The name on a sign standing in the yard, or empty for "whichever one is there".</summary>
        public string Name { get; }

        public YardSelector(BlockFacing direction, string name)
        {
            Direction = direction;
            Name = name ?? "";
        }

        public bool HasName => Name.Length > 0;
    }

    /// <summary>
    /// One paved tile, as far as the paper is concerned: the column of piles standing on it.
    ///
    /// The inventory is a reading of the column, not its live slots - ground storage keeps its
    /// goods in a block entity per pile, and the piles are created and destroyed as the column
    /// grows and shrinks. Conditions answer against this reading; the goods themselves are moved by
    /// the world transfer helpers, which is what <see cref="GroundPos"/> is for.
    /// </summary>
    public sealed class YardHold : ICargoHold
    {
        /// <summary>Where the goods stand: one above the paving.</summary>
        public BlockPos GroundPos { get; }

        public string Code { get; }

        /// <summary>
        /// Read on demand, not when the yard is found.
        ///
        /// Reading one column means walking up it block entity by block entity, and a yard can be
        /// four hundred columns. Only the ones the paper actually asks about are worth that, and on
        /// most ticks that is none of them.
        /// </summary>
        public IInventory Inventory => inventory ??= TargetInventoryResolver.ResolveGroundColumn(world?.Api, GroundPos);

        /// <summary>The goods stand in the world here, so they are moved by the world transfers.</summary>
        public BlockPos Pos => GroundPos;

        /// <summary>
        /// Answered by looking at the one block standing on the paving, not by reading the column.
        /// It is asked of every tile of the yard before anything else happens, and reading a column
        /// means walking up it block entity by block entity.
        /// </summary>
        public bool IsEmpty => world?.BlockAccessor?.GetBlockEntity(GroundPos) is not BlockEntityGroundStorage
            && world?.BlockAccessor?.GetBlock(GroundPos)?.Attributes?["layerGroupCode"].Exists != true;

        private readonly IWorldAccessor world;
        private IInventory inventory;

        public YardHold(IWorldAccessor world, BlockPos groundPos)
        {
            this.world = world;
            GroundPos = groundPos;
            Code = "yard " + groundPos.X + "/" + groundPos.Y + "/" + groundPos.Z;
        }

        /// <summary>Forgets what was read, so the next question is answered against the world again.</summary>
        public void Invalidate()
        {
            inventory = null;
        }

        public void MarkDirty()
        {
            Invalidate();

            // Every pile of the column is its own block entity, so there is no single thing to
            // mark - and no telling from here which of them a transfer touched.
            IBlockAccessor accessor = world?.BlockAccessor;
            if (accessor == null) return;

            for (int dy = 0; dy < 64; dy++)
            {
                BlockEntity be = accessor.GetBlockEntity(GroundPos.AddCopy(0, dy, 0));
                if (be == null) break;

                be.MarkDirty(true);
            }
        }
    }

    /// <summary>
    /// A storage yard as the other party to an exchange: one hold per paved tile, ordered from the
    /// device outwards.
    /// </summary>
    public sealed class YardHolder : ICargoHolder
    {
        public IReadOnlyList<ICargoHold> Holds { get; }

        /// <summary>The name read off a sign standing in the yard; empty when it has none.</summary>
        public string Name { get; }

        public IReadOnlyList<BlockPos> Tiles { get; }

        /// <summary>Always. A yard has nothing to arrive or come to a stop.</summary>
        public bool IsReady => true;

        public YardHolder(IReadOnlyList<ICargoHold> holds, IReadOnlyList<BlockPos> tiles, string name)
        {
            Holds = holds;
            Tiles = tiles;
            Name = name ?? "";
        }

        /// <summary>
        /// The tiles do not move, so a found yard is worth keeping - but what stands ON them
        /// changes with every transfer, so everything read off them is dropped here.
        /// </summary>
        public void Refresh()
        {
            foreach (ICargoHold hold in Holds)
            {
                if (hold is YardHold yardHold) yardHold.Invalidate();
            }
        }
    }

    /// <summary>
    /// Finds the storage yard a paper asked for.
    ///
    /// The header says which way to look and, when it matters, which yard is meant:
    /// <c>load yard north</c>, <c>load yard north coal</c>, or plain <c>load yard</c> for whatever
    /// is around.
    /// </summary>
    public class YardCargoHolderFinder : ICargoHolderFinder
    {
        public const string KeywordText = "yard";

        /// <summary>How far out to look, as a square rather than a circle - it is easier to picture.</summary>
        public const int SearchRadius = 10;

        public string Keyword => KeywordText;

        public bool TryParseHeader(IReadOnlyList<string> tokens, PaperErrorSink errors, out ICargoSelector selector)
        {
            BlockFacing direction = null;
            List<string> rest = new List<string>();

            foreach (string token in tokens ?? Array.Empty<string>())
            {
                BlockFacing facing = direction == null ? BlockFacing.FromCode(token.ToLowerInvariant()) : null;

                if (facing != null)
                {
                    direction = facing;
                    continue;
                }

                rest.Add(token);
            }

            // Whatever is left is the name, spaces and all: a yard called "north pile" reads the
            // way it is written on the sign.
            selector = new YardSelector(direction, BEYardSign.Clamp(string.Join(" ", rest)));
            return true;
        }

        public bool TryFind(IWorldAccessor world, BlockPos devicePos, ICargoSelector selector, out ICargoHolder holder)
        {
            holder = null;
            if (world == null || devicePos == null) return false;

            YardSelector wanted = selector as YardSelector ?? new YardSelector(null, "");
            HashSet<BlockPos> seen = new HashSet<BlockPos>();

            foreach (BlockPos start in Candidates(devicePos, wanted.Direction))
            {
                if (seen.Contains(start)) continue;
                if (!BlockYardTile.IsYardTile(world.BlockAccessor.GetBlock(start))) continue;

                YardArea area = Yard.AreaAt(world, start);

                // Whole yards, not tiles: once an area has been looked at, none of its tiles can
                // start another search. Without this a twenty by twenty yard would be flood filled
                // four hundred times.
                foreach (BlockPos tile in area.Tiles) seen.Add(tile);

                if (!area.IsValid) continue;

                IReadOnlyList<BlockPos> ordered = area.OrderedFrom(devicePos);
                string name = Yard.ReadName(world, ordered);

                // Rings run outwards, so with no name asked for, the first yard found is the
                // nearest one - which is the only answer that does not need explaining.
                if (wanted.HasName && !wanted.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;

                holder = new YardHolder(BuildHolds(world, ordered), ordered, name);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Where a yard could start, nearest first.
        ///
        /// <b>Sideways, two levels.</b> Without a vertical direction the paving is at the device's
        /// own level or one below — a yard is flat and the device stands either on it or beside it
        /// on the same ground. That turns a search of a cube into a search of two squares, and it
        /// is the whole reason this can be run rather than cached forever.
        ///
        /// <b>Up and down really do go up and down</b>, as far as the same ten blocks: a dock
        /// buried under the platform with the yard over its head, or one up in the loft of a
        /// warehouse whose floor is the yard. Then the search goes level by level, each level ring
        /// by ring — so the everyday case, paving directly overhead or underfoot, is the very first
        /// position looked at.
        ///
        /// <b>Ring by ring</b> otherwise, so positions come out ordered by how far away they are
        /// and the search can stop at the first yard that answers. A device standing at the edge of
        /// its own yard looks at nine positions, not at eight hundred.
        /// </summary>
        private static IEnumerable<BlockPos> Candidates(BlockPos devicePos, BlockFacing direction)
        {
            if (IsVertical(direction))
            {
                foreach (int dy in Levels(direction))
                {
                    for (int ring = 0; ring <= SearchRadius; ring++)
                    {
                        foreach ((int dx, int dz) in Ring(ring)) yield return devicePos.AddCopy(dx, dy, dz);
                    }
                }

                yield break;
            }

            for (int ring = 0; ring <= SearchRadius; ring++)
            {
                foreach ((int dx, int dz) in Ring(ring))
                {
                    if (!InDirection(direction, dx, dz)) continue;

                    foreach (int dy in Levels(direction)) yield return devicePos.AddCopy(dx, dy, dz);
                }
            }
        }

        public static bool IsVertical(BlockFacing direction)
        {
            return direction == BlockFacing.UP || direction == BlockFacing.DOWN;
        }

        /// <summary>
        /// Which levels are searched, nearest first.
        ///
        /// A vertical direction climbs or descends the full radius; anything else keeps to the two
        /// levels a flat yard can be on relative to the device standing next to it.
        /// </summary>
        public static IEnumerable<int> Levels(BlockFacing direction)
        {
            if (direction == BlockFacing.UP)
            {
                for (int dy = 1; dy <= SearchRadius; dy++) yield return dy;
                yield break;
            }

            if (direction == BlockFacing.DOWN)
            {
                for (int dy = -1; dy >= -SearchRadius; dy--) yield return dy;
                yield break;
            }

            yield return 0;
            yield return -1;
        }

        /// <summary>The square ring at this distance, so that nearer positions come first.</summary>
        private static IEnumerable<(int dx, int dz)> Ring(int ring)
        {
            if (ring == 0)
            {
                yield return (0, 0);
                yield break;
            }

            for (int dx = -ring; dx <= ring; dx++)
            {
                yield return (dx, -ring);
                yield return (dx, ring);
            }

            for (int dz = -ring + 1; dz <= ring - 1; dz++)
            {
                yield return (-ring, dz);
                yield return (ring, dz);
            }
        }

        /// <summary>
        /// Does this offset lie the way the header pointed? A horizontal direction takes the half
        /// of the square on that side; a vertical one says nothing about sideways, so everything
        /// counts - as it does without a direction at all.
        /// </summary>
        public static bool InDirection(BlockFacing direction, int dx, int dz)
        {
            if (direction == null || IsVertical(direction)) return true;

            // North is -Z in Vintage Story.
            if (direction == BlockFacing.NORTH) return dz < 0;
            if (direction == BlockFacing.SOUTH) return dz > 0;
            if (direction == BlockFacing.WEST) return dx < 0;
            if (direction == BlockFacing.EAST) return dx > 0;

            return true;
        }

        private static IReadOnlyList<ICargoHold> BuildHolds(IWorldAccessor world, IReadOnlyList<BlockPos> tiles)
        {
            List<ICargoHold> holds = new List<ICargoHold>();

            foreach (BlockPos tile in tiles)
            {
                holds.Add(new YardHold(world, tile.UpCopy()));
            }

            return holds;
        }
    }
}
