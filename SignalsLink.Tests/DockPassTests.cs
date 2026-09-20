using System.Collections.Generic;
using System.Linq;
using SignalsLink.src.signals.manageddock;
using SignalsLink.src.signals.paperConditions;

namespace SignalsLink.Tests
{
    /// <summary>
    /// One tick of a device with sections. What is worth defending here is that the sections share
    /// one action rail and one output pin - a paper with three headers must not do three times the
    /// work of a paper with none, and the pin must read the same however the player spread their
    /// output blocks around.
    ///
    /// The running of a section is handed in, so all of this can be checked without a world.
    /// </summary>
    public class DockPassTests
    {
        [Fact]
        public void Sections_run_in_the_order_they_are_written()
        {
            var seen = new List<string>();

            DockPass.Run(Sections("unload yard", "load train", "load boat"),
                (section, blocked) =>
                {
                    seen.Add(section.Header);
                    return SectionPassResult.Nothing;
                },
                out _);

            Assert.Equal(new[] { "unload yard", "load train", "load boat" }, seen);
        }

        [Fact]
        public void One_action_per_tick_across_the_whole_paper()
        {
            // Not per section: an action closes the rail for the sections below it, exactly as it
            // closes it for the blocks below it.
            var blockedAt = new List<bool>();

            DockPass.Run(Sections("unload", "load", "load yard"),
                (section, blocked) =>
                {
                    blockedAt.Add(blocked);
                    return new SectionPassResult(section.Header == "load", false, 0);
                },
                out bool actionPerformed);

            Assert.Equal(new[] { false, false, true }, blockedAt);
            Assert.True(actionPerformed);
        }

        [Fact]
        public void A_section_that_did_nothing_leaves_the_rail_open()
        {
            var blockedAt = new List<bool>();

            DockPass.Run(Sections("unload", "load"),
                (section, blocked) =>
                {
                    blockedAt.Add(blocked);
                    return SectionPassResult.Nothing;
                },
                out bool actionPerformed);

            Assert.Equal(new[] { false, false }, blockedAt);
            Assert.False(actionPerformed);
        }

        [Fact]
        public void The_first_output_block_on_the_paper_wins()
        {
            byte output = DockPass.Run(Sections("unload", "load"),
                (section, blocked) => new SectionPassResult(false, true, section.Header == "unload" ? (byte)7 : (byte)3),
                out _);

            Assert.Equal(7, output);
        }

        [Fact]
        public void A_later_section_claims_the_pin_when_no_earlier_one_did()
        {
            byte output = DockPass.Run(Sections("unload", "load"),
                (section, blocked) => new SectionPassResult(false, section.Header == "load", 3),
                out _);

            Assert.Equal(3, output);
        }

        [Fact]
        public void No_output_block_anywhere_means_a_pin_of_zero()
        {
            // The pin reads the current state, so "nothing held" is a real answer, not a reason to
            // leave the last one standing.
            byte output = DockPass.Run(Sections("unload", "load"),
                (section, blocked) => new SectionPassResult(true, false, 0),
                out _);

            Assert.Equal(0, output);
        }

        [Fact]
        public void A_section_with_nothing_at_the_other_end_is_passed_over()
        {
            // A train that has not arrived must not stop the yard section below it.
            var seen = new List<string>();

            DockPass.Run(Sections("unload train", "load yard"),
                (section, blocked) =>
                {
                    if (section.Header == "unload train") return null;

                    seen.Add(section.Header);
                    return new SectionPassResult(true, false, 0);
                },
                out bool actionPerformed);

            Assert.Equal(new[] { "load yard" }, seen);
            Assert.True(actionPerformed);
        }

        [Fact]
        public void An_empty_section_is_not_run_at_all()
        {
            var sections = new List<ConditionSection> { Section("unload yard", blocks: 0) };
            var seen = new List<string>();

            DockPass.Run(sections, (section, blocked) => { seen.Add(section.Header); return null; }, out _);

            Assert.Empty(seen);
        }

        [Fact]
        public void Nothing_at_all_is_a_pin_of_zero_and_no_action()
        {
            Assert.Equal(0, DockPass.Run(null, (s, b) => null, out bool actionPerformed));
            Assert.False(actionPerformed);
        }

        // ---------------------------------------------------------------- the section text

        [Fact]
        public void A_section_keeps_its_own_paragraphs_so_it_can_be_run_on_its_own()
        {
            CompiledConditions compiled = PaperConditionsParser.Parse(
                "unload yard\n" +
                "\n" +
                "game:firewood\n" +
                "target 2\n" +
                "\n" +
                "load yard\n" +
                "\n" +
                "game:plank\n" +
                "height 5\n");

            Assert.Equal("game:firewood\ntarget 2\n", compiled.Sections[0].Text);
            Assert.Equal("game:plank\nheight 5\n", compiled.Sections[1].Text);
        }

        [Fact]
        public void A_section_text_compiles_back_to_the_same_blocks()
        {
            // This is what lets a device hand one section to machinery that knows only whole papers.
            CompiledConditions whole = PaperConditionsParser.Parse(
                "unload yard\n\ngame:firewood\namount 4\n\nload yard\n\ngame:plank\n");

            CompiledConditions justTheSection = PaperConditionsParser.Parse(whole.Sections[0].Text);

            Assert.Single(justTheSection.Blocks);
            Assert.Equal(4, justTheSection.Blocks[0].Directives.Amount);
        }

        [Fact]
        public void A_paper_without_headers_keeps_its_text_too()
        {
            CompiledConditions compiled = PaperConditionsParser.Parse("game:firewood\ntarget 2\n");

            Assert.Equal("game:firewood\ntarget 2\n", Assert.Single(compiled.Sections).Text);
        }

        // ---------------------------------------------------------------- the missing header

        [Fact]
        public void A_device_that_needs_a_header_reports_a_paper_that_has_none()
        {
            IReadOnlyList<PaperConditionError> errors = BlockBehaviorPaperConditions.FindErrors(
                "game:firewood\ntarget 2\n", new Host());

            Assert.Equal("sectionmissing", Assert.Single(errors).Reason);
        }

        [Fact]
        public void It_is_reported_once_however_many_blocks_there_are()
        {
            // The paper has one thing wrong with it, not five.
            IReadOnlyList<PaperConditionError> errors = BlockBehaviorPaperConditions.FindErrors(
                "game:firewood\n\ngame:plank\n\ngame:log-*\n", new Host());

            Assert.Single(errors);
        }

        [Fact]
        public void A_paper_with_headers_is_not_reported()
        {
            IReadOnlyList<PaperConditionError> errors = BlockBehaviorPaperConditions.FindErrors(
                "unload yard\n\ngame:firewood\n", new Host());

            Assert.DoesNotContain(errors, e => e.Reason == "sectionmissing");
        }

        [Fact]
        public void An_empty_paper_is_not_a_mistake()
        {
            IReadOnlyList<PaperConditionError> errors = BlockBehaviorPaperConditions.FindErrors("", new Host());

            Assert.DoesNotContain(errors, e => e.Reason == "sectionmissing");
        }

        // ---------------------------------------------------------------- plumbing

        private static List<ConditionSection> Sections(params string[] headers)
        {
            return headers.Select(h => Section(h, blocks: 1)).ToList();
        }

        private static ConditionSection Section(string header, int blocks)
        {
            Assert.True(ConditionSection.TryParseHeader(header, 1, out ConditionSection section));

            for (int i = 0; i < blocks; i++)
            {
                section.Add(PaperConditionsParser.Parse("game:firewood\n").Blocks[0]);
            }

            return section;
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
