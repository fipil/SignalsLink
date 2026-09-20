using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.CompilerServices;
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

        public const int MaxInspectedSlots = 64;
        public const int MaxMessageChars = 512;
        public const int MaxTrackedDevices = 1024;
        // At most two full passes in a burst; four new captures per second for one logger/server.
        public const int BurstPasses = 2;
        public const int PassesPerSecond = 4;

        private sealed class Device
        {
            public long NextCapture, LastWrite = long.MinValue / 2, Skipped;
            public string Text;
            public LinkedListNode<string> Node;
        }
        private sealed class Budget
        {
            public readonly object Sync = new object();
            public System.Func<long> Clock = () => Environment.TickCount64;
            public readonly Dictionary<string, Device> Devices = new Dictionary<string, Device>();
            public readonly LinkedList<string> Recent = new LinkedList<string>();
            public double Tokens = BurstPasses;
            public long Refilled = Environment.TickCount64, Suppressed;
            public Device Get(string tag)
            {
                if (!Devices.TryGetValue(tag, out Device device))
                {
                    if (Devices.Count >= MaxTrackedDevices)
                    {
                        string oldest = Recent.First.Value;
                        Recent.RemoveFirst(); Devices.Remove(oldest);
                    }
                    device = new Device { Node = Recent.AddLast(tag) };
                    Devices.Add(tag, device);
                }
                else { Recent.Remove(device.Node); Recent.AddLast(device.Node); }
                return device;
            }
        }
        // A world's logger owns its budget. It can be collected after leaving that world.
        private static readonly ConditionalWeakTable<ILogger, Budget> budgets = new();
        private static Budget ForLogger(ILogger logger) => budgets.GetValue(logger, _ => new Budget());

        private sealed class Scope
        {
            public ILogger Logger;
            public string Tag;
            public List<string> Lines;
            public Budget Budget;
            public Device Device;
            public int Dropped;
            public string LastLine;
            public string LastMessage;
            public int LastCount;
            public Scope Parent;
        }
        [ThreadStatic] private static Scope current;
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
            var scope = new Scope { Parent = current };
            current = scope;
            if (apiLogger == null) return;
            var budget = ForLogger(apiLogger);
            lock (budget.Sync)
            {
                long now = budget.Clock();
                var device = budget.Get(scopeTag ?? "");
                if (now < device.NextCapture) { device.Skipped++; return; }
                budget.Tokens = Math.Min(BurstPasses, budget.Tokens + Math.Max(0, now - budget.Refilled) * PassesPerSecond / 1000d);
                budget.Refilled = now;
                if (budget.Tokens < 1)
                {
                    device.Skipped++; budget.Suppressed++;
                    // Spread retries so a fixed tick order does not always favour the same blocks.
                    device.NextCapture = now + MinIntervalMs + (uint)(scopeTag ?? "").GetHashCode() % 500;
                    return;
                }
                budget.Tokens--;
                device.NextCapture = now + MinIntervalMs;
                scope.Logger = apiLogger; scope.Tag = Short(scopeTag ?? "");
                scope.Budget = budget; scope.Device = device;
                scope.Lines = new List<string>(MaxLinesPerPass + 4);
            }
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
            long skipped, globalSkipped;
            bool unchanged;
            lock (scope.Budget.Sync)
            {
                long now = scope.Budget.Clock();
                var device = scope.Device;
                unchanged = device.Text == text;
                if (unchanged && now - device.LastWrite < HeartbeatMs)
                { device.Skipped++; return; }
                skipped = device.Skipped; device.Skipped = 0;
                globalSkipped = scope.Budget.Suppressed; scope.Budget.Suppressed = 0;
                device.Text = text; device.LastWrite = now;
            }
            // One bounded logger call, outside the lock, instead of one call per line.
            var output = new StringBuilder();
            string prefix = "[sl-dbg " + scope.Tag + "] ";
            if (globalSkipped > 0) output.Append(prefix).Append("(server trace budget: ").Append(globalSkipped).Append(" captures suppressed)").AppendLine();
            if (unchanged) output.Append(prefix).Append("(unchanged sampled state, ").Append(skipped + 1).Append(" passes since last report)");
            else
            {
                if (skipped > 0) output.Append(prefix).Append('(').Append(skipped).Append(" passes not captured or unchanged)").AppendLine();
                for (int i = 0; i < scope.Lines.Count; i++)
                {
                    if (i > 0) output.AppendLine();
                    output.Append(prefix).Append(scope.Lines[i]);
                }
            }
            scope.Logger.Notification(output.ToString());
        }

        private static string Short(string message)
        {
            if (message == null) return "";
            if (message.Length > MaxMessageChars) message = message.Substring(0, MaxMessageChars - 3) + "...";
            return message.Replace('\r', ' ').Replace('\n', ' ');
        }

        public static void Log(string message)
        {
            Scope scope = current;
            if (scope?.Logger == null) return;
            message = Short(message);
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

        /// <summary>What an inventory holds, short enough to read in a log line.</summary>
        public static string Describe(IInventory inventory)
        {
            if (inventory == null) return "<null>";

            var sb = new StringBuilder();
            sb.Append(inventory.Count).Append(" slots [");

            int shown = 0, inspected = 0;
            int count = inventory.Count;
            for (int i = 0; i < count && i < MaxInspectedSlots; i++)
            {
                inspected++;
                ItemSlot slot = inventory[i];
                if (slot == null || slot.Empty) continue;

                if (shown > 0) sb.Append(", ");
                sb.Append(i).Append(':').Append(slot.Itemstack?.Collectible?.Code?.ToString() ?? "?")
                  .Append(" x").Append(slot.StackSize);

                if (++shown >= 8) break;
            }

            if (shown == 0) sb.Append(inspected == count ? "empty" : "no items in inspected slots");
            if (inspected < count) sb.Append("; ").Append(count - inspected).Append(" slots not inspected");
            return sb.Append(']').ToString();
        }

        public static string Describe(IDictionary<string, object> ctx, string key)
        {
            if (ctx == null || !ctx.TryGetValue(key, out object value)) return key + "=<missing>";
            return key + "=" + (value is IInventory inventory ? Describe(inventory) : value?.ToString() ?? "<null>");
        }
    }
}
