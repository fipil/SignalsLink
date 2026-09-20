using SignalsLink.src.signals.cargo;
using SignalsLink.src.signals.yard;
using Vintagestory.API.MathTools;

namespace SignalsLink.Tests
{
    /// <summary>
    /// Reading `load yard north coal` and deciding which way to look.
    ///
    /// The search itself needs blocks in a world, but what a header MEANS and which offsets count
    /// as "that way" are plain reasoning, and they are the parts that go quietly wrong.
    /// </summary>
    public class YardSearchTests
    {
        [Fact]
        public void A_direction_in_the_header_is_picked_out_of_it()
        {
            YardSelector selector = Parse("north");

            Assert.Equal(BlockFacing.NORTH, selector.Direction);
            Assert.False(selector.HasName);
        }

        [Fact]
        public void What_is_left_after_the_direction_is_the_name()
        {
            YardSelector selector = Parse("north", "severní", "halda");

            Assert.Equal(BlockFacing.NORTH, selector.Direction);
            Assert.Equal("severní halda", selector.Name);
        }

        [Fact]
        public void The_direction_may_come_after_the_name()
        {
            // Header tokens do not depend on their order - that is the rule for the whole header.
            YardSelector selector = Parse("coal", "east");

            Assert.Equal(BlockFacing.EAST, selector.Direction);
            Assert.Equal("coal", selector.Name);
        }

        [Fact]
        public void Only_the_first_direction_counts_and_the_rest_is_name()
        {
            // A yard really called "west" is then still reachable, which it would not be if every
            // direction-shaped word were swallowed.
            YardSelector selector = Parse("north", "west");

            Assert.Equal(BlockFacing.NORTH, selector.Direction);
            Assert.Equal("west", selector.Name);
        }

        [Fact]
        public void No_direction_means_all_round()
        {
            YardSelector selector = Parse("coal");

            Assert.Null(selector.Direction);
            Assert.Equal("coal", selector.Name);
        }

        [Fact]
        public void A_name_longer_than_a_sign_can_hold_is_cut_to_what_fits()
        {
            YardSelector selector = Parse("north", new string('x', BEYardSign.MaxNameLength + 10));

            Assert.Equal(BEYardSign.MaxNameLength, selector.Name.Length);
        }

        // ---------------------------------------------------------------- which way is that way

        [Theory]
        [InlineData(0, -1, true)]    // straight north
        [InlineData(5, -3, true)]    // north and off to the side still counts
        [InlineData(0, 1, false)]    // behind
        [InlineData(0, 0, false)]    // level with the device is not north of it
        public void North_takes_the_half_of_the_square_on_that_side(int dx, int dz, bool expected)
        {
            Assert.Equal(expected, YardCargoHolderFinder.InDirection(BlockFacing.NORTH, dx, dz));
        }

        [Theory]
        [InlineData(1, 0, true)]
        [InlineData(-1, 0, false)]
        public void East_is_plus_x(int dx, int dz, bool expected)
        {
            Assert.Equal(expected, YardCargoHolderFinder.InDirection(BlockFacing.EAST, dx, dz));
        }

        [Theory]
        [InlineData(-1, 0, true)]
        [InlineData(1, 0, false)]
        public void West_is_minus_x(int dx, int dz, bool expected)
        {
            Assert.Equal(expected, YardCargoHolderFinder.InDirection(BlockFacing.WEST, dx, dz));
        }

        [Fact]
        public void South_is_plus_z()
        {
            Assert.True(YardCargoHolderFinder.InDirection(BlockFacing.SOUTH, 0, 1));
            Assert.False(YardCargoHolderFinder.InDirection(BlockFacing.SOUTH, 0, -1));
        }

        [Fact]
        public void Without_a_direction_every_offset_counts()
        {
            Assert.True(YardCargoHolderFinder.InDirection(null, 7, -7));
            Assert.True(YardCargoHolderFinder.InDirection(null, 0, 0));
        }

        [Fact]
        public void A_vertical_direction_says_nothing_about_sideways()
        {
            Assert.True(YardCargoHolderFinder.InDirection(BlockFacing.DOWN, 3, -4));
            Assert.True(YardCargoHolderFinder.InDirection(BlockFacing.UP, -2, 5));
        }

        // ---------------------------------------------------------------- which levels

        [Fact]
        public void Sideways_the_yard_is_level_with_the_device_or_one_below()
        {
            // A yard is flat, and a device stands either on it or beside it on the same ground.
            Assert.Equal(new[] { 0, -1 }, Levels(null));
            Assert.Equal(new[] { 0, -1 }, Levels(BlockFacing.NORTH));
        }

        [Fact]
        public void Up_climbs_the_whole_radius()
        {
            // A dock buried under the platform, with the yard over its head.
            int[] levels = Levels(BlockFacing.UP);

            Assert.Equal(1, levels[0]);
            Assert.Equal(YardCargoHolderFinder.SearchRadius, levels[^1]);
            Assert.Equal(YardCargoHolderFinder.SearchRadius, levels.Length);
        }

        [Fact]
        public void Down_descends_the_whole_radius()
        {
            // A dock up in the loft of a warehouse whose floor is the yard.
            int[] levels = Levels(BlockFacing.DOWN);

            Assert.Equal(-1, levels[0]);
            Assert.Equal(-YardCargoHolderFinder.SearchRadius, levels[^1]);
        }

        [Fact]
        public void The_nearest_level_comes_first_so_the_search_stops_there()
        {
            // Paving directly overhead or underfoot is the everyday case and must cost one lookup,
            // not a walk through the whole cube.
            Assert.Equal(1, Levels(BlockFacing.UP)[0]);
            Assert.Equal(-1, Levels(BlockFacing.DOWN)[0]);
        }

        [Fact]
        public void Only_up_and_down_are_vertical()
        {
            Assert.True(YardCargoHolderFinder.IsVertical(BlockFacing.UP));
            Assert.True(YardCargoHolderFinder.IsVertical(BlockFacing.DOWN));
            Assert.False(YardCargoHolderFinder.IsVertical(BlockFacing.NORTH));
            Assert.False(YardCargoHolderFinder.IsVertical(null));
        }

        private static int[] Levels(BlockFacing direction)
        {
            return System.Linq.Enumerable.ToArray(YardCargoHolderFinder.Levels(direction));
        }

        // ---------------------------------------------------------------- plumbing

        private static YardSelector Parse(params string[] tokens)
        {
            Assert.True(new YardCargoHolderFinder().TryParseHeader(tokens, null, out ICargoSelector selector));
            return Assert.IsType<YardSelector>(selector);
        }
    }
}
