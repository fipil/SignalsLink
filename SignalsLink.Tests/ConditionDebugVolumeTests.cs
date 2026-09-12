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
    /// These tests share ConditionDebug's static scope, so they all live in one class and each one
    /// uses a tag of its own.
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
            public List<string> Lines { get; } = new List<string>();

            protected override void LogImpl(EnumLogType logType, string format, params object[] args)
            {
                Lines.Add(args == null || args.Length == 0 ? format : string.Format(format, args));
            }
        }
    }
}
