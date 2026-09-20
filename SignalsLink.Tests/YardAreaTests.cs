using System.Collections.Generic;
using System.Linq;
using SignalsLink.src.signals.yard;
using Vintagestory.API.MathTools;

namespace SignalsLink.Tests
{
    /// <summary>
    /// Working out the shape of a storage yard. The flood fill takes the "is this a tile?" test as
    /// an argument rather than reading the world, so the shape reasoning can be checked here and
    /// only the block lookup is left for the game.
    /// </summary>
    public class YardAreaTests
    {
        [Fact]
        public void A_rectangle_is_found_whole()
        {
            YardArea area = Fill("""
                XXXX
                XXXX
                XXXX
                """);

            Assert.Equal(12, area.Tiles.Count);
            Assert.True(area.IsValid);
            Assert.False(area.TooLarge);
        }

        [Fact]
        public void Any_connected_shape_counts()
        {
            // An L: players build around track and buildings, so a rectangle cannot be the rule.
            YardArea area = Fill("""
                X...
                X...
                XXXX
                """);

            Assert.Equal(6, area.Tiles.Count);
            Assert.True(area.IsValid);
        }

        [Fact]
        public void Tiles_that_only_touch_at_a_corner_are_a_different_yard()
        {
            YardArea area = Fill("""
                XX..
                XX..
                ..XX
                ..XX
                """);

            Assert.Equal(4, area.Tiles.Count);
        }

        [Fact]
        public void Two_areas_that_touch_are_one_area()
        {
            // Intuitive, and anyone who wants two leaves a gap.
            YardArea area = Fill("""
                XXXXXX
                XXXXXX
                """);

            Assert.Equal(12, area.Tiles.Count);
        }

        [Fact]
        public void Too_few_tiles_is_not_a_yard_yet()
        {
            YardArea area = Fill("""
                XX..
                X...
                """);

            Assert.Equal(3, area.Tiles.Count);
            Assert.False(area.IsValid);
        }

        [Fact]
        public void Over_the_ceiling_is_reported_rather_than_truncated()
        {
            // A flood fill that quietly stopped half way would leave a yard that behaves
            // differently from the one the player can see.
            YardArea area = YardArea.FloodFill(new BlockPos(0, 0, 0, 0), _ => true);

            Assert.True(area.TooLarge);
            Assert.False(area.IsValid);
            Assert.True(area.Tiles.Count > YardArea.MaxTiles);
        }

        [Fact]
        public void A_yard_never_steps_up_or_down()
        {
            var tiles = new HashSet<BlockPos>
            {
                new BlockPos(0, 0, 0, 0),
                new BlockPos(1, 0, 0, 0),
                new BlockPos(2, 0, 0, 0),
                new BlockPos(3, 0, 0, 0),
                new BlockPos(3, 1, 0, 0),   // one step up: a different yard
            };

            YardArea area = YardArea.FloodFill(new BlockPos(0, 0, 0, 0), tiles.Contains);

            Assert.Equal(4, area.Tiles.Count);
        }

        [Fact]
        public void Nothing_at_the_start_is_an_empty_area()
        {
            YardArea area = YardArea.FloodFill(new BlockPos(0, 0, 0, 0), _ => false);

            Assert.Empty(area.Tiles);
            Assert.False(area.IsValid);
        }

        // ---------------------------------------------------------------- ordering

        [Fact]
        public void Tiles_are_ordered_from_the_device_outwards()
        {
            YardArea area = Fill("""
                XXXX
                XXXX
                """);

            // A dock standing just west of the near corner.
            IReadOnlyList<BlockPos> ordered = area.OrderedFrom(new BlockPos(-1, 0, 0, 0));

            Assert.Equal(new BlockPos(0, 0, 0, 0), ordered[0]);
            Assert.True(Distance(ordered[0]) <= Distance(ordered[ordered.Count - 1]));

            long Distance(BlockPos p) => (p.X + 1) * (p.X + 1) + p.Z * p.Z;
        }

        [Fact]
        public void The_order_is_the_same_every_time()
        {
            YardArea area = Fill("""
                X...
                X...
                XXXX
                """);

            var from = new BlockPos(-1, 0, 0, 0);
            Assert.Equal(area.OrderedFrom(from), area.OrderedFrom(from));
        }

        // ---------------------------------------------------------------- plumbing

        /// <summary>Rows run from north to south, columns from west to east; X is a tile.</summary>
        private static YardArea Fill(string map)
        {
            var tiles = new HashSet<BlockPos>();
            string[] rows = map.Replace("\r", "").Split('\n').Where(r => r.Trim().Length > 0).ToArray();

            for (int z = 0; z < rows.Length; z++)
            {
                string row = rows[z].Trim();
                for (int x = 0; x < row.Length; x++)
                {
                    if (row[x] == 'X') tiles.Add(new BlockPos(x, 0, z, 0));
                }
            }

            BlockPos start = tiles.OrderBy(p => p.Z).ThenBy(p => p.X).First();
            return YardArea.FloodFill(start, tiles.Contains);
        }
    }
}
