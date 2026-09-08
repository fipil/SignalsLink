using System.Collections.Generic;
using Vintagestory.API.Config;

namespace SignalsLink.src.signals.paperConditions
{
    /// <summary>
    /// One mistake found in a paper, with the line it is on and why it is wrong.
    ///
    /// A paper with a mistake is <b>accepted</b>, never rejected: a single mistyped line must not
    /// stop the whole device while the player is still writing the rest. The bad line simply never
    /// holds — and this is what tells the player which line that was, instead of leaving them to
    /// wonder why nothing happens.
    /// </summary>
    public sealed class PaperConditionError
    {
        /// <summary>1-based line number in the whole paper, as the player sees it.</summary>
        public int Line { get; }

        /// <summary>The offending line itself.</summary>
        public string Text { get; }

        /// <summary>Why it is wrong; a lang key suffix under <c>signalslink:papererror-</c>.</summary>
        public string Reason { get; }

        public PaperConditionError(int line, string text, string reason)
        {
            Line = line;
            Text = text;
            Reason = reason;
        }

        public string Describe()
        {
            return Lang.Get("signalslink:papererror-line", Line, Lang.Get("signalslink:papererror-" + Reason), Text);
        }

        public override string ToString()
        {
            return Line + ": " + Text + " (" + Reason + ")";
        }
    }

    /// <summary>
    /// Collects errors while parsing, remembering which line the parser is on so that the deeper
    /// condition parsing does not have to pass a line number down through every call.
    /// </summary>
    public sealed class PaperErrorSink
    {
        private readonly List<PaperConditionError> errors;

        public PaperErrorSink(List<PaperConditionError> errors)
        {
            this.errors = errors;
        }

        public int CurrentLine { get; set; }

        public void Add(string text, string reason)
        {
            errors?.Add(new PaperConditionError(CurrentLine, text, reason));
        }
    }

    /// <summary>One non-blank line of a paper, with its number.</summary>
    public readonly struct PaperLine
    {
        public int Number { get; }
        public string Text { get; }

        public PaperLine(int number, string text)
        {
            Number = number;
            Text = text;
        }
    }
}
