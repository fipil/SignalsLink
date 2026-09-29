using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;

namespace SignalsLink.Tests
{
    /// <summary>
    /// The level-B probe from signalslink-testing.md: can game types be built and questioned
    /// outside a running game? Game types can behave unpredictably without the client/server that
    /// normally initialises them, so this is settled by one small experiment before a whole suite
    /// is written on top of it.
    ///
    /// If this file ever goes red, condition-level tests are off the table and only the driver
    /// (level A, no ItemStack and no IInventory) stays.
    /// </summary>
    public class LevelBProbeTests
    {
        [Fact]
        public void An_item_stack_can_be_built_from_a_bare_item()
        {
            ItemStack stack = TestStacks.Item("game:firewood", 12);

            Assert.NotNull(stack.Collectible);
            Assert.Equal("game:firewood", stack.Collectible.Code.ToString());
            Assert.Equal(12, stack.StackSize);
        }

        [Fact]
        public void A_code_glob_condition_answers_about_a_hand_built_stack()
        {
            ICondition condition = new CodeGlobCondition("game:fire*");

            Assert.True(condition.Evaluate(TestStacks.Item("game:firewood"), TestStacks.Ctx()));
            Assert.False(condition.Evaluate(TestStacks.Item("game:plank"), TestStacks.Ctx()));
        }

        [Fact]
        public void An_inventory_can_be_built_without_an_api()
        {
            InventoryGeneric inventory = TestStacks.Inventory(4, TestStacks.Item("game:firewood", 8));

            Assert.Equal(4, inventory.Count);
            Assert.False(inventory[0].Empty);
            Assert.True(inventory[1].Empty);
        }

        [Fact]
        public void An_amount_condition_counts_across_slots()
        {
            // The interesting part is that this reaches BlockLiquidContainerBase.GetContainableProps,
            // a static from VSSurvivalMod - the kind of call that could need a running game.
            IInventory inventory = TestStacks.Inventory(
                4,
                TestStacks.Item("game:firewood", 8),
                TestStacks.Item("game:plank", 3),
                TestStacks.Item("game:firewood", 5));

            IInventoryCondition atLeast12 = new InventoryAmountCondition(
                new CodeGlobCondition("game:firewood"), 12, InventoryAmountComparison.AtLeast);

            IInventoryCondition atLeast14 = new InventoryAmountCondition(
                new CodeGlobCondition("game:firewood"), 14, InventoryAmountComparison.AtLeast);

            Assert.True(atLeast12.Evaluate(null, inventory, TestStacks.Ctx(), InventoryConditionScope.Target, false));
            Assert.False(atLeast14.Evaluate(null, inventory, TestStacks.Ctx(), InventoryConditionScope.Target, false));
        }

        [Fact]
        public void The_parser_compiles_a_whole_paper()
        {
            CompiledConditions compiled = PaperConditionsParser.Parse("game:firewood\ntarget 2\namount 8\n\nin target\ngame:firewood 96\noutput 15\n");

            Assert.True(compiled.HasAnyOutput);
        }
    }
}
