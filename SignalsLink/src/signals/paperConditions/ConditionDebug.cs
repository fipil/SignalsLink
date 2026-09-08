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

        private static ILogger logger;
        private static string tag;

        /// <summary>True while a traced pass is running. Check before building log strings.</summary>
        public static bool Enabled => logger != null;

        /// <summary>Does this paper ask to be traced?</summary>
        public static bool IsMarked(string conditionsText)
        {
            return conditionsText != null && conditionsText.Contains(Marker, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Opens a traced scope. Always pair with <see cref="End"/> in a finally block.</summary>
        public static void Begin(ILogger apiLogger, string scopeTag)
        {
            logger = apiLogger;
            tag = scopeTag;
        }

        public static void End()
        {
            logger = null;
            tag = null;
        }

        public static void Log(string message)
        {
            logger?.Notification("[sl-dbg " + tag + "] " + message);
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
