using System.Collections.Generic;
using SignalsLink.src.signals.paperConditions;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>
    /// The action rail: blocks that say to DO something rather than to carry something.
    ///
    /// This used to be a private pass inside one transfer class, so <c>do seal</c> worked when
    /// both ends of a section happened to be inventories and was silently dead when either end
    /// was a place in the world — under <c>load yard</c> it simply never ran, and the paper looked
    /// perfectly fine. These lock in the rules that replaced it.
    /// </summary>
    public class ConditionActionsTests
    {
        [Fact]
        public void A_block_that_only_acts_is_run()
        {
            // The case the whole change is about: no source condition anywhere in the block, so
            // there is nothing to carry and nothing for a transfer to select. It still acts.
            Spy spy = new Spy(does: true);

            Assert.True(ConditionActions.RunOn(Block(spy), Ctx()));
            Assert.Equal(1, spy.Runs);
        }

        [Fact]
        public void An_action_that_changed_nothing_does_not_claim_to_have_worked()
        {
            // A barrel that was sealed an hour ago. Reporting success would close the rail over
            // every block below it, forever.
            Spy spy = new Spy(does: false);

            Assert.False(ConditionActions.RunOn(Block(spy), Ctx()));
            Assert.Equal(1, spy.Runs);
        }

        [Fact]
        public void One_action_that_worked_is_enough_for_the_block()
        {
            Spy idle = new Spy(does: false);
            Spy worked = new Spy(does: true);

            Assert.True(ConditionActions.RunOn(Block(idle, worked), Ctx()));

            // Both are asked: they are separate lines of one block, not alternatives.
            Assert.Equal(1, idle.Runs);
            Assert.Equal(1, worked.Runs);
        }

        [Fact]
        public void An_output_block_is_not_an_action_block()
        {
            // Output blocks belong to the other rail. Running their actions here would run them
            // on every tick regardless of anything.
            Spy spy = new Spy(does: true);

            Assert.False(ConditionActions.RunOn(OutputBlock(spy), Ctx()));
            Assert.Equal(0, spy.Runs);
        }

        [Fact]
        public void A_block_with_nothing_to_do_is_no_action_block()
        {
            Assert.False(ConditionActions.RunOn(Block(), Ctx()));
            Assert.False(ConditionActions.RunOn(null, Ctx()));
        }

        // ---------------------------------------------------------------- on the rail

        [Fact]
        public void An_action_that_worked_closes_the_rail_below_it()
        {
            Spy first = new Spy(does: true);
            Spy second = new Spy(does: true);

            DriverResult result = Run(actionsBlocked: false, Block(first), Block(second));

            Assert.True(result.ActionPerformed);
            Assert.Equal(1, first.Runs);
            Assert.Equal(0, second.Runs);
        }

        [Fact]
        public void An_action_that_did_nothing_hands_over_to_the_block_below()
        {
            // The same rule a transfer follows: an attempt is not work. Without it a paper would
            // stop at its first `do seal` and never reach the line under it.
            Spy first = new Spy(does: false);
            Spy second = new Spy(does: true);

            DriverResult result = Run(actionsBlocked: false, Block(first), Block(second));

            Assert.True(result.ActionPerformed);
            Assert.Equal(1, first.Runs);
            Assert.Equal(1, second.Runs);
        }

        [Fact]
        public void A_closed_rail_runs_no_actions_at_all()
        {
            // Deliberate change of behaviour: explicit actions used to run in a pass of their own,
            // outside the rail, so they ignored backoff and credit. Now they wait a tick like
            // everything else.
            Spy spy = new Spy(does: true);

            Run(actionsBlocked: true, Block(spy));

            Assert.Equal(0, spy.Runs);
        }

        private static DriverResult Run(bool actionsBlocked, params ConditionBlock[] blocks)
        {
            IDictionary<string, object> ctx = Ctx();

            return ConditionDriver.Run(blocks, actionsBlocked,
                block => false,
                block => ConditionActions.RunOn(block, ctx));
        }

        /// <summary>No conditions, so the block holds whatever it is asked about.</summary>
        private static ConditionBlock Block(params IConditionAction[] actions)
        {
            return new ConditionBlock(new List<ScopedCondition>(), 0, false,
                PaperConditionDirectives.Empty, new List<IConditionAction>(actions));
        }

        private static ConditionBlock OutputBlock(params IConditionAction[] actions)
        {
            return new ConditionBlock(new List<ScopedCondition>(), 7, true,
                PaperConditionDirectives.Empty, new List<IConditionAction>(actions));
        }

        private static IDictionary<string, object> Ctx()
        {
            return new Dictionary<string, object>();
        }

        /// <summary>An action that reports whatever it was told to, and counts its askings.</summary>
        private sealed class Spy : IConditionAction
        {
            private readonly bool does;

            public Spy(bool does)
            {
                this.does = does;
            }

            public int Runs { get; private set; }

            public bool Execute(IDictionary<string, object> ctx)
            {
                Runs++;
                return does;
            }
        }
    }
}
