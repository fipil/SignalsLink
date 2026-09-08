using System;
using System.Collections.Generic;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;

namespace SignalsLink.src.signals.managedchute.transporting
{
    /// <summary>
    /// Something with an Output pin that <c>output</c> blocks on paper drive.
    /// </summary>
    public interface IConditionOutputSink
    {
        /// <summary>
        /// The value one evaluation pass computed for a pin. Called once per pass and ALWAYS: a
        /// pass in which no output block held reports 0, because the pin is a function of the
        /// current state, not of whatever last changed it. That is the whole point of the v2 rules
        /// — the old contract ("did the pin move?") let the pin freeze on a stale value whenever
        /// the pass was skipped.
        ///
        /// <paramref name="pin"/> is 0 on every device today; see <see cref="IDriverBlock.OutputPin"/>.
        /// </summary>
        void ApplyOutput(int pin, byte value);
    }

    /// <summary>
    /// Picks the block on paper that applies to one item stack, shared by every item transfer so
    /// they cannot drift apart.
    /// </summary>
    public static class ConditionResolution
    {
        /// <summary>
        /// The output rail on its own, for a host that has something to report on but nothing to
        /// carry — a solid block on its own end, say. The pin reports on what the device can SEE,
        /// which is not the same question as what it can move.
        /// </summary>
        public static byte RunOutputRail(PaperConditionsEvaluator evaluator, IDictionary<string, object> ctx)
        {
            IReadOnlyList<ConditionBlock> blocks = evaluator?.GetBlocks();
            if (blocks == null || blocks.Count == 0) return 0;

            DriverResult result = ConditionDriver.Run(
                blocks,
                true,   // there is no action rail here at all
                block =>
                {
                    bool holds = block.OutputConditionsHold(ctx);

                    if (ConditionDebug.Enabled)
                    {
                        ConditionDebug.Log("  output block value=" + block.OutputValue + " holds=" + holds);
                    }

                    return holds;
                },
                null);

            return result.GetOutput();
        }

        /// <summary>
        /// The directives of the first <b>action</b> block that accepts this stack.
        ///
        /// Output blocks are skipped here on purpose: the output rail belongs to
        /// <see cref="ConditionDriver"/>, which walks the paper once per pass. This is the
        /// question a transfer asks when it needs to know how to move a stack it already has in
        /// hand, not when it is choosing what to do this tick.
        ///
        /// Matching a block includes checking its directives, so a <c>target N ifEmpty</c> block
        /// stops matching the moment its slot fills and the walk moves on to the next one — a
        /// chain of them filling slot after slot is the whole point of the directive.
        /// </summary>
        /// <param name="canUse">
        /// Physical validity: would this block actually move anything? A block that cannot did no
        /// work, so the walk carries on to the next one.
        /// </param>
        public static bool ResolveActionDirectives(PaperConditionsEvaluator evaluator, ItemStack stack,
            IDictionary<string, object> ctx, out PaperConditionDirectives directives,
            System.Func<PaperConditionDirectives, bool> canUse = null)
        {
            directives = PaperConditionDirectives.Empty;
            if (evaluator == null || !evaluator.HasConditions) return true;

            IReadOnlyList<ConditionBlock> blocks = evaluator.GetBlocks();
            if (blocks == null || blocks.Count == 0) return true;

            for (int i = 0; i < blocks.Count; i++)
            {
                ConditionBlock block = blocks[i];

                if (block.IsOutputBlock) continue;
                if (!block.TryMatch(stack, ctx)) continue;
                if (canUse != null && !canUse(block.Directives)) continue;

                directives = block.Directives;
                return true;
            }

            return false;
        }
    }
}
