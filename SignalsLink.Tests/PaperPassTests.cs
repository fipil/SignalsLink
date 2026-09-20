using System.Collections.Generic;
using SignalsLink.src.signals.managedchute.transporting;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;

namespace SignalsLink.Tests
{
    /// <summary>
    /// Level-B tests: real papers, parsed and evaluated the way a device does it, against
    /// hand-built inventories. These lock in the rules that used to fail in the game — above all
    /// the Output pin that stayed at 15 after a charcoal pit finished burning.
    /// </summary>
    public class PaperPassTests
    {
        // The paper the stuck pin was found with: report 15 while the target holds enough
        // firewood, 0 once it does not.
        private const string WatchFirewood =
            "in target\n" +
            "game:firewood 96\n" +
            "output 15\n" +
            "\n" +
            "in target\n" +
            "!game:firewood 96\n" +
            "output 0\n";

        [Fact]
        public void The_pin_reports_the_value_of_the_block_that_holds()
        {
            IInventory target = TestStacks.Inventory(4, TestStacks.Item("game:firewood", 96));

            Assert.True(RunSensorPass(WatchFirewood, target, out byte output));
            Assert.Equal(15, output);
        }

        [Fact]
        public void The_pin_falls_once_the_firewood_turned_into_charcoal()
        {
            // The direction that was broken: the wood is gone, so the first block stops holding
            // and the second one takes over.
            IInventory target = TestStacks.Inventory(4, TestStacks.Item("game:charcoal", 96));

            Assert.True(RunSensorPass(WatchFirewood, target, out byte output));
            Assert.Equal(0, output);
        }

        [Fact]
        public void An_empty_target_reads_zero_rather_than_nothing()
        {
            // An empty inventory has no slots to iterate, which is how a negated condition used to
            // quietly answer "no" and leave the pin on its old value.
            IInventory target = TestStacks.Inventory(4);

            Assert.True(RunSensorPass(WatchFirewood, target, out byte output));
            Assert.Equal(0, output);
        }

        [Fact]
        public void A_paper_where_nothing_holds_leaves_the_pin_at_zero()
        {
            IInventory target = TestStacks.Inventory(4, TestStacks.Item("game:charcoal", 96));

            // Only one block, and it does not hold.
            Assert.False(RunSensorPass("in target\ngame:firewood 96\noutput 15\n", target, out byte output));
            Assert.Equal(0, output);
        }

        [Fact]
        public void A_paper_with_no_output_block_at_all_reports_nothing()
        {
            IInventory target = TestStacks.Inventory(4, TestStacks.Item("game:firewood", 96));

            // Every block counts as an output block for a sensor, so this one reports its
            // effective default rather than nothing - see everyBlockIsOutput.
            Assert.True(RunSensorPass("in target\ngame:firewood 96\n", target, out byte output));
            Assert.Equal(15, output);
        }

        // ---------------------------------------------------------------- the action rail

        [Fact]
        public void A_target_ifEmpty_chain_falls_through_to_the_next_block()
        {
            // Slot 2 is taken, so the first block is no longer valid and the second one decides
            // where the stack goes. This chain is what fills a chest slot by slot.
            var evaluator = Evaluator(
                "game:firewood\n" +
                "target 2 ifEmpty\n" +
                "\n" +
                "game:firewood\n" +
                "target 3\n");

            IInventory target = TestStacks.Inventory(4, null, TestStacks.Item("game:plank", 1));
            IDictionary<string, object> ctx = new Dictionary<string, object>
            {
                ["targetInventory"] = target
            };

            Assert.True(ConditionResolution.ResolveActionDirectives(
                evaluator, TestStacks.Item("game:firewood", 4), ctx, out PaperConditionDirectives directives));

            Assert.Equal((byte)3, directives.TargetSlot);
        }

        [Fact]
        public void The_first_block_wins_while_its_target_slot_is_still_empty()
        {
            var evaluator = Evaluator(
                "game:firewood\n" +
                "target 2 ifEmpty\n" +
                "\n" +
                "game:firewood\n" +
                "target 3\n");

            IInventory target = TestStacks.Inventory(4);
            IDictionary<string, object> ctx = new Dictionary<string, object>
            {
                ["targetInventory"] = target
            };

            Assert.True(ConditionResolution.ResolveActionDirectives(
                evaluator, TestStacks.Item("game:firewood", 4), ctx, out PaperConditionDirectives directives));

            Assert.Equal((byte)2, directives.TargetSlot);
        }

        [Fact]
        public void An_output_block_is_never_offered_as_an_action()
        {
            // The output rail belongs to the driver; a transfer asking "how do I move this stack?"
            // must not be handed an output block, which moves nothing.
            var evaluator = Evaluator(
                "in target\n" +
                "game:firewood 1+\n" +
                "output 15\n");

            IInventory target = TestStacks.Inventory(4, TestStacks.Item("game:firewood", 96));
            IDictionary<string, object> ctx = new Dictionary<string, object>
            {
                ["targetInventory"] = target
            };

            Assert.False(ConditionResolution.ResolveActionDirectives(
                evaluator, TestStacks.Item("game:firewood", 4), ctx, out _));
        }

        // ---------------------------------------------------------------- plumbing

        private static PaperConditionsEvaluator Evaluator(string paper)
        {
            var evaluator = new PaperConditionsEvaluator();
            evaluator.SetConditionsText(paper);
            return evaluator;
        }

        private static bool RunSensorPass(string paper, IInventory target, out byte output)
        {
            IDictionary<string, object> ctx = new Dictionary<string, object>
            {
                ["targetInventory"] = target
            };

            return Evaluator(paper).RunSensorPass(null, ctx, out output);
        }
    }
}
