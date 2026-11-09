using SignalsLink.src.signals.managedchute.transporting;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>
    /// The arithmetic of the mash, lifted from BlockEntityFruitPress so a device can do what a
    /// player's clicks do: how much fruit still fits, and what comes out once pressed.
    /// </summary>
    public class FruitPressMashTests
    {
        private const float Apple = 0.3125f;

        [Fact]
        public void Thirty_two_apples_fill_an_empty_press_exactly()
        {
            Assert.Equal(32, FruitPressMash.FitCount(32, Apple, 0, 10));
            Assert.Equal(32, FruitPressMash.FitCount(40, Apple, 0, 10));
        }

        [Fact]
        public void A_nearly_full_press_takes_what_still_rounds_under_the_capacity()
        {
            Assert.Equal(0, FruitPressMash.FitCount(4, Apple, 9.7, 10));
            Assert.Equal(1, FruitPressMash.FitCount(4, Apple, 9.6875, 10));
        }

        [Fact]
        public void Pressed_out_is_decided_at_a_hundredth_of_a_litre()
        {
            Assert.True(FruitPressMash.IsPressedOut(Mash(left: 0.009, transfered: 9.99)));
            Assert.False(FruitPressMash.IsPressedOut(Mash(left: 0.01, transfered: 9.99)));
            Assert.True(FruitPressMash.IsPressedOut(null));
        }

        [Fact]
        public void The_dry_preview_drops_the_litres_and_takes_the_dry_count()
        {
            ItemStack mash = Mash(left: 0, transfered: 10);

            ItemStack dry = FruitPressMash.PreviewDry(mash, new JuiceableProperties(), dryStackSize: 7);

            Assert.Equal(7, dry.StackSize);
            Assert.False(dry.Attributes.HasAttribute(FruitPressMash.LitresLeftAttr));
            Assert.False(dry.Attributes.HasAttribute(FruitPressMash.LitresTransferedAttr));
            Assert.Same(mash.Collectible, dry.Collectible);

            // A preview, not the thing itself.
            Assert.True(mash.Attributes.HasAttribute(FruitPressMash.LitresLeftAttr));
        }

        [Fact]
        public void A_return_stack_is_scaled_by_the_litres_that_came_out()
        {
            ItemStack wax = TestStacks.Item("game:beeswax", 2);
            var props = new JuiceableProperties { ReturnStack = new JsonItemStack { ResolvedItemstack = wax } };

            ItemStack dry = FruitPressMash.PreviewDry(Mash(left: 0, transfered: 3.004), props, dryStackSize: 99);

            Assert.Same(wax.Collectible, dry.Collectible);
            Assert.Equal(6, dry.StackSize);
        }

        private static ItemStack Mash(double left, double transfered)
        {
            ItemStack mash = TestStacks.Item("game:pressedmash-apple");
            mash.Attributes.SetDouble(FruitPressMash.LitresLeftAttr, left);
            mash.Attributes.SetDouble(FruitPressMash.LitresTransferedAttr, transfered);
            return mash;
        }
    }
}
