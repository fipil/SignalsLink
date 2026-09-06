using System.Collections.Generic;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;

namespace SignalsLink.src.signals.managedchute.transporting
{
    /// <summary>
    /// Something with an Output pin that an <c>output N</c> block on paper can drive. Item
    /// transfers get one only when their host actually has such a pin — the Damper does, the
    /// ManagedChute does not (all three of its anchors are inputs).
    /// </summary>
    public interface IConditionOutputSink
    {
        /// <summary>
        /// An <c>output N</c> block matched. Return true if the pin really changed, i.e. the block
        /// did work; false means it did nothing and evaluation should fall through to the next block.
        /// </summary>
        bool TrySetOutput(byte value);
    }

    /// <summary>
    /// Resolves which block on paper applies to one item stack, shared by every item transfer so
    /// they cannot drift apart.
    /// </summary>
    public static class ConditionResolution
    {
        /// <summary>
        /// Finds the directives of the first block whose conditions hold <b>and</b> whose action is
        /// a transfer.
        ///
        /// With no <paramref name="sink"/> this is the plain first-match the transfers have always
        /// done — the host has no Output pin, so an <c>output</c> block is meaningless to it.
        ///
        /// With a sink, the same walk runs but an <c>output</c> block is understood: it transfers
        /// nothing, so it wins only while it actually changes the pin. Once the pin already holds
        /// that value the block does no work and evaluation falls through to the next one — the
        /// same rule as a transfer that moves nothing, and what lets a plain <c>output 0</c> sit
        /// above the filling blocks as a reset.
        ///
        /// Both paths use the same block predicate on purpose. Matching a block includes checking
        /// its directives, so a <c>target N ifEmpty</c> block stops matching the moment its slot
        /// fills and the walk moves on to the next block — a chain of them filling slot after slot
        /// is the whole point of the directive, and it breaks the instant the two paths disagree.
        /// </summary>
        public static bool ResolveDirectives(PaperConditionsEvaluator evaluator, IConditionOutputSink sink,
            ItemStack stack, IDictionary<string, object> ctx, out PaperConditionDirectives directives)
        {
            directives = PaperConditionDirectives.Empty;
            if (evaluator == null || !evaluator.HasConditions) return true;

            if (sink == null)
            {
                return evaluator.Evaluate(stack, ctx, out byte _, out directives);
            }

            PaperConditionDirectives transferDirectives = null;

            evaluator.RunFirstMatching(stack, ctx, match =>
            {
                if (match.HasExplicitOutput)
                {
                    if (match.OutputValue > 15) return false; // not a signal value; skip the block
                    return sink.TrySetOutput(match.OutputValue);
                }

                transferDirectives = match.Directives;
                return true;
            });

            if (transferDirectives == null) return false;

            directives = transferDirectives;
            return true;
        }
    }
}
