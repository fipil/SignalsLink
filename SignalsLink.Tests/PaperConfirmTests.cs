using SignalsLink.src.signals.paperConditions;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>
    /// When overwriting a device's orders is worth asking about.
    ///
    /// The point of the dialog is a paper held by mistake against a configured device. The point of
    /// THIS rule is that it does not also appear for every ordinary write - a dialog that shows up
    /// every time is clicked through blind within a day and then protects nothing.
    /// </summary>
    public class PaperConfirmTests
    {
        [Fact]
        public void Overwriting_real_orders_with_different_ones_is_worth_asking()
        {
            Assert.True(PaperConfirmMod.WouldLoseSomething("game:firewood\n", "game:beeswax\n"));
        }

        [Fact]
        public void Clearing_real_orders_is_worth_asking()
        {
            Assert.True(PaperConfirmMod.WouldLoseSomething("game:firewood\n", null));
            Assert.True(PaperConfirmMod.WouldLoseSomething("game:firewood\n", "   "));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   \n  ")]
        public void A_device_with_no_orders_has_nothing_to_lose(string current)
        {
            Assert.False(PaperConfirmMod.WouldLoseSomething(current, "game:beeswax\n"));
            Assert.False(PaperConfirmMod.WouldLoseSomething(current, null));
        }

        [Fact]
        public void Writing_the_same_orders_again_is_not_a_mistake()
        {
            // Copying a paper out and pressing it back in is a normal thing to do.
            Assert.False(PaperConfirmMod.WouldLoseSomething("game:firewood\n", "game:firewood\n"));
        }

        [Fact]
        public void Trailing_whitespace_is_not_a_difference()
        {
            // A paper that came back through an editor gains a newline. That is not a change worth
            // stopping the player over.
            Assert.False(PaperConfirmMod.WouldLoseSomething("game:firewood", "game:firewood\n\n"));
            Assert.False(PaperConfirmMod.WouldLoseSomething("  game:firewood  ", "game:firewood"));
        }

        [Fact]
        public void But_a_difference_inside_the_text_is()
        {
            Assert.True(PaperConfirmMod.WouldLoseSomething("game:firewood\namount 5", "game:firewood\namount 6"));
        }
    }
}
