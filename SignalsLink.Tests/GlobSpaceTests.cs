using System.Collections.Generic;
using SignalsLink.src.signals.paperConditions;

namespace SignalsLink.Tests
{
    /// <summary>
    /// A code pattern with a space in it. Spotted in a real paper written by a player:
    /// `game:planks *` reads like "planks, any amount" but is a pattern for a code containing a
    /// space, so it matches nothing - and because a block ANDs its conditions, the whole block
    /// went dead in silence.
    /// </summary>
    public class GlobSpaceTests
    {
        [Fact]
        public void A_pattern_with_a_space_is_reported()
        {
            var errors = new List<PaperConditionError>();
            PaperConditionsParser.Parse("game:planks 23\ngame:planks *\n", errors);

            PaperConditionError error = Assert.Single(errors);
            Assert.Equal(2, error.Line);
            Assert.Equal("pattern-space", error.Reason);
        }

        [Fact]
        public void An_ordinary_glob_is_not_reported()
        {
            var errors = new List<PaperConditionError>();
            PaperConditionsParser.Parse("game:planks*\n", errors);

            Assert.Empty(errors);
        }

        [Fact]
        public void Conditions_in_one_block_are_ANDed_not_overridden()
        {
            // Two conditions on the same code are not a duplicate to be resolved - both have to
            // hold. That is what makes `game:planks 5+` with `game:planks 20-` mean "between 5
            // and 20", and it is also why one impossible line kills the block.
            ConditionBlock block = FirstBlock("in target\ngame:firewood 5+\ngame:firewood 20-\n");

            Assert.True(block.OutputConditionsHold(Ctx(10)));
            Assert.False(block.OutputConditionsHold(Ctx(3)));
            Assert.False(block.OutputConditionsHold(Ctx(40)));
        }

        private static ConditionBlock FirstBlock(string paper)
        {
            var evaluator = new PaperConditionsEvaluator();
            evaluator.SetConditionsText(paper);
            return evaluator.GetBlocks()[0];
        }

        private static IDictionary<string, object> Ctx(int firewood)
        {
            return new Dictionary<string, object>
            {
                ["targetInventory"] = TestStacks.Inventory(1, TestStacks.Item("game:firewood", firewood))
            };
        }
    }
}
