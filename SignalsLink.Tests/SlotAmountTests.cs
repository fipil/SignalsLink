using System.Collections.Generic;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;

namespace SignalsLink.Tests
{
    /// <summary>
    /// `game:firewood 96+ slot 5` — a question about one named slot rather than about the whole
    /// inventory. It is a gate: it says WHEN a block may act, never WHAT it carries.
    /// </summary>
    public class SlotAmountTests
    {
        [Fact]
        public void An_amount_can_be_asked_of_one_slot()
        {
            IInventory target = TestStacks.Inventory(
                4,
                TestStacks.Item("game:firewood", 4),
                TestStacks.Item("game:firewood", 96));

            Assert.True(Holds("in target\ngame:firewood 96+ slot 2\n", target));
            Assert.False(Holds("in target\ngame:firewood 96+ slot 1\n", target));
        }

        [Fact]
        public void The_slot_is_counted_from_one()
        {
            IInventory target = TestStacks.Inventory(2, TestStacks.Item("game:firewood", 10));

            // Slot 1 is the first one, the same as `target 1` and `source 1` mean.
            Assert.True(Holds("in target\ngame:firewood 10 slot 1\n", target));
        }

        [Fact]
        public void A_slot_beyond_the_inventory_reads_as_empty()
        {
            IInventory target = TestStacks.Inventory(2, TestStacks.Item("game:firewood", 10));

            Assert.False(Holds("in target\ngame:firewood 1+ slot 9\n", target));
            Assert.True(Holds("in target\ngame:firewood 0 slot 9\n", target));
        }

        [Fact]
        public void The_other_slots_do_not_count_towards_it()
        {
            // Two slots of 50 each: the whole inventory holds 100, but neither slot holds 96.
            IInventory target = TestStacks.Inventory(
                4,
                TestStacks.Item("game:firewood", 50),
                TestStacks.Item("game:firewood", 50));

            Assert.True(Holds("in target\ngame:firewood 96+\n", target));
            Assert.False(Holds("in target\ngame:firewood 96+ slot 1\n", target));
        }

        [Fact]
        public void A_negated_slot_condition_still_reads_that_slot()
        {
            IInventory target = TestStacks.Inventory(2, TestStacks.Item("game:firewood", 10));

            Assert.True(Holds("in target\n!game:firewood 96+ slot 1\n", target));
            Assert.False(Holds("in target\n!game:firewood 10 slot 1\n", target));
        }

        // ---------------------------------------------------------------- gate, not selector

        [Fact]
        public void A_block_gated_only_on_a_slot_cannot_choose_what_to_carry()
        {
            // Nothing here says what to move, so the block must not be selectable for a transfer.
            ConditionBlock block = FirstBlock("game:firewood 23+ slot 5\ntarget 1\n");

            Assert.False(block.CanSelectSource);
        }

        [Fact]
        public void A_negated_gate_is_still_a_gate()
        {
            ConditionBlock block = FirstBlock("!game:firewood 23+ slot 5\ntarget 1\n");

            Assert.False(block.CanSelectSource);
        }

        [Fact]
        public void A_plain_amount_still_chooses_what_to_carry()
        {
            ConditionBlock block = FirstBlock("game:firewood 23+\ntarget 1\n");

            Assert.True(block.CanSelectSource);
        }

        [Fact]
        public void A_gate_beside_a_selector_leaves_the_block_selectable()
        {
            // "Take firewood, but only while slot 5 holds at least ten planks."
            ConditionBlock block = FirstBlock("game:firewood 23+\ngame:plank 10+ slot 5\ntarget 1\n");

            Assert.True(block.CanSelectSource);
        }

        // ---------------------------------------------------------------- mistakes

        [Fact]
        public void Slot_zero_is_reported()
        {
            var errors = new List<PaperConditionError>();
            PaperConditionsParser.Parse("in target\ngame:firewood 10+ slot 0\n", errors);

            PaperConditionError error = Assert.Single(errors);
            Assert.Equal(2, error.Line);
            Assert.Equal("slot", error.Reason);
        }

        // ---------------------------------------------------------------- plumbing

        private static ConditionBlock FirstBlock(string paper)
        {
            var evaluator = new PaperConditionsEvaluator();
            evaluator.SetConditionsText(paper);
            return evaluator.GetBlocks()[0];
        }

        private static bool Holds(string paper, IInventory target)
        {
            IDictionary<string, object> ctx = new Dictionary<string, object>
            {
                ["targetInventory"] = target
            };

            return FirstBlock(paper).OutputConditionsHold(ctx);
        }
    }
}
