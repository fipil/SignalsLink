using System.Collections.Generic;
using SignalsLink.src.signals.managedchute.transporting;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;

namespace SignalsLink.Tests
{
    /// <summary>
    /// An `amount N` batch is atomic: it moves N pieces or nothing at all. A block that cannot
    /// gather N therefore does no work, so it must not win the pass — the walk has to carry on to
    /// the block below it.
    ///
    /// Found in game with this paper on a chute feeding a barrel:
    ///
    ///     game:hide-scraped-medium / target 1 ifEmpty / amount 12
    ///     game:hide-scraped-large  / target 1 ifEmpty / amount 8
    ///
    /// and a chest holding 4 medium and 8 large hides. The first block matched the medium, could
    /// never gather its 12, and nothing moved — the second block was never asked. Taking the four
    /// medium hides out of the chest made it work.
    ///
    /// **What these tests do NOT cover:** gathering the batch across several slots of the SAME
    /// material. That path compares candidates with ItemStack.Equals, which walks into game code
    /// that a hand-built item cannot survive, so it is out of reach here and belongs to the
    /// in-game rig tests. It is also the path on which the first attempt at this fix recursed
    /// endlessly and took the server down — which is exactly why the call graph is now split so
    /// that the recursion cannot be written by accident (see CanReachTarget).
    /// </summary>
    public class AtomicAmountTests
    {
        [Fact]
        public void A_block_that_cannot_gather_its_amount_is_not_selectable()
        {
            var chute = Chute(
                "game:hide-scraped-medium\ntarget 1 ifEmpty\namount 12\n",
                Medium(4), Large(8));

            // 12 wanted, 4 in the chest: this block can do nothing, so it must not win the pass.
            Assert.False(chute.FirstBlockAccepts(0));
        }

        [Fact]
        public void A_block_that_can_gather_its_amount_is_selectable()
        {
            var chute = Chute(
                "game:hide-scraped-large\ntarget 1 ifEmpty\namount 8\n",
                Medium(4), Large(8));

            Assert.True(chute.FirstBlockAccepts(1));
        }

        [Fact]
        public void A_block_without_an_amount_is_unaffected()
        {
            var chute = Chute("game:hide-scraped-medium\ntarget 1 ifEmpty\n", Medium(4));

            Assert.True(chute.FirstBlockAccepts(0));
        }

        // ---------------------------------------------------------------- the two marks

        [Fact]
        public void A_ceiling_never_waits()
        {
            // `amount 12-` says twelve at the most, and four is fewer than twelve. This is the one
            // form that does not hold out for the whole number.
            var chute = Chute("game:hide-scraped-medium\ntarget 1 ifEmpty\namount 12-\n", Medium(4));

            Assert.True(chute.FirstBlockAccepts(0));
        }

        [Fact]
        public void A_floor_waits_exactly_as_a_plain_amount_does()
        {
            // `amount 12+` is still a number that has to be reached; it only changes what happens
            // once it is.
            var chute = Chute("game:hide-scraped-medium\ntarget 1 ifEmpty\namount 12+\n", Medium(4));

            Assert.False(chute.FirstBlockAccepts(0));
        }

        [Fact]
        public void A_floor_that_is_met_is_selectable()
        {
            var chute = Chute("game:hide-scraped-large\ntarget 1 ifEmpty\namount 8+\n", Medium(4), Large(8));

            Assert.True(chute.FirstBlockAccepts(1));
        }

        // ---------------------------------------------------------------- plumbing

        private static ItemStack Medium(int size) => TestStacks.Item("game:hide-scraped-medium", size);
        private static ItemStack Large(int size) => TestStacks.Item("game:hide-scraped-large", size);

        private static TestChute Chute(string paper, params ItemStack[] chest)
        {
            var evaluator = new PaperConditionsEvaluator();
            evaluator.SetConditionsText(paper);

            return new TestChute(TestStacks.Inventory(4, chest), TestStacks.Inventory(4), evaluator);
        }

        /// <summary>
        /// The transfer with its protected selection check opened up. There is no world here: the
        /// check reads only the two inventories, which is what makes it testable at all.
        /// </summary>
        private sealed class TestChute : InventoryToInventoryTransfer
        {
            private readonly IInventory source;
            private readonly PaperConditionsEvaluator evaluator;

            public TestChute(IInventory source, IInventory target, PaperConditionsEvaluator evaluator)
                : base(null, source, target, null, 0, 0, evaluator)
            {
                this.source = source;
                this.evaluator = evaluator;
            }

            /// <summary>Would the paper's first block accept what is in this slot?</summary>
            public bool FirstBlockAccepts(int slotIndex)
            {
                IReadOnlyList<ConditionBlock> blocks = evaluator.GetBlocks();
                return CanTransferSelection(source[slotIndex], blocks[0].Directives);
            }
        }
    }
}
