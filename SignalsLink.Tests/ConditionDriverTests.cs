using System;
using System.Collections.Generic;
using SignalsLink.src.signals.paperConditions;

namespace SignalsLink.Tests
{
    /// <summary>
    /// Level-A tests: the evaluation pass from paper-conditions-rules-v2.md, driven with fake
    /// blocks and scripted answers. No game types are involved, so these are the tests that have
    /// to keep passing whatever else happens to the mod.
    /// </summary>
    public class ConditionDriverTests
    {
        // ---------------------------------------------------------------- output rail

        [Fact]
        public void The_first_output_block_that_holds_wins_and_the_rest_are_not_even_asked()
        {
            var asked = new List<string>();
            var blocks = Blocks(Output(3, name: "first"), Output(7, name: "second"));

            DriverResult result = ConditionDriver.Run(blocks, false, Asking(asked, _ => true), NeverActs());

            Assert.Equal(3, result.GetOutput());
            Assert.Equal(new[] { "first" }, asked);
        }

        [Fact]
        public void An_output_block_whose_conditions_fail_hands_over_to_the_next_one()
        {
            var blocks = Blocks(Output(3, name: "no"), Output(7, name: "yes"));

            DriverResult result = ConditionDriver.Run(blocks, false, b => b.Name == "yes", NeverActs());

            Assert.Equal(7, result.GetOutput());
        }

        [Fact]
        public void A_pin_no_block_claimed_reads_zero()
        {
            var blocks = Blocks(Output(9));

            DriverResult result = ConditionDriver.Run(blocks, false, _ => false, NeverActs());

            Assert.Equal(0, result.GetOutput());
            Assert.False(result.HasOutput());
        }

        [Fact]
        public void A_claimed_pin_is_not_zeroed_at_the_end()
        {
            var blocks = Blocks(Output(0), Output(11));

            DriverResult result = ConditionDriver.Run(blocks, false, _ => true, NeverActs());

            // `output 0` is a value a block really reported, not the "nothing held" default.
            Assert.True(result.HasOutput());
            Assert.Equal(0, result.GetOutput());
        }

        [Fact]
        public void The_output_dot_sentinel_is_carried_through_untouched()
        {
            var blocks = Blocks(Output(ConditionBlock.DefaultOutputValue));

            DriverResult result = ConditionDriver.Run(blocks, false, _ => true, NeverActs());

            Assert.Equal(255, result.GetOutput());
        }

        [Fact]
        public void Outputs_are_addressed_by_pin()
        {
            var blocks = Blocks(Output(3, pin: 0), Output(9, pin: 1), Output(11, pin: 0));

            DriverResult result = ConditionDriver.Run(blocks, false, _ => true, NeverActs());

            Assert.Equal(3, result.GetOutput(0));
            Assert.Equal(9, result.GetOutput(1));
        }

        // ---------------------------------------------------------------- action rail

        [Fact]
        public void A_block_whose_action_does_no_work_falls_through_to_the_next()
        {
            var tried = new List<string>();
            var blocks = Blocks(Action("empty"), Action("works"));

            DriverResult result = ConditionDriver.Run(blocks, false, _ => true, Acting(tried, b => b.Name == "works"));

            Assert.True(result.ActionPerformed);
            Assert.Equal(new[] { "empty", "works" }, tried);
        }

        [Fact]
        public void Only_one_action_runs_per_pass()
        {
            var tried = new List<string>();
            var blocks = Blocks(Action("first"), Action("second"));

            ConditionDriver.Run(blocks, false, _ => true, Acting(tried, _ => true));

            Assert.Equal(new[] { "first" }, tried);
        }

