using System;
using System.Collections.Generic;
using SignalsLink.src.signals.paperConditions;

namespace SignalsLink.src.signals.manageddock
{
    /// <summary>What running one section came to.</summary>
    public sealed class SectionPassResult
    {
        /// <summary>The section actually moved something. Closes the action rail for the rest.</summary>
        public bool ActionPerformed { get; }

        /// <summary>An <c>output</c> block in this section held.</summary>
        public bool HasOutput { get; }

        public byte Output { get; }

        public SectionPassResult(bool actionPerformed, bool hasOutput, byte output)
        {
            ActionPerformed = actionPerformed;
            HasOutput = hasOutput;
            Output = output;
        }

        public static readonly SectionPassResult Nothing = new SectionPassResult(false, false, 0);
    }

    /// <summary>
    /// One tick of a device with sections: the sections in the order they are written, sharing one
    /// action rail and one output pin between them.
    ///
    /// Both of those are what makes this its own thing rather than a loop.
    ///
    /// <b>One action per tick, across the whole paper.</b> `actionsBlocked` does not restart at
    /// each header - a section that moved something closes the rail for the ones below it, exactly
    /// as an action block closes it for the blocks below it. Otherwise a paper with three headers
    /// would quietly do three times the work of one with none.
    ///
    /// <b>One pin, and the first output block on the paper wins.</b> Not the last, and not the last
    /// section to have one: the pin has to read the same way whether the player wrote their output
    /// blocks in one section or spread them over several.
    ///
    /// A section whose other party is not there returns nothing and is simply passed over - a train
    /// that has not arrived must not stop the yard section below it from working.
    /// </summary>
    public static class DockPass
    {
        /// <summary>
        /// Runs the sections. <paramref name="runSection"/> is given the section and whether the
        /// action rail is already closed, and returns what happened - or null when there was
        /// nothing to run against.
        /// </summary>
        public static byte Run(IReadOnlyList<ConditionSection> sections,
            Func<ConditionSection, bool, SectionPassResult> runSection,
            out bool actionPerformed)
        {
            actionPerformed = false;
            byte output = 0;
            bool outputClaimed = false;

            if (sections == null || runSection == null) return 0;

            foreach (ConditionSection section in sections)
            {
                if (section.Blocks.Count == 0) continue;

                SectionPassResult result = runSection(section, actionPerformed);
                if (result == null) continue;

                if (result.HasOutput && !outputClaimed)
                {
                    output = result.Output;
                    outputClaimed = true;
                }

                if (result.ActionPerformed) actionPerformed = true;
            }

            return output;
        }
    }
}
