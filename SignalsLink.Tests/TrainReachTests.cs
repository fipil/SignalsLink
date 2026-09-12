using SignalsLink.YTT.src.train;
using Vintagestory.API.MathTools;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>
    /// `load train north5` — five blocks north, counted by stepping off the dock.
    ///
    /// A bare direction is a half-plane and cannot tell two parallel tracks apart. Counting the
    /// steps can, and the slack is the vehicle's own width: two blocks on standard gauge, one on
    /// a mine cart.
    /// </summary>
    public class TrainReachTests
    {
        /// <summary>The dock, at the middle of its block.</summary>
        private static readonly Vec3d Dock = new Vec3d(100.5, 4.5, 100.5);

        // ---------------------------------------------------------------- a bare direction

        [Fact]
        public void North_takes_anything_with_a_part_of_it_north()
        {
            Assert.True(Wide(z: 95.5).Covers(BlockFacing.NORTH, null));
            Assert.False(Wide(z: 95.5).Covers(BlockFacing.SOUTH, null));
        }

        [Fact]
        public void A_vehicle_across_the_dock_is_on_every_side()
        {
            // The honest answer, and the reason a bare direction cannot separate two tracks.
            Reach across = new Reach(80, 120, 99.5, 101.5);

            Assert.True(across.Covers(BlockFacing.NORTH, null));
            Assert.True(across.Covers(BlockFacing.SOUTH, null));
        }

        // ---------------------------------------------------------------- counted steps

        [Fact]
        public void Five_steps_north_finds_the_track_five_blocks_north()
        {
            Assert.True(Wide(z: 95.5).Covers(BlockFacing.NORTH, 5));
        }

        [Fact]
        public void And_not_the_one_on_the_far_track()
        {
            // The whole point: two parallel tracks, one dock each.
            Assert.False(Wide(z: 95.5).Covers(BlockFacing.NORTH, 9));
            Assert.False(Wide(z: 91.5).Covers(BlockFacing.NORTH, 5));
        }

        [Fact]
        public void Nor_the_same_distance_on_the_other_side()
        {
            Assert.False(Wide(z: 95.5).Covers(BlockFacing.SOUTH, 5));
        }

        [Theory]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        public void A_wide_gauge_train_forgives_a_miscount_of_one(int steps)
        {
            // Two blocks of body, so the counted block still lands on it.
            Assert.True(Wide(z: 95.5).Covers(BlockFacing.NORTH, steps));
        }

        [Fact]
        public void A_mine_cart_does_not()
        {
            // One block of body. This is worth knowing before building a narrow-gauge yard.
            Assert.True(Narrow(z: 95.5).Covers(BlockFacing.NORTH, 5));
            Assert.False(Narrow(z: 95.5).Covers(BlockFacing.NORTH, 7));
        }

        [Fact]
        public void East_and_west_count_along_the_other_axis()
        {
            Assert.True(WideEastWest(x: 105.5).Covers(BlockFacing.EAST, 5));
            Assert.True(WideEastWest(x: 95.5).Covers(BlockFacing.WEST, 5));
            Assert.False(WideEastWest(x: 105.5).Covers(BlockFacing.WEST, 5));
        }

        [Fact]
        public void A_train_lying_along_the_counted_axis_matches_wherever_it_reaches()
        {
            // A track pointing AT the dock: the body covers many distances, so counting steps
            // cannot single one out. Correct, and worth having written down.
            Reach alongside = new Reach(99.5, 101.5, 80, 99);

            Assert.True(alongside.Covers(BlockFacing.NORTH, 3));
            Assert.True(alongside.Covers(BlockFacing.NORTH, 10));
        }

        [Fact]
        public void No_direction_asks_nothing()
        {
            Assert.True(Wide(z: 95.5).Covers(null, null));
            Assert.True(Wide(z: 95.5).Covers(null, 5));
        }

        /// <summary>A standard gauge vehicle on an east-west track: two blocks wide.</summary>
        private static Reach Wide(double z) => new Reach(90, 110, z - 1, z + 1);

        /// <summary>A mine cart on an east-west track: one block wide.</summary>
        private static Reach Narrow(double z) => new Reach(90, 110, z - 0.5, z + 0.5);

        /// <summary>A standard gauge vehicle on a north-south track.</summary>
        private static Reach WideEastWest(double x) => new Reach(x - 1, x + 1, 90, 110);

        private sealed class Reach
        {
            private readonly double minX, maxX, minZ, maxZ;

            public Reach(double minX, double maxX, double minZ, double maxZ)
            {
                this.minX = minX;
                this.maxX = maxX;
                this.minZ = minZ;
                this.maxZ = maxZ;
            }

            public bool Covers(BlockFacing direction, int? distance)
            {
                return TrainReach.Covers(minX, maxX, minZ, maxZ, Dock, direction, distance);
            }
        }
    }
}
