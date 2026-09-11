using System.Collections.Generic;

namespace SignalsLink.src.signals.paperConditions
{
    /// <summary>
    /// Running what a block says to DO, as opposed to what it says to carry.
    ///
    /// Shared rather than owned by a transfer, because that is the whole fault this replaced: the
    /// action pass used to be a method of one transfer class, so <c>do seal</c> worked when both
    /// ends of a section happened to be inventories and did nothing at all when either end was a
    /// place in the world. The boundary lay at an internal choice of transfer class, which nobody
    /// writing a paper can see.
    /// </summary>
    public static class ConditionActions
    {
        /// <summary>
        /// Everything this block says to do. True only if something really happened.
        ///
        /// "Really happened" is not a formality. The action rail runs on every tick of the device
        /// - for the dock at work, twenty times a second - so an action that charged for each
        /// attempt would eat a supply in a moment, and an action that reported success without
        /// doing anything would close the rail over the blocks below it forever. <c>do seal</c>
        /// gets this right: a barrel that is already sealed answers false and costs nothing.
        /// Every action added later has to do the same, and it is the first thing to review.
        /// </summary>
        public static bool RunOn(ConditionBlock block, IDictionary<string, object> ctx)
        {
            if (block == null || !block.HasActions || block.IsOutputBlock) return false;

            // Judged with no stack in hand: the plain "is this true of the inventory?" question,
            // which is the one an action has always been asked.
            if (!block.MatchesActionContext(null, ctx)) return false;

            bool did = false;

            for (int i = 0; i < block.Actions.Count; i++)
            {
                if (block.Actions[i].Execute(ctx)) did = true;
            }

            return did;
        }
    }
}
