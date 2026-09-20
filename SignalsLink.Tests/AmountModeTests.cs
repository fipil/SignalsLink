using System.Collections.Generic;
using SignalsLink.src.signals.paperConditions;

namespace SignalsLink.Tests
{
    /// <summary>
    /// `amount 10`, `amount 10-` and `amount 10+`.
    ///
    /// Plain `amount N` has always been all-or-nothing, and that is the right default: a recipe
    /// wants four hides or it wants to wait. But it is a poor fit for "move what is there" and for
    /// "clear the lot in one go", and both of those had no way of being written down at all.
    ///
    /// The suffixes are not new vocabulary. Conditions have read `game:firewood 96+` as "ninety-six
    /// or more" since the beginning, so the same mark on an amount says the same thing about the
    /// same number - only as an instruction rather than a question.
    /// </summary>
    public class AmountModeTests
    {
        [Fact]
        public void A_bare_amount_is_still_all_or_nothing()
        {
            // Papers already written mean exactly what they meant before.
            PaperConditionDirectives directives = Directives("game:firewood\namount 10\n");

            Assert.Equal(10, directives.Amount);
            Assert.Equal(AmountMode.Exactly, directives.AmountMode);
            Assert.True(directives.IsAtomicAmount);
        }

        [Fact]
        public void A_minus_means_that_much_at_most()
        {
            PaperConditionDirectives directives = Directives("game:firewood\namount 10-\n");

            Assert.Equal(10, directives.Amount);
            Assert.Equal(AmountMode.AtMost, directives.AmountMode);
            Assert.False(directives.IsAtomicAmount);
        }

        [Fact]
        public void A_plus_means_that_much_at_least()
        {
            PaperConditionDirectives directives = Directives("game:firewood\namount 10+\n");

            Assert.Equal(10, directives.Amount);
            Assert.Equal(AmountMode.AtLeast, directives.AmountMode);
        }

        [Fact]
        public void At_least_is_still_all_or_nothing_about_the_number()
        {
            // "Ten or more" is a floor: nine will not do, and waiting is the point of writing it.
            PaperConditionDirectives directives = Directives("game:firewood\namount 10+\n");

            Assert.True(directives.IsAtomicAmount);
        }

        [Fact]
        public void At_most_takes_whatever_is_there()
        {
            // The one form that never waits: fewer than ten is fewer than ten.
            PaperConditionDirectives directives = Directives("game:firewood\namount 10-\n");

            Assert.False(directives.IsAtomicAmount);
        }

        [Fact]
        public void Only_at_least_reaches_past_the_number()
        {
            Assert.False(Directives("game:firewood\namount 10\n").TakesEverythingAvailable);
            Assert.False(Directives("game:firewood\namount 10-\n").TakesEverythingAvailable);
            Assert.True(Directives("game:firewood\namount 10+\n").TakesEverythingAvailable);
        }

        [Fact]
        public void Litres_carry_the_marks_too()
        {
            // Liquids count in litres, and there is no reason the vocabulary should differ.
            PaperConditionDirectives directives = Directives("*water*\namount 2.5-\n");

            Assert.Equal(2.5m, directives.Amount);
            Assert.Equal(AmountMode.AtMost, directives.AmountMode);
        }

        [Fact]
        public void A_space_before_the_mark_is_still_the_same_thing()
        {
            // Somebody will write it, and refusing it would teach nothing.
            Assert.Equal(AmountMode.AtLeast, Directives("game:firewood\namount 10 +\n").AmountMode);
        }

        [Theory]
        [InlineData("amount")]
        [InlineData("amount -")]
        [InlineData("amount +5")]
        [InlineData("amount 10++")]
        [InlineData("amount ten-")]
        public void Anything_else_is_a_paper_error(string line)
        {
            IReadOnlyList<PaperConditionError> errors = Errors("game:firewood\n" + line + "\n");

            Assert.Equal("amount", Assert.Single(errors).Reason);
        }

        [Fact]
        public void No_amount_at_all_is_not_a_mistake()
        {
            Assert.Empty(Errors("game:firewood\ntarget 2\n"));
        }

        // ---------------------------------------------------------------- plumbing

        private static PaperConditionDirectives Directives(string paper)
        {
            return Assert.Single(PaperConditionsParser.Parse(paper).Blocks).Directives;
        }

        private static IReadOnlyList<PaperConditionError> Errors(string paper)
        {
            List<PaperConditionError> errors = new List<PaperConditionError>();
            PaperConditionsParser.Parse(paper, errors);
            return errors;
        }
    }
}
