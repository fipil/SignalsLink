using System.Collections.Generic;
using System.Linq;
using SignalsLink.src.signals.paperConditions;

namespace SignalsLink.Tests
{
    /// <summary>
    /// The `when` gate: a section runs only while the Input pin holds one of its values, and on
    /// such a paper the pin stops meaning credit. Several recipes on one paper, one per section,
    /// picked by a counter on the pin.
    ///
    /// Defended hardest: a paper without `when` compiles and evaluates exactly as before, whatever
    /// the gate input is.
    /// </summary>
    public class WhenGateTests
    {
        private const string Two =
            "when 1\n" +                      // 1
            "\n" +
            "game:firewood\n" +               // 3
            "target 2\n" +
            "output 4\n" +
            "\n" +
            "when 2\n" +                      // 7
            "\n" +
            "game:plank\n" +                  // 9
            "target 3\n" +
            "\n" +
            "game:stick\n" +                  // 12
            "target 4\n";

        // ------------------------------------------------------------------ parsing

        [Fact]
        public void When_alone_is_a_header_with_both_ends_the_device()
        {
            ConditionSection section = Header("when 3");

            Assert.False(section.IsImplicit);
            Assert.Null(section.Direction);
            Assert.True(section.SourceIsDevice);
            Assert.True(section.TargetIsDevice);
            Assert.True(section.IsGated);
            Assert.Null(section.WhenError);
            Assert.True(section.ActiveFor(3));
            Assert.False(section.ActiveFor(2));
            Assert.False(section.ActiveFor(0));
        }

        [Fact]
        public void A_gate_rides_on_every_header_form()
        {
            ConditionSection unload = Header("unload north when 3");
            Assert.Equal(ConditionSection.Unload, unload.Direction);
            Assert.Equal(new[] { "north" }, unload.SourceTokens);
            Assert.True(unload.ActiveFor(3));

            ConditionSection load = Header("load yard 2 when 4");
            Assert.Equal(new[] { "yard", "2" }, load.TargetTokens);
            Assert.True(load.ActiveFor(4));

            ConditionSection fromTo = Header("from train to yard when 5");
            Assert.Equal(new[] { "train" }, fromTo.SourceTokens);
            Assert.Equal(new[] { "yard" }, fromTo.TargetTokens);
            Assert.True(fromTo.EndsAreComplete);
            Assert.True(fromTo.ActiveFor(5));
        }

        [Fact]
        public void Values_may_be_listed_and_ranged()
        {
            ConditionSection section = Header("WHEN 1 2 4-6 15");

            Assert.Equal(new[] { 1, 2, 4, 5, 6, 15 }, Enumerable.Range(0, 16).Where(v => section.ActiveFor((byte)v)));
            Assert.Null(section.WhenError);
        }

        [Theory]
        [InlineData("when", "")]
        [InlineData("when 0", "0")]
        [InlineData("when 16", "16")]
        [InlineData("when x", "x")]
        [InlineData("when 5-3", "5-3")]
        [InlineData("unload north when 1-2-3", "1-2-3")]
        public void A_gate_that_cannot_be_read_is_reported_on_the_header_line(string header, string token)
        {
            var errors = new List<PaperConditionError>();
            PaperConditionsParser.Parse(header + "\n\ngame:firewood\ntarget 2\n", errors);

            PaperConditionError error = Assert.Single(errors, e => e.Reason == "sectionwhenvalue");
            Assert.Equal(1, error.Line);
            Assert.Equal(token, Header(header).WhenError);
        }

        [Fact]
        public void A_good_value_beside_a_bad_one_still_counts()
        {
            ConditionSection section = Header("when 3 x");

            Assert.Equal("x", section.WhenError);
            Assert.True(section.ActiveFor(3));
        }

        [Fact]
        public void A_condition_line_with_when_in_it_is_not_a_header()
        {
            Assert.False(ConditionSection.TryParseHeader("game:firewood when 3", 1, out _));
            Assert.False(ConditionSection.IsHeaderLine("target 3 when 2"));
        }

