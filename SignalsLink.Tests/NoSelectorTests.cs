using System.Collections.Generic;
using System.Linq;
using SignalsLink.src.signals.paperConditions;

namespace SignalsLink.Tests
{
    /// <summary>
    /// A transfer block has to say WHAT to carry. One built only from `in target` conditions never
    /// picks anything and so does nothing, forever — the hardest kind of mistake to spot, because
    /// the paper reads perfectly sensibly. It is reported.
    ///
    /// Not everywhere, though: a valve needs no selector (the far end of the hose is its source)
    /// and a sensor carries nothing at all, so neither is nagged about it.
    /// </summary>
    public class NoSelectorTests
    {
        private const string TargetOnly =
            "in target\n" +
            "game:water-* 10+ slot 1\n" +
            "target 1\n";

        [Fact]
        public void A_transfer_block_with_nothing_to_select_is_reported()
        {
            PaperConditionError error = Assert.Single(ErrorsFor(TargetOnly, new Host(true)));

            Assert.Equal("noselector", error.Reason);
            Assert.Equal(1, error.Line);
        }

        [Fact]
        public void A_device_that_needs_no_selector_is_left_alone()
        {
            Assert.Empty(ErrorsFor(TargetOnly, new Host(false)));
        }

        [Fact]
        public void An_output_block_needs_no_selector()
        {
            // It carries nothing, so there is nothing for it to choose.
            Assert.Empty(ErrorsFor("in target\ngame:firewood 96+\noutput 15\n", new Host(true)));
        }

        [Fact]
        public void A_star_counts_as_saying_what_to_carry()
        {
            // "Anything" is a perfectly good answer, as long as it is written down.
            Assert.Empty(ErrorsFor("*\nin target\ngame:water-* 10+ slot 1\ntarget 1\n", new Host(true)));
        }

        [Fact]
        public void A_gate_alone_does_not_count()
        {
            // A slot-qualified amount says when, never what.
            PaperConditionError error = Assert.Single(
                ErrorsFor("game:firewood 23+ slot 5\ntarget 1\n", new Host(true)));

            Assert.Equal("noselector", error.Reason);
        }

        [Fact]
        public void The_reported_line_is_where_the_block_starts()
        {
            string paper =
                "game:firewood\n" +   // 1
                "target 1\n" +        // 2
                "\n" +                // 3
                "in target\n" +       // 4  <- the block that cannot select
                "game:plank 5+\n" +   // 5
                "target 2\n";         // 6

            PaperConditionError error = Assert.Single(ErrorsFor(paper, new Host(true)));

            Assert.Equal(4, error.Line);
        }

        // ---------------------------------------------------------------- plumbing

        private static IReadOnlyList<PaperConditionError> ErrorsFor(string paper, IPaperConditionsHost host)
        {
            return BlockBehaviorPaperConditions.FindErrors(paper, host)
                .Where(error => error.Reason == "noselector")
                .ToList();
        }

        private sealed class Host : IPaperConditionsHost
        {
            public Host(bool requiresTransferSelector)
            {
                RequiresTransferSelector = requiresTransferSelector;
            }

            public string ConditionsText { get; set; }
            public int SignalInputsCount => 3;
            public bool RequiresTransferSelector { get; }
        }
    }
}
