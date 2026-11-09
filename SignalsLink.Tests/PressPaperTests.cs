using System.Collections.Generic;
using System.Linq;
using SignalsLink.src.signals.paperConditions;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>The fruit press words on paper: `isPressing`, `do press`, `do release`.</summary>
    public class PressPaperTests
    {
        [Fact]
        public void The_screw_actions_parse_and_an_unknown_action_still_does_not()
        {
            var evaluator = new PaperConditionsEvaluator();
            evaluator.SetConditionsText("game:fruit-*\ndo press\n\ngame:pressedmash-*\ndo release\n");

            var blocks = evaluator.GetBlocks();
            Assert.Equal(2, blocks.Count);
            Assert.IsType<PressConditionAction>(Assert.Single(blocks[0].Actions));
            Assert.IsType<ReleaseConditionAction>(Assert.Single(blocks[1].Actions));

            var errors = new List<PaperConditionError>();
            PaperConditionsParser.Parse("game:fruit-*\ndo squeeze\n", errors);
            Assert.Equal("action", Assert.Single(errors).Reason);
        }

        [Fact]
        public void Pressing_is_a_block_condition_and_false_where_there_is_no_press()
        {
            var errors = new List<PaperConditionError>();
            PaperConditionsParser.Parse("in target\nisPressing\noutput 15\n\nin target\n!isPressing\noutput 1\n", errors);
            Assert.Empty(errors);

            var ctx = new Dictionary<string, object>();
            Assert.False(new PressingCondition(true).Evaluate(null, null, ctx, InventoryConditionScope.Target, false));
            Assert.False(new PressingCondition(false).Evaluate(null, null, ctx, InventoryConditionScope.Target, false));
        }

        [Fact]
        public void The_screw_actions_do_nothing_without_a_press_in_reach()
        {
            var ctx = new Dictionary<string, object>();
            Assert.False(new PressConditionAction().Execute(ctx));
            Assert.False(new ReleaseConditionAction().Execute(ctx));
            Assert.False(new PressConditionAction().Execute(null));
        }
    }
}
