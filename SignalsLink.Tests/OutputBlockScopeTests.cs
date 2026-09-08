using System.Collections.Generic;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;

namespace SignalsLink.Tests
{
    /// <summary>
    /// What an `output` block on a transfer device asks about. Found in game on a roof damper:
    /// `game:firewood 96 / output 5` never fired over a full pile, while the same block without
    /// the 96 did.
    /// </summary>
    public class OutputBlockScopeTests
    {
        [Fact]
        public void An_amount_condition_holds_in_an_output_block()
        {
            // The bug: an amount condition with no scope prefix is source-scoped, and the
            // source-scoped selection path starts by demanding a stack. An output block has none,
            // so the condition answered "no" whatever was in the pile.
            ConditionBlock block = FirstBlock("game:firewood 96\noutput 5\n");

            Assert.True(block.OutputConditionsHold(Ctx(TestStacks.Item("game:firewood", 96))));
        }

        [Fact]
        public void The_action_rail_predicate_is_what_used_to_answer_here()
        {
            // Pins the actual defect: asked the way the action rail asks - with the selection
            // semantics and no stack - the very same block says no over a full pile.
            ConditionBlock block = FirstBlock("game:firewood 96\noutput 5\n");
            IDictionary<string, object> ctx = Ctx(TestStacks.Item("game:firewood", 96));

            Assert.False(block.ConditionsHold(null, ctx));
            Assert.True(block.OutputConditionsHold(ctx));
        }

        [Fact]
        public void An_amount_condition_still_says_no_when_there_is_not_enough()
        {
            ConditionBlock block = FirstBlock("game:firewood 96\noutput 5\n");

            Assert.False(block.OutputConditionsHold(Ctx(TestStacks.Item("game:firewood", 50))));
        }

        [Fact]
        public void The_amount_is_counted_across_the_whole_column()
        {
            // A three-high pile of firewood reaches the conditions as one synthetic inventory, so
            // the count is of the column and not of the one pile the damper points at.
            ConditionBlock block = FirstBlock("game:firewood 96\noutput 5\n");

            IDictionary<string, object> ctx = Ctx(
                TestStacks.Item("game:firewood", 32),
                TestStacks.Item("game:firewood", 32),
                TestStacks.Item("game:firewood", 32));

            Assert.True(block.OutputConditionsHold(ctx));
        }

        [Fact]
        public void A_bare_code_condition_keeps_working()
        {
            ConditionBlock block = FirstBlock("game:firewood\noutput 5\n");

            Assert.True(block.OutputConditionsHold(Ctx(TestStacks.Item("game:firewood", 1))));
            Assert.False(block.OutputConditionsHold(Ctx(TestStacks.Item("game:charcoal", 96))));
        }

        [Fact]
        public void An_output_block_reads_the_target_even_with_no_prefix()
        {
            // Output blocks are target-scoped: the device reports on its own end. Written without
            // a prefix the line parses as source-scoped, and the output rail overrides that - so
            // an empty source next to a full target still reports.
            ConditionBlock block = FirstBlock("game:firewood 96\noutput 5\n");

            IDictionary<string, object> ctx = Ctx(TestStacks.Item("game:firewood", 96));
            ctx["sourceInventory"] = TestStacks.Inventory(4);   // the far end is empty

            Assert.True(block.OutputConditionsHold(ctx));
        }

        [Fact]
        public void An_explicit_in_source_output_block_reads_the_target_too()
        {
            // `in source` in an output block has nothing to be about, so it is overridden rather
            // than quietly measuring the wrong end. The parser should report it as a mistake in
            // the paper; that part is not built yet.
            ConditionBlock block = FirstBlock("in source\ngame:firewood 96\noutput 5\n");

            IDictionary<string, object> ctx = Ctx(TestStacks.Item("game:firewood", 96));
            ctx["sourceInventory"] = TestStacks.Inventory(4);

            Assert.True(block.OutputConditionsHold(ctx));
        }

        // ---------------------------------------------------------------- plumbing

        private static ConditionBlock FirstBlock(string paper)
        {
            var evaluator = new PaperConditionsEvaluator();
            evaluator.SetConditionsText(paper);
            return evaluator.GetBlocks()[0];
        }

        /// <summary>A target holding the given stacks, the way a ground column reaches conditions.</summary>
        private static IDictionary<string, object> Ctx(params ItemStack[] stacks)
        {
            return new Dictionary<string, object>
            {
                ["targetInventory"] = TestStacks.Inventory(stacks.Length < 1 ? 1 : stacks.Length, stacks)
            };
        }
    }
}
