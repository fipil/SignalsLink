using SignalsLink.src.signals.yard;
using Vintagestory.API.MathTools;

namespace SignalsLink.Tests
{
    /// <summary>
    /// Reading a neighbour out of the extended chunk array.
    ///
    /// This is the arithmetic that was wrong for a fortnight, and it was wrong in the worst way:
    /// a guessed axis order reads a real block, just the wrong one, so the border appeared and
    /// vanished by position and every look at it suggested a different culprit. Nothing about it
    /// needs a game, so nothing about it should ever be guessed again.
    ///
    /// The array is a chunk (32) with one block of its neighbours on each side, so 34 to a side.
    /// Which axis runs fastest is the engine's business and is documented nowhere - so it is worked
    /// out from a block whose index and position are both known.
    /// </summary>
    public class ExtendedChunkLayoutTests
    {
        private const int Side = 34;
        private const int Border = 1;

        [Fact]
        public void The_layout_the_code_used_to_assume_is_recognised()
        {
            // index = (y * 34 + z) * 34 + x
            var layout = Resolve(new BlockPos(37, 10, 70, 0), (a, b, c) => (b * Side + c) * Side + a);

            Assert.True(layout.Known);
            Assert.Equal(1, layout.StepX);
            Assert.Equal(Side, layout.StepZ);
        }

        [Fact]
        public void So_is_the_one_where_z_runs_fastest()
        {
            // index = (y * 34 + x) * 34 + z
            var layout = Resolve(new BlockPos(37, 10, 70, 0), (a, b, c) => (b * Side + a) * Side + c);

            Assert.True(layout.Known);
            Assert.Equal(Side, layout.StepX);
            Assert.Equal(1, layout.StepZ);
        }

        [Fact]
        public void And_the_one_where_y_runs_fastest()
        {
            // index = (x * 34 + z) * 34 + y
            var layout = Resolve(new BlockPos(37, 10, 70, 0), (a, b, c) => (a * Side + c) * Side + b);

            Assert.True(layout.Known);
            Assert.Equal(Side * Side, layout.StepX);
            Assert.Equal(Side, layout.StepZ);
        }

        [Fact]
        public void Negative_coordinates_are_the_ordinary_case()
        {
            // The yard this went wrong on stands at -8, 3, -22. A remainder that keeps the sign
            // would put the block outside the array and every neighbour lookup with it.
            var layout = Resolve(new BlockPos(-8, 3, -22, 0), (a, b, c) => (b * Side + c) * Side + a);

            Assert.True(layout.Known);
            Assert.Equal(1, layout.StepX);
            Assert.Equal(Side, layout.StepZ);
        }

        [Fact]
        public void A_step_leads_to_the_neighbouring_cell_and_no_further()
        {
            BlockPos pos = new BlockPos(37, 10, 70, 0);
            var layout = Resolve(pos, Index);

            int here = Index(Local(pos.X), Local(pos.Y), Local(pos.Z));
            int east = Index(Local(pos.X) + 1, Local(pos.Y), Local(pos.Z));
            int north = Index(Local(pos.X), Local(pos.Y), Local(pos.Z) - 1);

            Assert.Equal(east, here + layout.StepFor(BlockFacing.EAST));
            Assert.Equal(north, here + layout.StepFor(BlockFacing.NORTH));
        }

        [Fact]
        public void Going_east_and_back_west_is_where_it_started()
        {
            var layout = Resolve(new BlockPos(37, 10, 70, 0), Index);

            Assert.Equal(0, layout.StepFor(BlockFacing.EAST) + layout.StepFor(BlockFacing.WEST));
            Assert.Equal(0, layout.StepFor(BlockFacing.NORTH) + layout.StepFor(BlockFacing.SOUTH));
        }

        [Fact]
        public void A_block_whose_coordinates_coincide_keeps_what_was_known()
        {
            // 5, 5, 5 within its chunk: three axes that cannot be told apart. Rather than pick one
            // at random, the answer from the block before it stands.
            var known = Resolve(new BlockPos(37, 10, 70, 0), Index);
            var kept = BlockYardTile.ExtendedChunkLayout.Of(Side * Side * Side, 0, new BlockPos(5, 5, 5, 0), known);

            Assert.True(kept.Known);
            Assert.Equal(known.StepX, kept.StepX);
            Assert.Equal(known.StepZ, kept.StepZ);
        }

        [Fact]
        public void An_index_that_matches_nothing_leaves_the_answer_alone()
        {
            var nothing = BlockYardTile.ExtendedChunkLayout.Of(Side * Side * Side, 999999, new BlockPos(37, 10, 70, 0), default);

            Assert.False(nothing.Known);
        }

        [Fact]
        public void An_array_that_is_not_a_cube_of_the_right_size_is_refused()
        {
            var nothing = BlockYardTile.ExtendedChunkLayout.Of(1000, 0, new BlockPos(37, 10, 70, 0), default);

            Assert.False(nothing.Known);
        }

        // ---------------------------------------------------------------- plumbing

        private static int Local(int coordinate)
        {
            int wrapped = coordinate % 32;
            return (wrapped < 0 ? wrapped + 32 : wrapped) + Border;
        }

        /// <summary>The layout this game build actually uses, as far as these tests are concerned.</summary>
        private static int Index(int x, int y, int z)
        {
            return (y * Side + z) * Side + x;
        }

        private static BlockYardTile.ExtendedChunkLayout Resolve(BlockPos pos, System.Func<int, int, int, int> index)
        {
            int extIndex3d = index(Local(pos.X), Local(pos.Y), Local(pos.Z));

            return BlockYardTile.ExtendedChunkLayout.Of(Side * Side * Side, extIndex3d, pos, default);
        }
    }
}
