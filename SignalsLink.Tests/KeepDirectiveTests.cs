using System.Collections.Generic;
using SignalsLink.src.signals.managedchute.transporting;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>
    /// `keep N` — the level to hold in the target.
    ///
    /// Found in game: `in target game:beeswax 17-` is a GATE, and the dock's default amount is
    /// everything that matches, so the first transfer under an open gate moved a whole stack. The
    /// gate says when, the level says how much.
    /// </summary>
    public class KeepDirectiveTests
    {
        // ---------------------------------------------------------------- reading it

        [Fact]
        public void Keep_is_read_off_the_paper()
        {
            Assert.Equal(17m, Directives("game:beeswax\nkeep 17\n").Keep);
        }

        [Fact]
        public void A_paper_without_it_keeps_nothing()
        {
            Assert.Null(Directives("game:beeswax\n").Keep);
            Assert.False(Directives("game:beeswax\n").HasKeep);
        }

        [Fact]
        public void Keep_and_amount_are_different_things()
        {
            // One is the level in the target, the other the size of a batch. Writing both is
            // sensible: five at a time until there are seventeen.
            PaperConditionDirectives directives = Directives("game:beeswax\nkeep 17\namount 5\n");

            Assert.Equal(17m, directives.Keep);
            Assert.Equal(5m, directives.Amount);
        }

        [Theory]
        [InlineData("keep")]
        [InlineData("keep ")]
        [InlineData("keep x")]
        [InlineData("keep 5 7")]
        [InlineData("keep 5-")]
        public void A_level_that_is_not_a_number_is_a_paper_error(string line)
        {
            // `keep 5-` too: a level has no "at most" about it, and silently ignoring the mark
            // would be worse than saying so.
            Assert.Contains(Errors("game:beeswax\n" + line + "\n"), e => e.Reason == "keep");
        }

        // ---------------------------------------------------------------- counting it

        [Fact]
        public void What_is_already_there_is_counted_with_the_conditions_own_counter()
        {
            // The number `keep` works from must be the number `in target … N-` sees, or one block
            // carrying both lines would argue with itself.
            ConditionBlock block = Block("game:beeswax\nkeep 17\n");

            IInventory target = TestStacks.Inventory(4,
                TestStacks.Item("game:beeswax", 6),
                TestStacks.Item("game:firewood", 40),
                TestStacks.Item("game:beeswax", 4));

            Assert.Equal(10m, block.CountInTarget(target, TestStacks.Ctx()));
        }

        [Fact]
        public void A_glob_counts_the_whole_family_together()
        {
            // His call, and it matches what the condition already does: `game:ingot-*` with
            // `keep 17` is seventeen ingots, of any kind.
            ConditionBlock block = Block("game:ingot-*\nkeep 17\n");

            IInventory target = TestStacks.Inventory(4,
                TestStacks.Item("game:ingot-iron", 5),
                TestStacks.Item("game:ingot-copper", 4),
                TestStacks.Item("game:plank-oak", 60));

            Assert.Equal(9m, block.CountInTarget(target, TestStacks.Ctx()));
        }

        [Fact]
        public void Nothing_of_ours_in_the_target_counts_as_none()
        {
            ConditionBlock block = Block("game:beeswax\nkeep 17\n");

            Assert.Equal(0m, block.CountInTarget(TestStacks.Inventory(4), TestStacks.Ctx()));
            Assert.Equal(0m, block.CountInTarget(null, TestStacks.Ctx()));
        }

        // ---------------------------------------------------------------- capping by it

        [Fact]
        public void The_batch_is_held_down_to_what_is_missing()
        {
            Assert.Equal(7, Capped(100, room: 7));
            Assert.Equal(3, Capped(3, room: 7));
        }

        [Fact]
        public void A_target_at_its_level_moves_nothing()
        {
            Assert.Equal(0, Capped(100, room: 0));
            Assert.Equal(0, Capped(100, room: -4));
        }

        [Fact]
        public void Without_keep_the_batch_is_left_alone()
        {
            Assert.Equal(100, Capped(100, room: null));
        }

        // ---------------------------------------------------------------- plumbing

        private static int Capped(int quantity, decimal? room)
        {
            return TestCap.Apply(quantity, new TransferSelection(null, PaperConditionDirectives.Empty, room));
        }

        private static PaperConditionDirectives Directives(string paper)
        {
            return Block(paper).Directives;
        }

        private static ConditionBlock Block(string paper)
        {
            var evaluator = new PaperConditionsEvaluator();
            evaluator.SetConditionsText(paper);

            return evaluator.GetBlocks()[0];
        }

        private static IReadOnlyList<PaperConditionError> Errors(string paper)
        {
            var errors = new List<PaperConditionError>();
            PaperConditionsParser.Parse(paper, errors);

            return errors;
        }

        /// <summary>Opens the protected cap so the arithmetic can be checked on its own.</summary>
        private sealed class TestCap : InventoryToInventoryTransfer
        {
            private TestCap() : base(null, null, null, null, 0, 0, null)
            {
            }

            public static int Apply(int quantity, TransferSelection selection)
            {
                return CappedByKeep(quantity, selection);
            }
        }
    }
}
