using System.Collections.Generic;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>
    /// How much a traced device is allowed to write.
    ///
    /// Measured in game on a dock serving a yard: 54 to 119 lines a second, enough to make a
    /// singleplayer world stutter. Capping how OFTEN a device writes was not enough, because one
    /// pass of a dock names every column it read and every pair it tried - three dozen lines for a
    /// small yard, and a yard may be much larger than that.
    ///
    /// Each test uses a distinct tag so rate limiting does not couple separate scenarios.
    /// </summary>
    public class ConditionDebugVolumeTests
    {
        [Fact]
        public void One_pass_never_writes_more_than_the_cap()
        {
            Log logger = Pass("cap", 500);

            // The cap, the line saying what was left out, and the pass's own last line.
            Assert.Equal(ConditionDebug.MaxLinesPerPass + 2, logger.Lines.Count);
        }

        [Fact]
        public void The_last_line_of_a_long_pass_always_survives()
        {
            // A device says what came of the whole pass at the end of it. Cutting the tail off to
            // fit the cap would throw away the one line worth reading.
            Log logger = Pass("tail", 500);

            Assert.EndsWith("line 499", logger.Lines[^1]);
        }

        [Fact]
        public void And_it_says_how_much_it_left_out()
        {
            Log logger = Pass("counted", ConditionDebug.MaxLinesPerPass + 8);

            Assert.Contains("7 lines of this pass not shown", logger.Lines[^2]);
        }

        [Fact]
        public void A_short_pass_is_written_whole_and_says_nothing_about_lines_left_out()
        {
            Log logger = Pass("short", 3);

            Assert.Equal(3, logger.Lines.Count);
            Assert.DoesNotContain(logger.Lines, line => line.Contains("more lines"));
        }

        [Fact]
        public void A_second_pass_hard_on_the_heels_of_the_first_is_held_back()
        {
            Log logger = new Log();

            ConditionDebug.Begin(logger, "twice");
            ConditionDebug.Log("something");
            ConditionDebug.End();

            ConditionDebug.Begin(logger, "twice");
            ConditionDebug.Log("something else");
            ConditionDebug.End();

            Assert.Single(logger.Lines);
        }

        [Fact]
        public void What_was_dropped_from_one_pass_is_not_counted_against_the_next()
        {
            Log logger = new Log();

            ConditionDebug.Begin(logger, "reset");
            for (int i = 0; i < 40; i++) ConditionDebug.Log("line " + i);
            ConditionDebug.End();

            // Far enough apart in tag that the rate limit is not what is being measured here.
            Log second = Pass("reset-again", 2);

            Assert.Equal(2, second.Lines.Count);
        }

        [Fact]
        public void One_line_above_the_cap_keeps_the_tail_without_an_omission_notice()
        {
            var logger=Pass("one-above-cap",ConditionDebug.MaxLinesPerPass+1);
            Assert.Equal(ConditionDebug.MaxLinesPerPass+1,logger.Lines.Count);
            Assert.EndsWith("line "+ConditionDebug.MaxLinesPerPass,logger.Lines[^1]);
        }

        [Fact]
        public void The_same_line_again_is_counted_rather_than_repeated()
        {
            // A dock read the same ground column once per pair it tried, four lines apart by nothing.
            Log logger = new Log();

            ConditionDebug.Begin(logger, "repeated");
            ConditionDebug.Log("column");
            ConditionDebug.Log("column");
            ConditionDebug.Log("column");
            ConditionDebug.Log("end");
            ConditionDebug.End();

            Assert.Equal(2, logger.Lines.Count);
            Assert.EndsWith("column (x3)", logger.Lines[0]);
            Assert.EndsWith("end", logger.Lines[1]);
        }

        [Fact]
        public void A_muted_stretch_writes_nothing_and_the_pass_carries_on_after_it()
        {
            // A section without its own `# debug` on a paper that has one elsewhere.
            Log logger = new Log();

            ConditionDebug.Begin(logger, "muted");
            ConditionDebug.Log("before");
            ConditionDebug.Mute();
            Assert.False(ConditionDebug.Enabled);
            ConditionDebug.Log("silenced");
            ConditionDebug.Unmute();
            Assert.True(ConditionDebug.Enabled);
            ConditionDebug.Log("after");
            ConditionDebug.End();

            Assert.Equal(2, logger.Lines.Count);
            Assert.EndsWith("before", logger.Lines[0]);
            Assert.EndsWith("after", logger.Lines[1]);
        }

        [Fact]
        public void Untraced_worker_cannot_modify_a_pass_while_it_is_being_flushed()
        {
            var logger=new Log();
            logger.Callback=()=>System.Threading.Tasks.Task.Run(()=> {
                Assert.False(ConditionDebug.Enabled);
                ConditionDebug.Log("foreign transfer");
            }).GetAwaiter().GetResult();
            ConditionDebug.Begin(logger,"worker-during-flush");
            ConditionDebug.Log("first"); ConditionDebug.Log("second");
            ConditionDebug.End();
            Assert.Equal(2,logger.Lines.Count);
            Assert.DoesNotContain(logger.Lines,l=>l.Contains("foreign"));
            Assert.False(ConditionDebug.Enabled);
        }

        [Fact]
        public async System.Threading.Tasks.Task Concurrent_scopes_keep_their_own_logger_and_lines()
        {
            using var rendezvous=new System.Threading.Barrier(2);
            var logs=new[] {new Log(),new Log()};
            var tasks=Enumerable.Range(0,2).Select(i=>System.Threading.Tasks.Task.Factory.StartNew(()=> {
                ConditionDebug.Begin(logs[i],"concurrent-"+i);
                try {
                    Assert.True(rendezvous.SignalAndWait(TimeSpan.FromSeconds(5)));
                    ConditionDebug.Log("only-"+i);
                    Assert.True(rendezvous.SignalAndWait(TimeSpan.FromSeconds(5)));
                } finally { ConditionDebug.End(); }
            },System.Threading.Tasks.TaskCreationOptions.LongRunning)).ToArray();
            await System.Threading.Tasks.Task.WhenAll(tasks);
            for(int i=0;i<2;i++) Assert.Equal("[sl-dbg concurrent-"+i+"] only-"+i,Assert.Single(logs[i].Lines));
        }

        [Fact]
        public void Nested_scope_restores_parent_and_throwing_logger_does_not_leave_scope_enabled()
        {
            var outer=new Log(); var inner=new Log();
            ConditionDebug.Begin(outer,"outer-scope"); ConditionDebug.Log("before");
            ConditionDebug.Begin(inner,"inner-scope"); ConditionDebug.Log("inside"); ConditionDebug.End();
            ConditionDebug.Log("after"); ConditionDebug.End();
            Assert.Equal(2,outer.Lines.Count); Assert.Single(inner.Lines);
            var throwing=new Log { Callback=()=>throw new InvalidOperationException("logger failed") };
            ConditionDebug.Begin(throwing,"throwing-scope"); ConditionDebug.Log("message");
            Assert.Throws<InvalidOperationException>(ConditionDebug.End);
            Assert.False(ConditionDebug.Enabled);
        }

        private static void FreezeBudget(Log logger, long now = 10000)
        {
            var budget = typeof(ConditionDebug).GetMethod("ForLogger", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] { logger });
            var type = budget.GetType();
            type.GetField("Clock").SetValue(budget, (System.Func<long>)(() => now));
            type.GetField("Refilled").SetValue(budget, now);
        }

        [Fact]
        public void Throttled_capture_skips_formatting_but_does_not_skip_work()
        {
            var logger = new Log(); FreezeBudget(logger);
            int formatted = 0, transfers = 0;
            for (int i = 0; i < 100; i++)
            {
                ConditionDebug.Begin(logger, "early-throttle");
                try
                {
                    transfers++;
                    if (ConditionDebug.Enabled) { formatted++; ConditionDebug.Log("captured"); }
                }
                finally { ConditionDebug.End(); }
            }
            Assert.Equal(100, transfers); Assert.Equal(1, formatted); Assert.Single(logger.Lines);
            Assert.Equal(1, logger.Calls);
        }

        [Fact]
        public void Shared_budget_limits_many_devices_and_summarizes_suppression()
        {
            var logger = new Log(); FreezeBudget(logger);
            int captures = 0;
            for (int i = 0; i < 100; i++)
            {
                ConditionDebug.Begin(logger, "device-" + i);
                if (ConditionDebug.Enabled) { captures++; ConditionDebug.Log("state"); }
                ConditionDebug.End();
            }
            Assert.Equal(ConditionDebug.BurstPasses, captures);
            Assert.Equal(2, logger.Calls);
            // Advance the injected clock, leaving refill time at its original value.
            var budget = typeof(ConditionDebug).GetMethod("ForLogger", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] { logger });
            budget.GetType().GetField("Clock").SetValue(budget, (System.Func<long>)(() => 11000));
            ConditionDebug.Begin(logger, "after-burst"); ConditionDebug.Log("state"); ConditionDebug.End();
            Assert.Contains(logger.Lines, line => line.Contains("98 captures suppressed"));
            Assert.Equal(3, logger.Calls);
        }

        [Fact]
        public void Long_pass_uses_one_logger_call_and_messages_cannot_inject_extra_lines()
        {
            var logger = new Log(); FreezeBudget(logger);
            ConditionDebug.Begin(logger, "bounded-lines");
            for (int i = 0; i < 100; i++) ConditionDebug.Log(i + new string('x', 2000) + "\\nextra");
            ConditionDebug.End();
            Assert.Equal(1, logger.Calls);
            Assert.Equal(ConditionDebug.MaxLinesPerPass + 2, logger.Lines.Count);
            Assert.All(logger.Lines, line => Assert.True(line.Length < ConditionDebug.MaxMessageChars + 100));
        }

        [Fact]
        public void Large_empty_inventory_is_only_sampled_and_is_not_reported_as_wholly_empty()
        {
            int reads = 0;
            var inventory = AnchorLifecycleTests.Proxy.Make<IInventory>((m, a) =>
            {
                if (m.Name == "get_Count") return 100000;
                if (m.Name == "get_Item") { reads++; return null; }
                return AnchorLifecycleTests.Proxy.Unhandled;
            });
            string description = ConditionDebug.Describe(inventory);
            Assert.Equal(ConditionDebug.MaxInspectedSlots, reads);
            Assert.Contains("slots not inspected", description);
            Assert.DoesNotContain("[empty]", description);
        }

        [Fact]
        public void Device_history_is_bounded_and_loggers_do_not_share_a_budget()
        {
            var first = new Log(); FreezeBudget(first);
            for (int i = 0; i < ConditionDebug.MaxTrackedDevices + 50; i++)
            { ConditionDebug.Begin(first, "bounded-" + i); ConditionDebug.End(); }
            var budget = typeof(ConditionDebug).GetMethod("ForLogger", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] { first });
            var devices = (System.Collections.IDictionary)budget.GetType().GetField("Devices").GetValue(budget);
            Assert.Equal(ConditionDebug.MaxTrackedDevices, devices.Count);
            var second = new Log(); FreezeBudget(second);
            ConditionDebug.Begin(second, "fresh-server"); Assert.True(ConditionDebug.Enabled);
            ConditionDebug.Log("fresh"); ConditionDebug.End(); Assert.Single(second.Lines);
        }

        private static Log Pass(string tag, int lines)
        {
            Log logger = new Log();

            ConditionDebug.Begin(logger, tag);
            for (int i = 0; i < lines; i++) ConditionDebug.Log("line " + i);
            ConditionDebug.End();

            return logger;
        }

        private sealed class Log : LoggerBase
        {
            public Action Callback { get; set; }
            public int Calls { get; private set; }
            public List<string> Lines { get; } = new List<string>();

            protected override void LogImpl(EnumLogType logType, string format, params object[] args)
            {
                Calls++;
                Callback?.Invoke();
                Lines.AddRange((args == null || args.Length == 0 ? format : string.Format(format, args)).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));
            }
        }
    }
}
