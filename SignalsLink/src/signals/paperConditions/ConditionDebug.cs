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
    /// The current logger is a static: the server tick is single-threaded, and a host opens the
    /// scope right before its pass and closes it right after, so nothing else can be inside it.
    /// </summary>
    public static class ConditionDebug
    {
        public const string Marker = "# debug";

        /// <summary>The same thing written without the space, which is what most people type.</summary>
        public const string TightMarker = "#debug";

        /// <summary>A pass that says the same as the one before is repeated only this often.</summary>
        public const long HeartbeatMs = 10000;

        private static ILogger logger;
        private static string tag;
        private static List<string> pass;

        private static readonly Dictionary<string, Repeat> lastPass = new Dictionary<string, Repeat>();

        /// <summary>True while a traced pass is running. Check before building log strings.</summary>
        public static bool Enabled => logger != null;

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
            logger = apiLogger;
            tag = scopeTag;
            pass = apiLogger == null ? null : new List<string>();
        }

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
            if (logger != null && pass != null && pass.Count > 0) Flush();

            logger = null;
            tag = null;
            pass = null;
        }

        private static void Flush()
        {
            string text = string.Join("\n", pass);
            long now = Environment.TickCount64;

            if (lastPass.TryGetValue(tag, out Repeat before) && before.Text == text)
            {
                if (now - before.At < HeartbeatMs)
                {
                    lastPass[tag] = new Repeat(text, before.At, before.Skipped + 1);
                    return;
                }

                if (before.Skipped > 0)
                {
                    logger.Notification("[sl-dbg " + tag + "] (unchanged, " + before.Skipped + " passes)");
                }
            }

            foreach (string line in pass) logger.Notification("[sl-dbg " + tag + "] " + line);

            lastPass[tag] = new Repeat(text, now, 0);
        }

        public static void Log(string message)
        {
            pass?.Add(message);
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
