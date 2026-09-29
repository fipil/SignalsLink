using System;
using System.Collections.Generic;
using SignalsLink.src.signals.paperConditions;

namespace SignalsLink.src.signals.manageddock
{
    /// <summary>What running one section came to.</summary>
    public sealed class SectionPassResult
    {
        /// <summary>Moved something, so the rail closes for the sections below.</summary>
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
    /// One tick of a device with sections. They share one action rail (one action per tick across
    /// the whole paper) and one output pin (first output block on the paper wins). A section whose
    /// other party is not there returns null and is passed over.
    /// </summary>
    public static class DockPass
    {
        /// <summary>
        /// <paramref name="runSection"/> gets the section and whether the rail is already closed,
        /// and returns what happened, or null when there was nothing to run against.
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
