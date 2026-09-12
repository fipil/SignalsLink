using SignalsLink.src.signals;
using Vintagestory.API.MathTools;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>
    /// Which way a device faces when it is set down.
    ///
    /// Twice now a block has gone down backwards - the yard sign showed its back, the dock its wire
    /// anchors - because `SuggestedHVOrientation` answers a different question than "where is the
    /// player". A device whose front is the side you work at faces whoever put it there.
    /// </summary>
    public class PlacementFacingTests
    {
        [Theory]
        [InlineData(0, -5, "north")]     // the player stands north of it (north is -Z)
        [InlineData(0, 5, "south")]
        [InlineData(5, 0, "east")]
        [InlineData(-5, 0, "west")]
        public void It_turns_towards_the_player(double dx, double dz, string expected)
        {
            Assert.Equal(expected, PlacementFacing.FromOffset(dx, dz).Code);
        }

        [Theory]
        [InlineData(5, 4, "east")]
        [InlineData(4, 5, "south")]
        [InlineData(-5, -4, "west")]
        [InlineData(-4, -5, "north")]
        public void The_longer_side_of_the_offset_decides(double dx, double dz, string expected)
        {
            Assert.Equal(expected, PlacementFacing.FromOffset(dx, dz).Code);
        }

        [Fact]
        public void Standing_exactly_on_the_diagonal_still_picks_one()
        {
            // No preference is worth arguing over, but it must not be undefined.
            Assert.NotNull(PlacementFacing.FromOffset(5, 5));
            Assert.NotNull(PlacementFacing.FromOffset(0, 0));
        }

        [Fact]
        public void Without_a_player_it_falls_back_rather_than_throwing()
        {
            // Placement can be driven by things other than a person standing there.
            Assert.Equal(BlockFacing.NORTH, PlacementFacing.TowardsPlayer(null, null));
        }
    }
}
