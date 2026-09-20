using SignalsLink.src.signals.vehicle;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>
    /// A minecart carries its load in a container hung on it, and the game keeps a long-lived
    /// workspace for that container. The workspace's inventory is what the player opens and edits;
    /// it is filled from the container item once and never re-reads it.
    ///
    /// Found in game, twice over. First the goods were written into the cart's own copy and nothing
    /// told the cart to save, so the chest looked empty and the cargo would not have survived a
    /// reload. Then they were written into the container ITEM - saved, and still not what the
    /// player was looking at, because the workspace was still showing the slots it was filled with.
    /// Goods appeared in the chest when loaded and stayed there after being unloaded somewhere else.
    ///
    /// So the rule is: go through the workspace. Which leaves this one decision of our own.
    /// </summary>
    public class CartHoldTests
    {
        [Fact]
        public void A_workspace_that_has_never_been_filled_must_be_filled()
        {
            Assert.True(HungContainers.NeedsReload(null, null, TestStacks.Item("game:chest", 1)));
        }

        [Fact]
        public void A_workspace_filled_from_the_container_that_hangs_there_is_left_alone()
        {
            // Filling it again would throw away the slots the player may have open, and every
            // change - the dock's and the player's - comes back through those slots anyway.
            ItemStack chest = TestStacks.Item("game:chest", 1);

            Assert.False(HungContainers.NeedsReload(new InventoryGeneric(4, "signalslink-fakewrapper", null), chest, chest));
        }

        [Fact]
        public void A_different_container_on_the_same_hook_must_be_filled_again()
        {
            // Same hook, same kind of chest, so the game hands back the same cached workspace -
            // which would otherwise pour the old chest's contents into the new one.
            Assert.True(HungContainers.NeedsReload(
                new InventoryGeneric(4, "signalslink-fakewrapper", null),
                TestStacks.Item("game:chest", 1),
                TestStacks.Item("game:chest", 1)));
        }

        [Theory]
        [InlineData("chest")]
        [InlineData("crate")]
        public void A_container_is_recognised_through_the_subclass_it_really_carries(string kind)
        {
            // Found in game the hard way: asked for CollectibleBehaviorHeldBag by exact type, this
            // found nothing and every cart in the world went empty. Nothing in the game carries
            // that class itself - a chest carries BoatableGenericTypedContainer, a crate carries
            // BoatableCrate, and both are subclasses.
            Item item = new Item();
            item.CollectibleBehaviors = new CollectibleBehavior[]
            {
                kind == "chest"
                    ? new CollectibleBehaviorBoatableGenericTypedContainer(item)
                    : new CollectibleBehaviorBoatableCrate(item)
            };

            Assert.NotNull(HungContainers.BagOn(item));
        }

        [Fact]
        public void And_a_lantern_is_not_a_container()
        {
            Assert.Null(HungContainers.BagOn(new Item()));
            Assert.Null(HungContainers.BagOn(null));
        }

        [Fact]
        public void Nothing_without_an_entity()
        {
            Assert.False(HungContainers.IsCarrier(null));
            Assert.Empty(HungContainers.HoldsOf(null));
        }
    }
}
