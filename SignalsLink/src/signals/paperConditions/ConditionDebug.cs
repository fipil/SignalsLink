using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;

namespace SignalsLink.src.signals.paperConditions
{
    /// <summary>
    /// Opt-in tracing of one device's evaluation pass.
    ///
    /// A world is full of machines and logging all of them is unreadable, so a device traces
    /// itself only while its paper contains the marker line <c># debug</c>. The parser already
    /// skips lines starting with <c>#</c>, so the marker changes nothing about how the paper
    /// behaves — it travels with the block, needs no command and no restart, and is removed by
    /// deleting the line.
    ///
    /// Scopes belong to the current thread. Concurrent evaluations must not append to another
    /// device's trace; nested synchronous scopes restore their parent when closed.
    /// </summary>
    public static class ConditionDebug
    {
        public const string Marker = "# debug";

        /// <summary>The same thing written without the space, which is what most people type.</summary>
        public const string TightMarker = "#debug";

        /// <summary>A pass that says the same as the one before is repeated only this often.</summary>
        public const long HeartbeatMs = 10000;

        /// <summary>
        /// And no device writes more often than this, whatever it has to say.
        ///
        /// Suppressing only IDENTICAL passes is not enough: a dock walking a yard names a
        /// different column every tick, so nothing ever matched and the trace wrote several
        /// hundred lines a second - enough to make a singleplayer game stutter. Two passes a
        /// second is plenty to watch a device think.
        /// </summary>
        public const long MinIntervalMs = 500;

        /// <summary>
        /// And one pass never writes more than this many lines, plus its last one.
        ///
        /// A device says a line or two per section, so a paper of several sections must fit; at
        /// eight the sections at the bottom were never seen at all. Capping the rate alone was
        /// not enough: two passes a second of a chatty device is still a hundred lines a second.
        ///
        /// The last line is always kept, because a device says what came of the whole pass at the
        /// end of it, and cutting the tail off would throw away the one line worth reading.
        /// </summary>
        public const int MaxLinesPerPass = 24;

        private sealed class Scope
        {
            public ILogger Logger;
            public string Tag;
            public readonly List<string> Lines = new List<string>();
            public int Dropped;
            public string LastLine;
            public string LastMessage;
            public int LastCount;
            public Scope Parent;
        }
        [ThreadStatic] private static Scope current;
        private static readonly object repeatLock = new object();
        private static readonly Dictionary<string, Repeat> lastPass = new Dictionary<string, Repeat>();

        /// <summary>True while a traced pass is running. Check before building log strings.</summary>
        public static bool Enabled => current?.Logger != null;

        /// <summary>
        /// Does this paper ask to be traced? Asked on every tick of every device, so it stays two
        /// plain comparisons rather than a pattern.
        /// </summary>
        public static bool IsMarked(string conditionsText)
        {
            if (conditionsText == null) return false;

            return conditionsText.Contains(Marker, StringComparison.OrdinalIgnoreCase)
                || conditionsText.Contains(TightMarker, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Opens a traced scope. Always pair with <see cref="End"/> in a finally block.</summary>
        public static void Begin(ILogger apiLogger, string scopeTag)
        {
            current = new Scope { Logger = apiLogger, Tag = scopeTag, Parent = current };
        }

        /// <summary>Silences the trace until <see cref="Unmute"/>: a section without its own marker.</summary>
        public static void Mute() => Begin(null, null);

        public static void Unmute() => End();

        /// <summary>
        /// Closes the scope and writes the pass out - but only if it says something the last one
        /// did not.
        ///
        /// A device traces itself five to twenty times a second, and a device that is stuck says
        /// the same thing every time. Printing all of it buries the one line where something
        /// changed, which is the line the trace exists for. An unchanged pass is repeated once
        /// every <see cref="HeartbeatMs"/> so it is still visible that the device is alive.
        /// </summary>
        public static void End()
        {
            Scope scope = current;
            if (scope == null) return;
            // Detach before invoking user logger callbacks, including callbacks that throw or trace.
            current = scope.Parent;
            if (scope.Logger != null && scope.Lines.Count > 0) Flush(scope);
        }

        private static void Flush(Scope scope)
        {
            if (scope.LastLine != null)
            {
                if (scope.Dropped > 0) scope.Lines.Add("(" + scope.Dropped + " lines of this pass not shown)");
                scope.Lines.Add(scope.LastLine);
            }
            string text = string.Join("\n", scope.Lines);
            long skipped = 0;
            bool unchanged = false;
            lock (repeatLock)
            {
                long now = Environment.TickCount64;
                if (lastPass.TryGetValue(scope.Tag, out Repeat before))
                {
                    unchanged = before.Text == text;
                    long quiet = unchanged ? HeartbeatMs : MinIntervalMs;
                    if (now - before.At < quiet)
                    {
                        lastPass[scope.Tag] = new Repeat(before.Text, before.At, before.Skipped + 1);
                        return;
                    }
                    skipped = before.Skipped;
                }
                lastPass[scope.Tag] = new Repeat(text, now, 0);
            }
            // Never hold the shared throttle lock while calling an external logger.
            // The heartbeat is one line, not the whole pass again: it is above, unchanged.
            if (unchanged)
            {
                scope.Logger.Notification("[sl-dbg " + scope.Tag + "] (unchanged, " + (skipped + 1) + " passes)");
                return;
            }
            if (skipped > 0) scope.Logger.Notification("[sl-dbg " + scope.Tag + "] (" + skipped + " passes not shown)");
            foreach (string line in scope.Lines) scope.Logger.Notification("[sl-dbg " + scope.Tag + "] " + line);
        }

        public static void Log(string message)
        {
            Scope scope = current;
            if (scope?.Logger == null) return;
            // The same line again is counted, not repeated: a transfer reads one column per pair.
            if (scope.LastLine == null && scope.Lines.Count > 0 && message == scope.LastMessage)
            {
                scope.Lines[scope.Lines.Count - 1] = message + " (x" + ++scope.LastCount + ")";
                return;
            }
            if (scope.Lines.Count >= MaxLinesPerPass)
            {
                if (scope.LastLine != null) scope.Dropped++;
                scope.LastLine = message;
                return;
            }
            scope.Lines.Add(message);
            scope.LastMessage = message;
            scope.LastCount = 1;
        }

        private readonly struct Repeat
        {
            public Repeat(string text, long at, int skipped)
            {
                Text = text;
                At = at;
                Skipped = skipped;
            }

            public string Text { get; }
            public long At { get; }
            public int Skipped { get; }
        }

        /// <summary>What an inventory holds, short enough to read in a log line.</summary>
        public static string Describe(IInventory inventory)
        {
            if (inventory == null) return "<null>";

            var sb = new StringBuilder();
            sb.Append(inventory.Count).Append(" slots [");

            int shown = 0;
            for (int i = 0; i < inventory.Count; i++)
            {
                ItemSlot slot = inventory[i];
                if (slot == null || slot.Empty) continue;

                if (shown > 0) sb.Append(", ");
                sb.Append(i).Append(':').Append(slot.Itemstack?.Collectible?.Code?.ToString() ?? "?")
                  .Append(" x").Append(slot.StackSize);

                if (++shown >= 8) { sb.Append(", ..."); break; }
            }

            if (shown == 0) sb.Append("empty");
            return sb.Append(']').ToString();
        }

        public static string Describe(IDictionary<string, object> ctx, string key)
        {
            if (ctx == null || !ctx.TryGetValue(key, out object value)) return key + "=<missing>";
            return key + "=" + (value is IInventory inventory ? Describe(inventory) : value?.ToString() ?? "<null>");
        }
    }
}
