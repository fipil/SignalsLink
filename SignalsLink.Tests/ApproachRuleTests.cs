using SignalsLink.src.signals.chunkanchor;
using Vintagestory.API.MathTools;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>
    /// When "a vehicle is on its way to X" should wake a sleeping anchor: X has to be its ground,
    /// and the vehicle has to be near enough that loading the ground now is not paying for a
    /// journey.
    /// </summary>
    public class ApproachRuleTests
    {
        [Fact]
        public void A_station_on_the_anchors_own_columns_concerns_it()
        {
            Assert.True(ApproachRule.Concerns(new[] { K(10, 10), K(10, 11) }, K(10, 11)));
        }

        [Fact]
        public void And_so_does_one_right_next_to_them()
        {
            // The station block stands at the end of a platform; the platform is what is held.
            Assert.True(ApproachRule.Concerns(new[] { K(10, 10) }, K(11, 11)));
        }

        [Fact]
        public void Two_columns_away_does_not()
        {
            Assert.False(ApproachRule.Concerns(new[] { K(10, 10) }, K(12, 10)));
        }

        [Fact]
        public void An_anchor_holding_nothing_is_concerned_by_nothing()
        {
            Assert.False(ApproachRule.Concerns(new long[0], K(10, 10)));
            Assert.False(ApproachRule.Concerns(null, K(10, 10)));
        }

        [Fact]
        public void Near_is_measured_flat_from_the_middle_of_the_target_block()
        {
            BlockPos target = new BlockPos(100, 5, 100);

            Assert.True(ApproachRule.Close(new Vec3d(100.5, 40, 250.5), target, 150));
            Assert.False(ApproachRule.Close(new Vec3d(100.5, 40, 251.5), target, 150));
        }

        [Fact]
        public void No_distance_means_never()
        {
            Assert.False(ApproachRule.Close(new Vec3d(100.5, 5, 100.5), new BlockPos(100, 5, 100), 0));
        }

        private static long K(int x, int z) => AnchorArea.Key(x, z);
    }
}
