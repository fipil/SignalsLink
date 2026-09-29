using System.Collections.Generic;
using SignalsLink.src.signals.paperConditions;

namespace SignalsLink.Tests
{
    /// <summary>
    /// `height N` - how high goods may be stacked where they are put down.
    ///
    /// It stands on its own rather than hanging off `target ground`, because under a section header
    /// the ground is already implied and there would be nothing to hang it on. Height belongs to
    /// the CARGO: logs stack five high, barrels want one layer so they can be reached.
    /// </summary>
    public class HeightDirectiveTests
    {
        [Fact]
        public void Height_sets_how_high_a_column_may_grow()
        {
            ConditionBlock block = Single("game:log-*\nheight 5\n");

            Assert.Equal(5, block.Directives.TargetGroundHeight);
        }

        [Fact]
        public void Without_it_a_column_is_one_block_high()
        {
            ConditionBlock block = Single("game:barrel\n");

            Assert.Equal(1, block.Directives.TargetGroundHeight);
        }

        [Fact]
        public void Target_ground_N_still_says_the_same_thing()
        {
            // The older spelling stays - papers in existing worlds are written that way.
            ConditionBlock block = Single("game:log-*\ntarget ground 5\n");

            Assert.Equal(5, block.Directives.TargetGroundHeight);
            Assert.True(block.Directives.TargetGround);
        }

        [Theory]
        [InlineData("height 0")]
        [InlineData("height 256")]
        [InlineData("height high")]
        [InlineData("height 2 3")]
        public void A_height_that_is_not_a_number_in_range_is_a_paper_error(string line)
        {
            IReadOnlyList<PaperConditionError> errors = Errors("game:log-*\n" + line + "\n");

            Assert.Equal("height", Assert.Single(errors).Reason);
        }

        [Fact]
        public void The_error_points_at_the_line_it_is_on()
        {
            IReadOnlyList<PaperConditionError> errors = Errors("game:log-*\nheight nope\n");

            Assert.Equal(2, Assert.Single(errors).Line);
        }

        // ---------------------------------------------------------------- plumbing

        private static ConditionBlock Single(string paper)
        {
            return Assert.Single(PaperConditionsParser.Parse(paper).Blocks);
        }

        private static IReadOnlyList<PaperConditionError> Errors(string paper)
        {
            List<PaperConditionError> errors = new List<PaperConditionError>();
            PaperConditionsParser.Parse(paper, errors);
            return errors;
        }
    }
}
