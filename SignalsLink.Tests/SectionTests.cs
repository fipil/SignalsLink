using System.Collections.Generic;
using System.Linq;
using SignalsLink.src.signals.paperConditions;

namespace SignalsLink.Tests
{
    /// <summary>
    /// Sections: the layer that lets a device with two ends of its own say which way a block is
    /// going. A header rebinds what `in source` and `in target` point at; it adds no vocabulary.
    ///
    /// The point these tests defend hardest is that a paper WITHOUT a header still compiles to
    /// exactly one section, so the chute, valve, damper and sensor cannot tell that any of this
    /// was added.
    /// </summary>
    public class SectionTests
    {
        [Fact]
        public void A_paper_without_a_header_is_one_implicit_section()
        {
            CompiledConditions compiled = Parse("game:firewood\ntarget 2\n\ngame:plank\ntarget 3\n");

            ConditionSection section = Assert.Single(compiled.Sections);
            Assert.True(section.IsImplicit);
            Assert.False(compiled.HasExplicitSections);
            Assert.Equal(2, section.Blocks.Count);
            Assert.Equal(2, compiled.Blocks.Count);
        }

        [Fact]
        public void A_header_opens_a_section_and_the_blocks_below_belong_to_it()
        {
            CompiledConditions compiled = Parse(
                "unload\n" +
                "\n" +
                "game:firewood\n" +
                "target 2\n" +
                "\n" +
                "load\n" +
                "\n" +
                "game:plank\n" +
                "target 3\n");

            Assert.True(compiled.HasExplicitSections);
            Assert.Equal(2, compiled.Sections.Count);

            Assert.Equal("unload", compiled.Sections[0].Direction);
            Assert.Single(compiled.Sections[0].Blocks);

            Assert.Equal("load", compiled.Sections[1].Direction);
            Assert.Single(compiled.Sections[1].Blocks);
        }

        [Fact]
        public void The_tokens_after_the_direction_are_kept_but_not_interpreted()
        {
            // Which holder they name, and what `north` means, is the finder's business - the
            // parser never learns it, so a new kind of vehicle never touches the parser.
            CompiledConditions compiled = Parse("unload train north\n\ngame:firewood\ntarget 2\n");

            ConditionSection section = Assert.Single(compiled.Sections);
            Assert.Equal("unload", section.Direction);
            Assert.Equal(new[] { "train", "north" }, section.SourceTokens.ToArray());
        }

        [Fact]
        public void The_direction_may_stand_anywhere_on_the_header()
        {
            CompiledConditions compiled = Parse("yard load\n\ngame:firewood\ntarget 2\n");

            ConditionSection section = Assert.Single(compiled.Sections);
            Assert.Equal("load", section.Direction);
            Assert.Equal(new[] { "yard" }, section.TargetTokens.ToArray());
        }

        [Fact]
        public void Two_sections_with_the_same_header_are_one_section()
        {
            CompiledConditions compiled = Parse(
                "load yard\n\ngame:firewood\ntarget 2\n\n" +
                "load yard\n\ngame:plank\ntarget 3\n");

            ConditionSection section = Assert.Single(compiled.Sections);
            Assert.Equal(2, section.Blocks.Count);
        }

        [Fact]
        public void Headers_differing_in_their_tokens_are_different_sections()
        {
            CompiledConditions compiled = Parse(
                "unload train 1\n\ngame:firewood\ntarget 2\n\n" +
                "unload train 3\n\ngame:plank\ntarget 3\n");

            Assert.Equal(2, compiled.Sections.Count);
        }

        [Fact]
        public void A_header_must_stand_on_a_paragraph_of_its_own()
        {
            // Otherwise a condition line that happens to start with the same word would open a
            // section, which is a trap nobody would find.
            CompiledConditions compiled = Parse("unload\ngame:firewood\ntarget 2\n");

            Assert.False(compiled.HasExplicitSections);
        }

        [Fact]
        public void Blocks_above_the_first_header_are_reported_and_dropped()
        {
            var errors = new List<PaperConditionError>();
            CompiledConditions compiled = PaperConditionsParser.Parse(
                "game:firewood\n" +   // 1  <- orphan
                "target 2\n" +        // 2
                "\n" +
                "unload\n" +          // 4
                "\n" +
                "game:plank\n" +      // 6
                "target 3\n", errors);

            PaperConditionError error = Assert.Single(errors);
            Assert.Equal("sectionorphan", error.Reason);
            Assert.Equal(1, error.Line);

            // Dropped, not quietly read as "unload".
            Assert.Single(compiled.Blocks);
            Assert.Single(compiled.Sections);
        }

        [Fact]
        public void Comments_do_not_stop_a_paragraph_being_a_header()
        {
            CompiledConditions compiled = Parse("# vylozit vlak\nunload train\n\ngame:firewood\ntarget 2\n");

            ConditionSection section = Assert.Single(compiled.Sections);
            Assert.Equal("unload", section.Direction);
        }

        [Fact]
        public void The_header_remembers_which_line_it_is_on()
        {
            CompiledConditions compiled = Parse("unload\n\ngame:firewood\ntarget 2\n");

            Assert.Equal(1, compiled.Sections[0].FirstLine);
        }

        // ---------------------------------------------------------------- device support

        [Fact]
        public void A_header_on_a_device_without_sections_is_reported()
        {
            IReadOnlyList<PaperConditionError> errors = BlockBehaviorPaperConditions.FindErrors(
                "unload\n\ngame:firewood\ntarget 2\n", new Host(supportsSections: false));

            Assert.Contains(errors, e => e.Reason == "sectionunsupported");
        }

        [Fact]
        public void A_header_on_a_device_with_sections_is_fine()
        {
            IReadOnlyList<PaperConditionError> errors = BlockBehaviorPaperConditions.FindErrors(
                "unload\n\ngame:firewood\ntarget 2\n", new Host(supportsSections: true));

            Assert.DoesNotContain(errors, e => e.Reason == "sectionunsupported");
        }

        [Fact]
        public void A_paper_without_headers_is_never_reported()
        {
            IReadOnlyList<PaperConditionError> errors = BlockBehaviorPaperConditions.FindErrors(
                "game:firewood\ntarget 2\n", new Host(supportsSections: false));

            Assert.DoesNotContain(errors, e => e.Reason == "sectionunsupported");
        }

        // ---------------------------------------------------------------- plumbing

        private static CompiledConditions Parse(string paper)
        {
            return PaperConditionsParser.Parse(paper);
        }

        private sealed class Host : IPaperConditionsHost
        {
            public Host(bool supportsSections)
            {
                SupportsSections = supportsSections;
            }

            public string ConditionsText { get; set; }
            public int SignalInputsCount => 2;
            public bool RequiresTransferSelector => false;
            public bool SupportsSections { get; }
        }
    }
}
