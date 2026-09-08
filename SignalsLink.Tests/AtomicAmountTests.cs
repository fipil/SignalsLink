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
    /// </summary>
    public class AtomicAmountTests
    {
        [Fact]
        public void A_block_that_cannot_gather_its_amount_is_not_selectable()
        {
            IInventory source = TestStacks.Inventory(
                4,
                TestStacks.Item("game:hide-scraped-medium", 4),
                TestStacks.Item("game:hide-scraped-large", 8));

            var transfer = new TestTransfer(source, TestStacks.Inventory(4));

            // 12 wanted, 4 in the chest: this block can do nothing.
            Assert.False(transfer.CanTransfer(source[0], Directives(targetSlot: 1, amount: 12)));
        }

        [Fact]
        public void A_block_that_can_gather_its_amount_is_selectable()
        {
            IInventory source = TestStacks.Inventory(
                4,
                TestStacks.Item("game:hide-scraped-medium", 4),
                TestStacks.Item("game:hide-scraped-large", 8));

            var transfer = new TestTransfer(source, TestStacks.Inventory(4));

            Assert.True(transfer.CanTransfer(source[1], Directives(targetSlot: 1, amount: 8)));
        }

        // NOTE: gathering the batch across SEVERAL slots of the same material cannot be tested
        // here. That path compares the candidates with ItemStack.Equals(world, ...), which
        // resolves the stacks through the item registry, and a hand-built Item is not in it. It
        // belongs to the in-game rig tests.

        [Fact]
        public void A_block_without_an_amount_is_unaffected()
        {
            IInventory source = TestStacks.Inventory(4, TestStacks.Item("game:hide-scraped-medium", 4));

            var transfer = new TestTransfer(source, TestStacks.Inventory(4));

            Assert.True(transfer.CanTransfer(source[0], Directives(targetSlot: 1, amount: null)));
        }

        // ---------------------------------------------------------------- plumbing

        private static PaperConditionDirectives Directives(byte targetSlot, decimal? amount)
        {
            return new PaperConditionDirectives(null, targetSlot, false, amount, true);
        }

        /// <summary>
        /// The transfer, with the protected selection check opened up. There is no world here: the
        /// check only looks at the two inventories, which is what makes it testable at all.
        /// </summary>
        private sealed class TestTransfer : InventoryToInventoryTransfer
        {
            public TestTransfer(IInventory source, IInventory target)
                : base(null, source, target, null, 0, 0, new PaperConditionsEvaluator())
            {
            }

            public bool CanTransfer(ItemSlot slot, PaperConditionDirectives directives)
            {
                return CanTransferSelection(slot, directives);
            }
        }
    }
}
