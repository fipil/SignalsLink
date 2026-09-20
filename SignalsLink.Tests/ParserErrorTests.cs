using System.Collections.Generic;
using System.Linq;
using SignalsLink.src.signals.paperConditions;

namespace SignalsLink.Tests
{
    /// <summary>
    /// Mistakes in a paper are collected, not thrown: a paper is always accepted so that one
    /// mistyped line cannot stop a device while the rest is still being written. These check that
    /// what is collected actually names the line and the reason.
    /// </summary>
    public class ParserErrorTests
    {
        [Fact]
        public void A_clean_paper_reports_nothing()
        {
            Assert.Empty(ErrorsIn("game:firewood\ntarget 2\namount 8\n"));
        }

        [Fact]
        public void An_unknown_scope_is_reported_with_its_line()
        {
            var errors = ErrorsIn("game:firewood\nin nowhere\ntarget 2\n");

            PaperConditionError error = Assert.Single(errors);
            Assert.Equal(2, error.Line);
            Assert.Equal("scope", error.Reason);
            Assert.Equal("in nowhere", error.Text);
        }

        [Fact]
        public void An_out_of_range_output_is_reported()
        {
            var errors = ErrorsIn("in target\ngame:firewood 96\noutput 42\n");

            PaperConditionError error = Assert.Single(errors);
            Assert.Equal(3, error.Line);
            Assert.Equal("output", error.Reason);
        }

        [Fact]
        public void Each_directive_reports_its_own_reason()
        {
            Assert.Equal("source", Assert.Single(ErrorsIn("game:firewood\nsource nope\n")).Reason);
            Assert.Equal("target", Assert.Single(ErrorsIn("game:firewood\ntarget sideways\n")).Reason);
            Assert.Equal("amount", Assert.Single(ErrorsIn("game:firewood\namount lots\n")).Reason);
            Assert.Equal("action", Assert.Single(ErrorsIn("game:firewood\ndo something\n")).Reason);
        }

        [Fact]
        public void In_source_in_an_output_block_is_reported_on_the_line_it_is_written()
        {
            // It still works - the output rail answers it against the target - but it means
            // something other than what it says, so it gets named.
            var errors = ErrorsIn("in source\ngame:firewood 96\noutput 5\n");

            PaperConditionError error = Assert.Single(errors);
            Assert.Equal(1, error.Line);
            Assert.Equal("outputsource", error.Reason);
        }

        [Fact]
        public void In_source_is_fine_in_a_block_that_carries_no_output()
        {
            Assert.Empty(ErrorsIn("in source\ngame:firewood 96\ntarget 2\n"));
        }

        [Fact]
        public void Line_numbers_count_the_blank_lines_between_blocks()
        {
            // The number has to be the line the player is looking at in their own paper, which
            // means counting everything - blank lines and comments included.
            var errors = ErrorsIn(
                "game:firewood\n" +   // 1
                "target 2\n" +        // 2
                "\n" +                // 3
                "# a note\n" +        // 4
                "game:plank\n" +      // 5
                "target sideways\n"); // 6

            PaperConditionError error = Assert.Single(errors);
            Assert.Equal(6, error.Line);
        }

        [Fact]
        public void A_bad_line_does_not_stop_the_rest_of_the_paper()
        {
            var errors = new List<PaperConditionError>();
            CompiledConditions compiled = PaperConditionsParser.Parse(
                "game:firewood\ntarget sideways\n\nin target\ngame:firewood 96\noutput 15\n", errors);

            Assert.NotEmpty(errors);
            Assert.True(compiled.HasAnyOutput);   // the second block compiled regardless
        }

        private static IReadOnlyList<PaperConditionError> ErrorsIn(string paper)
        {
            var errors = new List<PaperConditionError>();
            PaperConditionsParser.Parse(paper, errors);
            return errors;
        }
    }
}