        [Fact]
        public void A_blocked_action_rail_stops_the_actions_but_not_the_pass()
        {
            var tried = new List<string>();
            var blocks = Blocks(Output(5), Action("nope"));

            DriverResult result = ConditionDriver.Run(blocks, true, _ => true, Acting(tried, _ => true));

            // The pin still updates with no input credit - that is what makes it a sensor.
            Assert.Equal(5, result.GetOutput());
            Assert.False(result.ActionPerformed);
            Assert.Empty(tried);
        }

        [Fact]
        public void The_pass_is_skipped_when_there_is_nothing_to_compute_and_nothing_to_do()
        {
            var tried = new List<string>();
            var asked = new List<string>();
            var blocks = Blocks(Action("a"), Action("b"));

            DriverResult result = ConditionDriver.Run(blocks, true, Asking(asked, _ => true), Acting(tried, _ => true));

            Assert.Empty(tried);
            Assert.Empty(asked);
            Assert.False(result.HasOutput());
        }

        [Fact]
        public void One_pass_can_set_the_output_and_act_at_the_same_time()
        {
            var blocks = Blocks(Output(9), Action("move"));

            DriverResult result = ConditionDriver.Run(blocks, false, _ => true, _ => true);

            Assert.Equal(9, result.GetOutput());
            Assert.True(result.ActionPerformed);
        }

        // ---------------------------------------------------------------- the rails interleave

        [Fact]
        public void An_output_block_below_an_action_sees_what_the_action_did()
        {
            // The same two blocks in two orders. The output block only holds once the action has
            // run, so where it stands decides what the pin reports in that very same pass.
            bool moved = false;

            var above = Blocks(Output(15, name: "watch"), Action("move"));
            DriverResult resultAbove = ConditionDriver.Run(above, false, _ => moved, _ => moved = true);

            moved = false;
            var below = Blocks(Action("move"), Output(15, name: "watch"));
            DriverResult resultBelow = ConditionDriver.Run(below, false, _ => moved, _ => moved = true);

            Assert.Equal(0, resultAbove.GetOutput());
            Assert.Equal(15, resultBelow.GetOutput());
        }

        // ---------------------------------------------------------------- the sensor exception

        [Fact]
        public void For_the_sensor_every_block_is_an_output_block()
        {
            var tried = new List<string>();
            var blocks = Blocks(Action("plain"));   // no explicit `output` written on it

            DriverResult result = ConditionDriver.Run(
                blocks, false, _ => true, Acting(tried, _ => true), everyBlockIsOutput: true);

            Assert.Equal(15, result.GetOutput());   // the effective default for a sensor block
            Assert.Empty(tried);                    // a sensor has no action rail at all
        }

        // ---------------------------------------------------------------- plumbing

        private sealed class FakeBlock : IDriverBlock
        {
            public string Name { get; init; }
            public bool IsOutputBlock { get; init; }
            public int OutputPin { get; init; }
            public byte OutputValue { get; init; }
        }

        private static FakeBlock Output(byte value, int pin = 0, string name = null)
        {
            return new FakeBlock
            {
                Name = name ?? ("output" + value),
                IsOutputBlock = true,
                OutputPin = pin,
                OutputValue = value
            };
        }

        private static FakeBlock Action(string name)
        {
            // 15 is what the parser leaves on a block that carries no explicit `output`.
            return new FakeBlock { Name = name, IsOutputBlock = false, OutputPin = 0, OutputValue = 15 };
        }

        private static IReadOnlyList<FakeBlock> Blocks(params FakeBlock[] blocks)
        {
            return blocks;
        }

        private static Func<FakeBlock, bool> NeverActs()
        {
            return _ => throw new Xunit.Sdk.XunitException("the action rail should not have been reached");
        }

        private static Func<FakeBlock, bool> Asking(List<string> log, Func<FakeBlock, bool> answer)
        {
            return b => { log.Add(b.Name); return answer(b); };
        }

        private static Func<FakeBlock, bool> Acting(List<string> log, Func<FakeBlock, bool> answer)
        {
            return b => { log.Add(b.Name); return answer(b); };
        }
    }
}
