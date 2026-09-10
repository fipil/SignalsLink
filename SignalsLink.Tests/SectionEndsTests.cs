using System.Collections.Generic;
using SignalsLink.src.signals.paperConditions;

namespace SignalsLink.Tests
{
    /// <summary>
    /// The header names the two ends of an exchange.
    ///
    /// This is the grammar that lets a device be a router rather than a warehouse: goods can go
    /// from one other party straight to another without being carried through the device's own
    /// crate. `unload X` and `load X` stay as the short ways of saying "to me" and "from me".
    ///
    /// An empty side means <b>the device itself</b>. That rule is what keeps the parser ignorant of
    /// what any holder is called - there is no `dock` keyword to teach it.
    /// </summary>
    public class SectionEndsTests
    {
        [Fact]
        public void From_to_names_both_ends()
        {
            ConditionSection section = Header("from train to yard");

            Assert.Equal(new[] { "train" }, section.SourceTokens);
            Assert.Equal(new[] { "yard" }, section.TargetTokens);
        }

        [Fact]
        public void Each_end_keeps_the_whole_of_its_specification()
        {
            ConditionSection section = Header("from train engine to yard north coal");

            Assert.Equal(new[] { "train", "engine" }, section.SourceTokens);
            Assert.Equal(new[] { "yard", "north", "coal" }, section.TargetTokens);
        }

        [Fact]
        public void Unload_means_from_there_to_me()
        {
            ConditionSection section = Header("unload yard north");

            Assert.Equal(new[] { "yard", "north" }, section.SourceTokens);
            Assert.Empty(section.TargetTokens);
        }

        [Fact]
        public void Load_means_from_me_to_there()
        {
            ConditionSection section = Header("load yard north");

            Assert.Empty(section.SourceTokens);
            Assert.Equal(new[] { "yard", "north" }, section.TargetTokens);
        }

        [Fact]
        public void An_empty_side_is_the_device_itself()
        {
            // Not a keyword the parser has to know - just "nothing said", which the device reads as
            // "me". That is the seam: the parser never learns what a holder is called.
            ConditionSection section = Header("unload yard");

            Assert.True(section.TargetIsDevice);
            Assert.False(section.SourceIsDevice);
        }

        [Fact]
        public void With_from_to_neither_end_is_the_device()
        {
            ConditionSection section = Header("from train to yard");

            Assert.False(section.SourceIsDevice);
            Assert.False(section.TargetIsDevice);
        }

        [Fact]
        public void The_direction_word_may_still_stand_anywhere()
        {
            // The old rule for the short forms, kept: tokens on a header do not depend on order.
            ConditionSection section = Header("yard north unload");

            Assert.Equal(new[] { "yard", "north" }, section.SourceTokens);
            Assert.True(section.TargetIsDevice);
        }

        [Fact]
        public void A_line_with_no_direction_and_no_from_is_not_a_header()
        {
            Assert.False(ConditionSection.TryParseHeader("game:firewood", 1, out _));
            Assert.False(ConditionSection.TryParseHeader("to yard", 1, out _));
        }

        [Fact]
        public void Case_does_not_matter()
        {
            ConditionSection section = Header("From Train To Yard");

            Assert.Equal(new[] { "Train" }, section.SourceTokens);
            Assert.Equal(new[] { "Yard" }, section.TargetTokens);
        }

        // ---------------------------------------------------------------- what is wrong with it

        [Fact]
        public void From_without_to_is_still_a_header_so_it_can_be_reported()
        {
            // Falling through to "this is a condition line" would turn every line of the section
            // into nonsense and bury the one real mistake.
            Assert.True(ConditionSection.TryParseHeader("from train", 1, out ConditionSection section));

            Assert.Equal(new[] { "train" }, section.SourceTokens);
            Assert.Empty(section.TargetTokens);
        }

        [Fact]
        public void The_long_form_has_to_name_both_ends()
        {
            // Someone who writes `from train` and stops was on their way to saying where the goods
            // go. Reading it as `unload train` would be finishing their sentence for them.
            Assert.Equal("sectionends", Assert.Single(Errors("from train\n\ngame:firewood\n")).Reason);
        }

        [Fact]
        public void A_header_naming_neither_end_is_reported()
        {
            // `unload` on its own says "from me to me".
            IReadOnlyList<PaperConditionError> errors = Errors("unload\n\ngame:firewood\n");

            Assert.Equal("sectionends", Assert.Single(errors).Reason);
        }

        [Fact]
        public void From_with_nothing_after_to_is_reported()
        {
            IReadOnlyList<PaperConditionError> errors = Errors("from train to\n\ngame:firewood\n");

            Assert.Equal("sectionends", Assert.Single(errors).Reason);
        }

        [Fact]
        public void The_report_points_at_the_header_line()
        {
            IReadOnlyList<PaperConditionError> errors = Errors("# a paper\n\nunload\n\ngame:firewood\n");

            Assert.Equal(3, Assert.Single(errors).Line);
        }

        [Fact]
        public void A_header_with_both_ends_named_is_not_reported()
        {
            Assert.DoesNotContain(Errors("from train to yard\n\ngame:firewood\n"), e => e.Reason == "sectionends");
            Assert.DoesNotContain(Errors("unload yard\n\ngame:firewood\n"), e => e.Reason == "sectionends");
        }

        // ---------------------------------------------------------------- plumbing

        private static ConditionSection Header(string line)
        {
            Assert.True(ConditionSection.TryParseHeader(line, 1, out ConditionSection section));
            return section;
        }

        private static IReadOnlyList<PaperConditionError> Errors(string paper)
        {
            return BlockBehaviorPaperConditions.FindErrors(paper, new Host());
        }

        private sealed class Host : IPaperConditionsHost
        {
            public string ConditionsText { get; set; }
            public int SignalInputsCount => 2;
            public bool RequiresTransferSelector => false;
            public bool SupportsSections => true;
            public bool RequiresSections => true;
        }
    }
}