        [Fact]
        public void Gated_and_ungated_sections_on_one_paper_are_a_mistake()
        {
            var errors = new List<PaperConditionError>();
            PaperConditionsParser.Parse("unload north when 1\n\ngame:firewood\ntarget 2\n\nload south\n\ngame:plank\ntarget 3\n", errors);

            PaperConditionError error = Assert.Single(errors, e => e.Reason == "sectionwhenmixed");
            Assert.Equal(6, error.Line);
            Assert.Equal("load south", error.Text);
        }

        [Fact]
        public void Two_headers_with_the_same_gate_are_one_section()
        {
            CompiledConditions compiled = PaperConditionsParser.Parse("when 2\n\ngame:plank\ntarget 3\n\nwhen 2\n\ngame:stick\ntarget 4\n");

            ConditionSection section = Assert.Single(compiled.Sections);
            Assert.Equal(2, section.Blocks.Count);
        }

        [Fact]
        public void A_paper_without_when_is_not_gated()
        {
            Assert.False(PaperConditionsParser.Parse("game:firewood\ntarget 2\n").IsGated);
            Assert.False(PaperConditionsParser.Parse("unload north\n\ngame:firewood\ntarget 2\n").IsGated);
            Assert.False(Header("unload north").IsGated);
        }

        // ------------------------------------------------------------------ the view

        [Fact]
        public void The_view_for_an_input_holds_the_open_sections_blocks_in_paper_order()
        {
            CompiledConditions compiled = PaperConditionsParser.Parse(Two);
            Assert.True(compiled.IsGated);
            Assert.Equal(3, compiled.Blocks.Count);

            CompiledConditions one = compiled.ActiveFor(1);
            Assert.Single(one.Sections);
            Assert.Equal(new[] { 3 }, one.Blocks.Select(b => b.FirstLine));
            Assert.True(one.HasAnyOutput);

            CompiledConditions two = compiled.ActiveFor(2);
            Assert.Equal(new[] { 9, 12 }, two.Blocks.Select(b => b.FirstLine));
            Assert.False(two.HasAnyOutput);

            CompiledConditions closed = compiled.ActiveFor(0);
            Assert.Empty(closed.Sections);
            Assert.Empty(closed.Blocks);
            Assert.Empty(compiled.ActiveFor(7).Blocks);
        }

        [Fact]
        public void An_ungated_paper_is_its_own_view_for_any_input()
        {
            CompiledConditions compiled = PaperConditionsParser.Parse("game:firewood\ntarget 2\n");

            Assert.Same(compiled, compiled.ActiveFor(0));
            Assert.Same(compiled, compiled.ActiveFor(9));
        }

        // ------------------------------------------------------------------ the evaluator

        [Fact]
        public void The_evaluator_follows_the_gate_input()
        {
            var evaluator = new PaperConditionsEvaluator();
            evaluator.SetConditionsText(Two);

            Assert.True(evaluator.IsGated);
            Assert.False(evaluator.GateOpen);
            Assert.Empty(evaluator.GetBlocks());
            Assert.Empty(evaluator.GetSections());
            Assert.False(evaluator.HasAnyOutput);

            evaluator.GateInput = 1;
            Assert.True(evaluator.GateOpen);
            Assert.Equal(new[] { 3 }, evaluator.GetBlocks().Select(b => b.FirstLine));
            Assert.True(evaluator.HasAnyOutput);

            evaluator.GateInput = 2;
            Assert.Equal(new[] { 9, 12 }, evaluator.GetBlocks().Select(b => b.FirstLine));
            Assert.False(evaluator.HasAnyOutput);

            evaluator.GateInput = 15;
            Assert.False(evaluator.GateOpen);
            Assert.Empty(evaluator.GetBlocks());
        }

