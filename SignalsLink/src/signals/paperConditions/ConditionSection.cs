using System;
using System.Collections.Generic;

namespace SignalsLink.src.signals.paperConditions
{
    /// <summary>
    /// A run of blocks under one header, and the layer that makes a device with two ends of its
    /// own possible.
    ///
    /// A header does not add vocabulary — it rebinds what the existing vocabulary points at. Under
    /// <c>unload</c> the source is the other party and the target is the device; under <c>load</c>
    /// it is the other way round. Every condition and every directive then means exactly what it
    /// always meant, which is the whole reason this is a section rather than a pile of new
    /// directives.
    ///
    /// A paper with no header at all compiles to a single implicit section, so the devices that
    /// know nothing about sections cannot tell the difference.
    /// </summary>
    public sealed class ConditionSection
    {
        public const string Unload = "unload";
        public const string Load = "load";

        /// <summary>The long form: <c>from X to Y</c>, where neither end need be the device.</summary>
        public const string From = "from";
        public const string To = "to";

        /// <summary>The header line as written, or empty for the implicit section.</summary>
        public string Header { get; }

        /// <summary>True for the section a paper without headers compiles into.</summary>
        public bool IsImplicit => Header.Length == 0;

        /// <summary><see cref="Unload"/> or <see cref="Load"/>; null for the implicit section.</summary>
        public string Direction { get; }

        /// <summary>
        /// What the goods are taken from, and what they are put into, in the order written.
        ///
        /// The parser deliberately does not interpret either. Which holder they name, and what
        /// <c>north</c> or <c>3</c> mean, is known to the holder finder — so a new kind of vehicle
        /// is a pure addition and the parser is never touched.
        ///
        /// An <b>empty</b> side means the device itself. That is what keeps this seam clean: there
        /// is no <c>dock</c> keyword for the parser to learn, only "nothing said here", which the
        /// device reads as "me".
        /// </summary>
        public IReadOnlyList<string> SourceTokens { get; }

        public IReadOnlyList<string> TargetTokens { get; }

        public bool SourceIsDevice => SourceTokens.Count == 0;
        public bool TargetIsDevice => TargetTokens.Count == 0;

        /// <summary>A header has to name at least one other party; "from me to me" is nothing.</summary>
        public bool NamesAnEnd => !SourceIsDevice || !TargetIsDevice;

        /// <summary>
        /// Is the header finished?
        ///
        /// The long form has to name BOTH ends: someone who writes <c>from train</c> and stops was
        /// on their way to saying where the goods go, and reading it as <c>unload train</c> would be
        /// finishing their sentence for them. The short forms name one end by design.
        /// </summary>
        public bool EndsAreComplete => Direction == From
            ? !SourceIsDevice && !TargetIsDevice
            : NamesAnEnd;

        /// <summary>The line the header stands on, for reporting mistakes.</summary>
        public int FirstLine { get; }

        public IReadOnlyList<ConditionBlock> Blocks => blocks;

        /// <summary>
        /// The section's paragraphs as written, without the header.
        ///
        /// Kept so that a device can hand ONE section to everything that already works on a whole
        /// paper - transfers, directives, actions - without any of it having to learn what a
        /// section is. The alternative was a way of narrowing the block list at every point where
        /// the paper is consulted, which is a seam in a dozen places instead of a string here.
        /// </summary>
        public string Text => text.ToString();

        private readonly List<ConditionBlock> blocks;
        private readonly System.Text.StringBuilder text = new System.Text.StringBuilder();

        public ConditionSection(string header, string direction, IReadOnlyList<string> sourceTokens,
            IReadOnlyList<string> targetTokens, int firstLine)
        {
            Header = header ?? "";
            Direction = direction;
            SourceTokens = sourceTokens ?? Array.Empty<string>();
            TargetTokens = targetTokens ?? Array.Empty<string>();
            FirstLine = firstLine;
            blocks = new List<ConditionBlock>();
        }

        public void Add(ConditionBlock block)
        {
            blocks.Add(block);
        }

        /// <summary>Keeps one paragraph of the section as the player wrote it.</summary>
        public void AddParagraph(IEnumerable<PaperLine> lines)
        {
            if (lines == null) return;

            if (text.Length > 0) text.Append('\n');

            foreach (PaperLine line in lines) text.Append(line.Text).Append('\n');
        }

        /// <summary>
        /// Is this line a section header? Headers stand on a paragraph of their own, so the caller
        /// checks that too before asking.
        /// </summary>
        public static bool IsHeaderLine(string line)
        {
            return TryParseHeader(line, 0, out _);
        }

        public static bool TryParseHeader(string line, int lineNumber, out ConditionSection section)
        {
            section = null;
            if (string.IsNullOrWhiteSpace(line)) return false;

            string[] tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return false;

            if (tokens[0].Equals(From, StringComparison.OrdinalIgnoreCase))
            {
                section = ParseFromTo(line, tokens, lineNumber);
                return true;
            }

            string direction = null;
            List<string> rest = new List<string>();

            foreach (string token in tokens)
            {
                if (direction == null
                    && (token.Equals(Unload, StringComparison.OrdinalIgnoreCase)
                        || token.Equals(Load, StringComparison.OrdinalIgnoreCase)))
                {
                    direction = token.ToLowerInvariant();
                    continue;
                }

                rest.Add(token);
            }

            if (direction == null) return false;

            // The short forms name one end and leave the other empty, which is the device: `unload`
            // brings goods here, `load` takes them away.
            bool unload = direction == Unload;

            section = new ConditionSection(line, direction,
                unload ? rest : (IReadOnlyList<string>)Array.Empty<string>(),
                unload ? Array.Empty<string>() : (IReadOnlyList<string>)rest,
                lineNumber);

            return true;
        }

        /// <summary>
        /// <c>from X to Y</c>. A line that begins with <c>from</c> is taken as a header even when
        /// the rest of it makes no sense — otherwise a missing <c>to</c> would turn every line of
        /// the section into a nonsense condition and bury the one real mistake.
        /// </summary>
        private static ConditionSection ParseFromTo(string line, string[] tokens, int lineNumber)
        {
            List<string> source = new List<string>();
            List<string> target = new List<string>();
            List<string> side = source;

            for (int i = 1; i < tokens.Length; i++)
            {
                if (side == source && tokens[i].Equals(To, StringComparison.OrdinalIgnoreCase))
                {
                    side = target;
                    continue;
                }

                side.Add(tokens[i]);
            }

            return new ConditionSection(line, From, source, target, lineNumber);
        }
    }
}
