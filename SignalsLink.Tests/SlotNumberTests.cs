using System.Collections.Generic;
using SignalsLink.src.signals.paperConditions;

namespace SignalsLink.Tests
{
    /// <summary>
    /// Slot numbers on paper. The old 1-14 cap only ever mirrored what a four-bit signal pin can
    /// count to; a composite inventory of several wagons is hundreds of slots, so it had to go.
    /// </summary>
    public class SlotNumberTests
    {
        [Fact]
        public void A_slot_number_above_fourteen_is_accepted()
        {
            PaperConditionDirectives directives = FirstBlock("game:firewood\ntarget 48\n").Directives;

            Assert.Equal(48, directives.TargetSlot);
        }

        [Fact]
        public void So_is_a_source_slot_above_fourteen()
        {
            PaperConditionDirectives directives = FirstBlock("game:firewood\nsource 320\ntarget 2\n").Directives;

            Assert.Equal(320, directives.SourceSlot);
        }

        [Fact]
        public void Two_hundred_and_fifty_six_still_fits()
        {
            // It used to be a byte, so this is the number that would have silently wrapped.
            PaperConditionDirectives directives = FirstBlock("game:firewood\ntarget 256\n").Directives;

            Assert.Equal(256, directives.TargetSlot);
        }

        [Fact]
        public void Zero_and_negative_are_still_refused()
        {
            Assert.Contains(ErrorsIn("game:firewood\ntarget 0\n"), e => e.Reason == "target");
            Assert.Contains(ErrorsIn("game:firewood\nsource 0\n"), e => e.Reason == "source");
        }

        [Fact]
        public void Last_names_the_end_of_the_inventory()
        {
            PaperConditionDirectives source = FirstBlock("game:firewood\nsource last\ntarget 2\n").Directives;
            Assert.True(source.SourceLast);
            Assert.Null(source.SourceSlot);

            PaperConditionDirectives target = FirstBlock("game:firewood\ntarget last\n").Directives;
            Assert.True(target.TargetLast);
            Assert.Null(target.TargetSlot);
        }

        [Fact]
        public void Last_combines_with_ifEmpty()
        {
            PaperConditionDirectives directives = FirstBlock("game:firewood\ntarget last ifEmpty\n").Directives;

            Assert.True(directives.TargetLast);
            Assert.True(directives.RequireTargetEmpty);
        }

        [Fact]
        public void Fifteen_on_paper_means_slot_fifteen()
        {
            // On the Source PIN 15 means "the last slot". On paper it must stay literal, which is
            // exactly why `last` is a word and not a number.
            PaperConditionDirectives directives = FirstBlock("game:firewood\nsource 15\ntarget 2\n").Directives;

            Assert.Equal(15, directives.SourceSlot);
            Assert.False(directives.SourceLast);
        }

        // ---------------------------------------------------------------- plumbing

        private static ConditionBlock FirstBlock(string paper)
        {
            var evaluator = new PaperConditionsEvaluator();
            evaluator.SetConditionsText(paper);
            return evaluator.GetBlocks()[0];
        }

        private static IReadOnlyList<PaperConditionError> ErrorsIn(string paper)
        {
            var errors = new List<PaperConditionError>();
            PaperConditionsParser.Parse(paper, errors);
            return errors;
        }
    }
}