        [Fact]
        public void The_gate_input_means_nothing_to_an_ungated_paper()
        {
            var evaluator = new PaperConditionsEvaluator();
            evaluator.SetConditionsText("game:firewood\ntarget 2\noutput 5\n\ngame:plank\ntarget 3\n");

            foreach (byte input in new byte[] { 0, 1, 7, 15 })
            {
                evaluator.GateInput = input;
                Assert.False(evaluator.IsGated);
                Assert.False(evaluator.GateOpen);
                Assert.Equal(2, evaluator.GetBlocks().Count);
                Assert.True(evaluator.HasAnyOutput);
                Assert.Single(evaluator.GetSections());
            }
        }

        [Fact]
        public void A_new_paper_text_forgets_the_old_views_but_keeps_the_input()
        {
            var evaluator = new PaperConditionsEvaluator { GateInput = 2 };
            evaluator.SetConditionsText(Two);
            Assert.Equal(2, evaluator.GetBlocks().Count);

            evaluator.SetConditionsText("when 2\n\ngame:stick\ntarget 4\n");
            Assert.Equal(new[] { 3 }, evaluator.GetBlocks().Select(b => b.FirstLine));
            Assert.Equal(2, evaluator.GateInput);
        }

        // ------------------------------------------------------------------ what the device says about it

        [Fact]
        public void A_one_way_device_with_an_input_pin_takes_a_bare_gate_but_no_ends()
        {
            var chute = new Host { SupportsGates = true };

            Assert.DoesNotContain(Errors("when 3\n\ngame:firewood\ntarget 2\n", chute), e => e.Reason.StartsWith("section"));
            Assert.Contains(Errors("unload north when 3\n\ngame:firewood\ntarget 2\n", chute), e => e.Reason == "sectionunsupported");
        }

        [Fact]
        public void A_device_without_an_input_pin_cannot_read_a_gate()
        {
            var sensor = new Host { SupportsGates = false };

            PaperConditionError error = Assert.Single(Errors("when 3\n\ngame:firewood\n", sensor), e => e.Reason == "sectionwhenunsupported");
            Assert.Equal(1, error.Line);
            Assert.Equal("when 3", error.Text);
        }

        [Fact]
        public void A_two_ended_device_takes_gated_headers_but_still_wants_its_ends()
        {
            var dock = new Host { SupportsGates = true, SupportsSections = true, RequiresSections = true };

            Assert.DoesNotContain(Errors("unload north when 3\n\ngame:firewood\n\nload south when 4\n\ngame:plank\n", dock), e => e.Reason.StartsWith("section"));
            Assert.Contains(Errors("when 3\n\ngame:firewood\n", dock), e => e.Reason == "sectionends");
        }

        [Fact]
        public void Nothing_changes_for_papers_without_a_gate()
        {
            var chute = new Host { SupportsGates = true };
            var dock = new Host { SupportsGates = true, SupportsSections = true, RequiresSections = true };

            Assert.DoesNotContain(Errors("game:firewood\ntarget 2\n", chute), e => e.Reason.StartsWith("section"));
            Assert.Contains(Errors("unload north\n\ngame:firewood\n", chute), e => e.Reason == "sectionunsupported");
            Assert.Contains(Errors("game:firewood\n", dock), e => e.Reason == "sectionmissing");
        }

        // ------------------------------------------------------------------ helpers

        private static ConditionSection Header(string line)
        {
            Assert.True(ConditionSection.TryParseHeader(line, 1, out ConditionSection section), line);
            return section;
        }

        private static IReadOnlyList<PaperConditionError> Errors(string paper, IPaperConditionsHost host)
            => BlockBehaviorPaperConditions.FindErrors(paper, host);

        private sealed class Host : IPaperConditionsHost
        {
            public string ConditionsText { get; set; }
            public int SignalInputsCount => 1;
            public bool RequiresTransferSelector => false;
            public bool SupportsSections { get; init; }
            public bool RequiresSections { get; init; }
            public bool SupportsGates { get; init; }
        }
    }
}
