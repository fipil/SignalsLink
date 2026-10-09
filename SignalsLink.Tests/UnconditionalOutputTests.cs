using System.Collections.Generic;
using SignalsLink.src.signals.paperConditions;

namespace SignalsLink.Tests
{
    /// <summary>
    /// A paragraph that is nothing but `output N`: a block with no conditions, so it always holds.
    /// At the bottom of a paper it is the value the pin falls back to; higher up it blankets every
    /// output block below it, because the first holding output block wins.
    /// </summary>
    public class UnconditionalOutputTests
    {
        [Fact]
        public void A_lone_output_line_is_a_block_that_always_holds()
        {
            var errors = new List<PaperConditionError>();
            CompiledConditions compiled = PaperConditionsParser.Parse("output 5\n", errors);

            ConditionBlock block = Assert.Single(compiled.Blocks);
            Assert.True(block.IsOutputBlock);
            Assert.Equal(5, block.OutputValue);
            Assert.True(block.OutputConditionsHold(new Dictionary<string, object>()));
            Assert.Empty(errors);
        }

        [Fact]
        public void At_the_bottom_it_is_the_fallback_value()
        {
            string paper = "in target\ngame:firewood 96\noutput 15\n\noutput 2\n";

            Assert.Equal(15, Output(paper, TestStacks.Inventory(4, TestStacks.Item("game:firewood", 96))));
            Assert.Equal(2, Output(paper, TestStacks.Inventory(4, TestStacks.Item("game:charcoal", 96))));
        }

        [Fact]
        public void Higher_up_it_blankets_the_output_blocks_below()
        {
            string paper = "output 7\n\nin target\ngame:firewood 96\noutput 15\n";

            Assert.Equal(7, Output(paper, TestStacks.Inventory(4, TestStacks.Item("game:firewood", 96))));
        }

        [Fact]
        public void A_paragraph_of_directives_alone_still_makes_no_block()
        {
            Assert.Empty(PaperConditionsParser.Parse("target 3\n").Blocks);
            Assert.Empty(PaperConditionsParser.Parse("amount 4\nkeep 2\n").Blocks);
        }

        private static byte Output(string paper, Vintagestory.API.Common.IInventory target)
        {
            var evaluator = new PaperConditionsEvaluator();
            evaluator.SetConditionsText(paper);
            var ctx = new Dictionary<string, object> { ["targetInventory"] = target };
            Assert.True(evaluator.RunSensorPass(null, ctx, out byte output));
            return output;
        }
    }
}
