using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;

namespace SignalsLink.src.signals.paperConditions
{
    public static class PaperConditionsParser
    {
        // `game:firewood 96+` and, with a slot named, `game:firewood 96+ slot 5`.
        private static readonly Regex inventoryAmountRegex = new Regex("^(?<pattern>@\\S+|\\S*[\\*\\?]\\S*|[A-Za-z0-9_]+:\\S+)\\s+(?<amount>\\d+(?:[\\.,]\\d+)?)(?<mode>[+-]?)(?:\\s+slot\\s+(?<slot>\\d+))?$", RegexOptions.None);

        public static CompiledConditions Parse(string text, List<PaperConditionError> errors = null)
        {
            PaperErrorSink sink = errors != null ? new PaperErrorSink(errors) : null;
            var blocks = new List<ConditionBlock>();
            var sections = new List<ConditionSection>();

            // A paper with no header at all is one section without one, so every device that knows
            // nothing about sections sees exactly what it always saw.
            ConditionSection current = null;
            var orphans = new List<ConditionBlock>();
            var orphanParagraphs = new List<List<PaperLine>>();
            int firstOrphanLine = 0;

            foreach (var p in SplitIntoParagraphs(text))
            {
                // A header written without the blank line under it. Recognised here so that the
                // player is told what to do about it, instead of being told that the header line
                // is a condition nobody understands - which is true and no help at all.
                if (LooksLikeACrowdedHeader(p))
                {
                    errors?.Add(new PaperConditionError(p[0].Number, p[0].Text, "sectionheaderalone"));
                    continue;
                }

                if (TryTakeSectionHeader(p, out ConditionSection header))
                {
                    if (header.WhenError != null) errors?.Add(new PaperConditionError(header.FirstLine, header.Header, "sectionwhenvalue"));

                    // Two sections written with the same header are one section; the second run of
                    // blocks simply continues the first.
                    ConditionSection existing = sections.Find(s =>
                        string.Equals(s.Header, header.Header, StringComparison.OrdinalIgnoreCase));

                    if (existing != null)
                    {
                        current = existing;
                    }
                    else
                    {
                        sections.Add(header);
                        current = header;
                    }

                    // Comments beside the header belong to the section: `# debug` right under it.
                    if (p.Count > 1) current.AddParagraph(p.FindAll(IsComment));

                    continue;
                }

                // Nothing but comments makes no block, but a `# debug` in it belongs where it stands.
                if (p.TrueForAll(IsComment))
                {
                    if (current != null) current.AddParagraph(p); else orphanParagraphs.Add(p);
                    continue;
                }

                var conditions = new List<ScopedCondition>();
                var actions = new List<IConditionAction>();
                byte? outputValue = null;
                bool hasExplicitOutput = false;
                int? sourceSlot = null;
                int? targetSlot = null;
                bool sourceLast = false;
                bool targetLast = false;
                bool targetGround = false;
                bool targetFirepit = false;
                int targetGroundHeight = 1;
                bool requireTargetEmpty = false;
                decimal? amount = null;
                decimal? keep = null;
                AmountMode amountMode = AmountMode.Exactly;
                string recipeCode = null;
                int recipeCount = 1;
                AmountMode recipeMode = AmountMode.Exactly;
                InventoryConditionScope currentScope = InventoryConditionScope.Source;
                int explicitSourceLine = 0;
                string explicitSourceText = null;

                foreach (PaperLine paperLine in p)
                {
                    string line = paperLine.Text;
                    if (sink != null) sink.CurrentLine = paperLine.Number;

                    if (line.StartsWith("#") || line.StartsWith("//")) continue;

                    if (TryParseScopeDirective(line, out InventoryConditionScope parsedScope))
                    {
                        currentScope = parsedScope;

                        if (parsedScope == InventoryConditionScope.Source)
                        {
                            explicitSourceLine = paperLine.Number;
                            explicitSourceText = line;
                        }

                        continue;
                    }

                    if (line.StartsWith("in ", StringComparison.OrdinalIgnoreCase))
                    {
                        sink?.Add(line, "scope");
                        continue;
                    }

                    // Special directive: output N  (N = 0..15) nebo `output .`
                    if (line.StartsWith("output ", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 2 && parts[1] == ".")
                        {
                            outputValue = ConditionBlock.DefaultOutputValue;
                            hasExplicitOutput = true;
                            continue;
                        }

                        // Range widened from 1..14 to 0..15 for the ManagedHose Output anchor
                        // (Signals 0..15). `output 0/15` is not used on the BlockSensor today,
                        // so this does not change existing behavior.
                        if (parts.Length == 2 && byte.TryParse(parts[1], out byte val) && val >= 0 && val <= 15)
                        {
                            outputValue = val;
                            hasExplicitOutput = true;
                            continue;
                        }

                        sink?.Add(line, "output");
                        continue;
                    }

                    if (TryParseSourceDirective(line, out int? parsedSourceSlot, out bool parsedSourceLast))
                    {
                        sourceSlot = parsedSourceSlot;
                        sourceLast = parsedSourceLast;
                        continue;
                    }

                    if (line.StartsWith("source ", StringComparison.OrdinalIgnoreCase))
                    {
                        sink?.Add(line, "source");
                        continue;
                    }

                    if (TryParseTargetDirective(line, out int? parsedTargetSlot, out bool parsedTargetGround, out bool parsedRequireTargetEmpty, out int parsedTargetGroundHeight, out bool parsedTargetFirepit, out bool parsedTargetLast))
                    {
                        targetSlot = parsedTargetSlot;
                        targetGround = parsedTargetGround;
                        targetGroundHeight = parsedTargetGroundHeight;
                        targetFirepit = parsedTargetFirepit;
                        requireTargetEmpty = parsedRequireTargetEmpty;
                        targetLast = parsedTargetLast;
                        continue;
                    }

                    if (line.StartsWith("target ", StringComparison.OrdinalIgnoreCase))
                    {
                        sink?.Add(line, "target");
                        continue;
                    }

                    if (TryParseHeightDirective(line, out int parsedHeight))
                    {
                        targetGroundHeight = parsedHeight;
                        continue;
                    }

                    if (line.StartsWith("height ", StringComparison.OrdinalIgnoreCase))
                    {
                        sink?.Add(line, "height");
                        continue;
                    }

                    if (TryParseAmountDirective(line, out decimal parsedAmount, out AmountMode parsedAmountMode))
                    {
                        amount = parsedAmount;
                        amountMode = parsedAmountMode;
                        continue;
                    }

                    if (line.StartsWith("amount", StringComparison.OrdinalIgnoreCase)
                        && (line.Length == 6 || line[6] == ' '))
                    {
                        sink?.Add(line, "amount");
                        continue;
                    }

                    if (TryParseKeepDirective(line, out decimal parsedKeep))
                    {
                        keep = parsedKeep;
                        continue;
                    }

                    if (line.StartsWith("keep", StringComparison.OrdinalIgnoreCase)
                        && (line.Length == 4 || line[4] == ' '))
                    {
                        sink?.Add(line, "keep");
                        continue;
                    }

                    if (TryParseRecipeDirective(line, out string parsedRecipe, out int parsedRecipeCount, out AmountMode parsedRecipeMode))
                    {
                        recipeCode = parsedRecipe;
                        recipeCount = parsedRecipeCount;
                        recipeMode = parsedRecipeMode;
                        continue;
                    }
                    if (line.StartsWith("recipe", StringComparison.OrdinalIgnoreCase)
                        && (line.Length == 6 || line[6] == ' '))
                    {
                        sink?.Add(line, "recipe");
                        continue;
                    }
                    if (TryParseAction(line, out IConditionAction action))
                    {
                        actions.Add(action);
                        continue;
                    }

                    if (line.StartsWith("do ", StringComparison.OrdinalIgnoreCase))
                    {
                        sink?.Add(line, "action");
                        continue;
                    }

                    conditions.Add(new ScopedCondition(ParseLine(line, sink), currentScope));
                }

                // `in source` in an output block: the output rail runs when nothing is being
                // carried, so there is no source stack for it to be about. It is answered against
                // the target instead - almost certainly what was meant - but the line is a mistake
                // worth naming rather than silently reinterpreting.
                if (hasExplicitOutput && explicitSourceLine > 0)
                {
                    errors?.Add(new PaperConditionError(explicitSourceLine, explicitSourceText, "outputsource"));
                    conditions.Add(new ScopedCondition(FalseCondition.Instance, InventoryConditionScope.Target));
                }

                // A recipe says what to carry and where; the other directives would contradict it.
                if (recipeCode != null && (amount.HasValue || keep.HasValue || sourceSlot.HasValue || sourceLast
                    || targetSlot.HasValue || targetLast || targetGround || targetFirepit || actions.Count > 0 || hasExplicitOutput))
                {
                    errors?.Add(new PaperConditionError(p[0].Number, "", "recipealone"));
                    recipeCode = null;
                }

                // A paragraph of nothing but `output N` is a block too: no conditions means it always
                // holds, so it is the fallback value at the bottom of a paper, or a blanket over the
                // output blocks below it. Directives alone still make no block.
                if (conditions.Count > 0 || actions.Count > 0 || hasExplicitOutput || recipeCode != null)
                {
                    // OutputValue keeps the effective default 15 when `output` is not
                    // specified — for the BlockSensor (no behavior change). HasExplicitOutput
                    // records whether `output` was actually specified; ManagedHose reads it (see spec §6).
                    ConditionBlock block = new ConditionBlock(conditions, outputValue ?? 15, hasExplicitOutput, new PaperConditionDirectives(sourceSlot, targetSlot, targetGround, amount, requireTargetEmpty, targetGroundHeight, targetFirepit, sourceLast, targetLast, amountMode, keep, recipeCode, recipeCount, recipeMode), actions, p[0].Number);
                    blocks.Add(block);

                    if (current != null)
                    {
                        current.Add(block);
                        current.AddParagraph(p);
                    }
                    else
                    {
                        // Held aside: whether these are orphans or a perfectly ordinary paper is
                        // not known until the whole text has been read.
                        orphans.Add(block);
                        orphanParagraphs.Add(p);
                        if (firstOrphanLine == 0) firstOrphanLine = p[0].Number;
                    }
                }
            }

            if (sections.Count == 0)
            {
                // No header anywhere: one implicit section holding everything.
                ConditionSection implicitSection = new ConditionSection("", null, null, null, 0);
                foreach (ConditionBlock block in orphans) implicitSection.Add(block);
                foreach (List<PaperLine> paragraph in orphanParagraphs) implicitSection.AddParagraph(paragraph);
                sections.Add(implicitSection);
            }
            else if (orphans.Count > 0)
            {
                // Blocks above the first header. Reading them as "unload" would be a silent guess
                // about what the player meant, so they are reported and left out.
                errors?.Add(new PaperConditionError(firstOrphanLine, "", "sectionorphan"));
                foreach (ConditionBlock block in orphans) blocks.Remove(block);
            }

            // One gate on the paper means every section needs one: with a gated and an ungated
            // section side by side the Input pin would have to mean two things at once.
            if (sections.Exists(s => s.IsGated))
            {
                ConditionSection ungated = sections.Find(s => !s.IsGated && !s.IsImplicit);
                if (ungated != null) errors?.Add(new PaperConditionError(ungated.FirstLine, ungated.Header, "sectionwhenmixed"));
            }

            // `# debug` above the first header means every section.
            if (sections.Count > 0 && orphanParagraphs.Exists(p => p.Exists(line => ConditionDebug.IsMarked(line.Text))))
            {
                foreach (ConditionSection section in sections) section.MarkTraced();
            }

            return new CompiledConditions(blocks, sections);
        }

        /// <summary>
        /// A section header stands on a paragraph of its own — one line, comments aside. That rule
        /// keeps it apart from a condition line that happens to begin with the same word.
        /// </summary>
        /// <summary>
        /// Does this paragraph begin with a header and then carry on regardless?
        /// </summary>
        private static bool LooksLikeACrowdedHeader(List<PaperLine> paragraph)
        {
            PaperLine? first = null;
            int lines = 0;

            foreach (PaperLine line in paragraph)
            {
                if (line.Text.StartsWith("#") || line.Text.StartsWith("//")) continue;

                first ??= line;
                lines++;
            }

            return lines > 1 && first != null && ConditionSection.TryParseHeader(first.Value.Text, first.Value.Number, out _);
        }

        private static bool TryTakeSectionHeader(List<PaperLine> paragraph, out ConditionSection section)
        {
            section = null;

            PaperLine? only = null;
            foreach (PaperLine line in paragraph)
            {
                if (line.Text.StartsWith("#") || line.Text.StartsWith("//")) continue;
                if (only != null) return false;
                only = line;
            }

            if (only == null) return false;
            return ConditionSection.TryParseHeader(only.Value.Text, only.Value.Number, out section);
        }

        /// <summary>
        /// Splits the paper into blocks on blank lines, keeping the line numbers so that an error
        /// can name the line the player is looking at. Blank and whitespace-only lines separate;
        /// several in a row are one separator.
        /// </summary>
        private static List<List<PaperLine>> SplitIntoParagraphs(string text)
        {
            var paragraphs = new List<List<PaperLine>>();
            var current = new List<PaperLine>();

            string[] lines = text.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].Trim();

                if (trimmed.Length == 0)
                {
                    if (current.Count > 0)
                    {
                        paragraphs.Add(current);
                        current = new List<PaperLine>();
                    }
                    continue;
                }

                current.Add(new PaperLine(i + 1, trimmed));
            }

            if (current.Count > 0) paragraphs.Add(current);
            return paragraphs;
        }

        private static bool IsComment(PaperLine line)
        {
            return line.Text.StartsWith("#") || line.Text.StartsWith("//");
        }

        private static bool TryParseScopeDirective(string line, out InventoryConditionScope scope)
        {
            scope = InventoryConditionScope.Source;

            if (!line.StartsWith("in ", StringComparison.OrdinalIgnoreCase)) return false;

            string value = line.Substring(3).Trim();
            if (value.Equals("source", StringComparison.OrdinalIgnoreCase))
            {
                scope = InventoryConditionScope.Source;
                return true;
            }

            if (value.Equals("target", StringComparison.OrdinalIgnoreCase))
            {
                scope = InventoryConditionScope.Target;
                return true;
            }

            return false;
        }

        private static bool TryParseAction(string line, out IConditionAction action)
        {
            action = null;
            if (!line.StartsWith("do ", StringComparison.OrdinalIgnoreCase)) return false;

            string value = line.Substring(3).Trim();
            if (value.Equals("seal", StringComparison.OrdinalIgnoreCase))
            {
                action = new SealConditionAction();
                return true;
            }

            return false;
        }

        private static bool TryParseSourceDirective(string line, out int? sourceSlot, out bool sourceLast)
        {
            sourceSlot = null;
            sourceLast = false;
            if (!line.StartsWith("source ", StringComparison.OrdinalIgnoreCase)) return false;

            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return false;

            if (parts[1].Equals("last", StringComparison.OrdinalIgnoreCase))
            {
                sourceLast = true;
                return true;
            }

            if (int.TryParse(parts[1], out int parsed) && parsed >= 1)
            {
                sourceSlot = parsed;
                return true;
            }

            return false;
        }

        /// <summary>
        /// <c>height N</c> - how high a column of goods may be stacked.
        ///
        /// It stands on its own rather than hanging off <c>target ground</c>, because with a
        /// section header the ground is already implied and there is nothing to hang it on. Height
        /// is a property of the CARGO, not of the place: logs stack five high, barrels want one
        /// layer so that they can be reached.
        ///
        /// A ceiling, not a promise - ground storage has its own limits per kind of item, so some
        /// things fill up sooner.
        /// </summary>
        private static bool TryParseHeightDirective(string line, out int height)
        {
            height = 1;

            if (!line.StartsWith("height ", StringComparison.OrdinalIgnoreCase)) return false;

            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 2 || !int.TryParse(parts[1], out int parsed) || parsed < 1 || parsed > 255)
            {
                return false;
            }

            height = parsed;
            return true;
        }

        private static bool TryParseTargetDirective(string line, out int? targetSlot, out bool targetGround, out bool requireTargetEmpty, out int targetGroundHeight, out bool targetFirepit, out bool targetLast)
        {
            targetSlot = null;
            targetGround = false;
            requireTargetEmpty = false;
            targetGroundHeight = 1;
            targetFirepit = false;
            targetLast = false;

            if (!line.StartsWith("target ", StringComparison.OrdinalIgnoreCase)) return false;

            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 2 && parts[1].Equals("firepit", StringComparison.OrdinalIgnoreCase))
            {
                targetFirepit = true;
                return true;
            }
            if (parts.Length == 2 && parts[1].Equals("ground", StringComparison.OrdinalIgnoreCase))
            {
                targetGround = true;
                return true;
            }

            // `target ground N` — grow the ground column up to N blocks high (N >= 1).
            if (parts.Length == 3 && parts[1].Equals("ground", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(parts[2], out int parsedHeight) && parsedHeight >= 1 && parsedHeight <= 255)
            {
                targetGround = true;
                targetGroundHeight = parsedHeight;
                return true;
            }

            if (parts.Length == 2 && parts[1].Equals("last", StringComparison.OrdinalIgnoreCase))
            {
                targetLast = true;
                return true;
            }

            if (parts.Length == 3 && parts[1].Equals("last", StringComparison.OrdinalIgnoreCase)
                && parts[2].Equals("ifEmpty", StringComparison.OrdinalIgnoreCase))
            {
                targetLast = true;
                requireTargetEmpty = true;
                return true;
            }

            if (parts.Length == 2 && int.TryParse(parts[1], out int parsedTargetSlot) && parsedTargetSlot >= 1)
            {
                targetSlot = parsedTargetSlot;
                return true;
            }

            if (parts.Length == 3 && int.TryParse(parts[1], out parsedTargetSlot) && parsedTargetSlot >= 1
                && parts[2].Equals("ifEmpty", StringComparison.OrdinalIgnoreCase))
            {
                targetSlot = parsedTargetSlot;
                requireTargetEmpty = true;
                return true;
            }

            return false;
        }

        /// <summary>
        /// <c>amount 10</c>, <c>amount 10-</c>, <c>amount 10+</c>.
        ///
        /// The mark may be written against the number or after a space - somebody will write it
        /// either way, and refusing one of them would teach nobody anything.
        /// </summary>
        /// <summary>`recipe game:paper-parchment`, `recipe game:paper-parchment 2`, `2-`, `2+`; a code without a domain is the game's.</summary>
        private static bool TryParseRecipeDirective(string line, out string code, out int count, out AmountMode mode)
        {
            code = null;
            count = 1;
            mode = AmountMode.Exactly;

            if (!line.StartsWith("recipe ", StringComparison.OrdinalIgnoreCase)) return false;

            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts.Length > 3) return false;
            if (parts[1].IndexOfAny(new[] { '*', '?', '@' }) >= 0) return false;   // one recipe output, not a pattern

            if (parts.Length == 3)
            {
                string number = parts[2];
                if (number.EndsWith("+")) { mode = AmountMode.AtLeast; number = number[..^1]; }
                else if (number.EndsWith("-")) { mode = AmountMode.AtMost; number = number[..^1]; }
                if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out count) || count < 1) return false;
            }

            code = parts[1];
            return true;
        }

        private static bool TryParseAmountDirective(string line, out decimal amount, out AmountMode mode)
        {
            amount = 0;
            mode = AmountMode.Exactly;

            if (!line.StartsWith("amount ", StringComparison.OrdinalIgnoreCase)) return false;

            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts.Length > 3) return false;

            string number = parts[1];
            string mark = parts.Length == 3 ? parts[2] : "";

            if (mark.Length == 0 && number.Length > 1 && (number.EndsWith("+") || number.EndsWith("-")))
            {
                mark = number.Substring(number.Length - 1);
                number = number.Substring(0, number.Length - 1);
            }

            if (mark == "+") mode = AmountMode.AtLeast;
            else if (mark == "-") mode = AmountMode.AtMost;
            else if (mark.Length > 0) return false;

            // Signs are the marks' business, not the number's: with NumberStyles.Number a leading
            // or trailing sign is swallowed as part of the figure, and `amount +5` or `amount 10++`
            // would quietly mean five and ten.
            const NumberStyles plainNumber = NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands;

            return decimal.TryParse(number, plainNumber, CultureInfo.InvariantCulture, out amount) && amount >= 0;
        }

        /// <summary>
        /// `keep N` — the level to hold in the target. No marks: a level is not a batch, so there
        /// is no "at least" or "at most" to say about it.
        /// </summary>
        private static bool TryParseKeepDirective(string line, out decimal keep)
        {
            keep = 0;

            if (!line.StartsWith("keep ", StringComparison.OrdinalIgnoreCase)) return false;

            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return false;

            const NumberStyles plainNumber = NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands;

            return decimal.TryParse(parts[1], plainNumber, CultureInfo.InvariantCulture, out keep) && keep >= 0;
        }

        private static readonly Regex validNameRegex = new Regex("^[A-Za-z0-9_]+$", RegexOptions.None);

        private static bool ContainsWhitespace(string line)
        {
            for (int i = 0; i < line.Length; i++)
            {
                if (char.IsWhiteSpace(line[i])) return true;
            }
            return false;
        }

        private static bool IsValidName(string name)
        {
            return validNameRegex.IsMatch(name);
        }

        private static ICondition ParseLine(string line, PaperErrorSink sink)
        {
            // NOT prefix: !something  -> handled first
            if (line.StartsWith("!"))
            {
                string negated = line.Substring(1).TrimStart();

                // `!isBurning` is answered directly rather than wrapped: NotCondition is a plain
                // condition, so in target scope it would be run per inventory slot instead of once
                // against the block, which is not what the word means.
                if (string.Equals(negated, "isBurning", StringComparison.OrdinalIgnoreCase))
                {
                    return new BlockBurningCondition(false);
                }

                if (TryParseBlockTemperature(negated, true, sink, out ICondition negatedTemperature))
                {
                    return negatedTemperature;
                }

                var inner = ParseLine(negated, sink);
                return new NotCondition(inner);
            }

            if (TryParseInventoryAmountCondition(line, sink, out ICondition inventoryAmountCondition))
            {
                return inventoryAmountCondition;
            }

            // Inventory state: inventoryEmpty / inventoryFilled (filled = "holds something",
            // not "is full"). Must be checked before the bare-attribute fallback below, otherwise
            // these words would be parsed as attribute-exists conditions.
            // Block state: isBurning. Like the two above it has to come before the bare-attribute
            // fallback, or the word would be read as an attribute-exists condition.
            if (string.Equals(line, "isBurning", StringComparison.OrdinalIgnoreCase))
            {
                return new BlockBurningCondition(true);
            }

            if (TryParseBlockTemperature(line, false, sink, out ICondition blockTemperature))
            {
                return blockTemperature;
            }

            if (string.Equals(line, "inventoryEmpty", StringComparison.OrdinalIgnoreCase))
            {
                return new InventoryContentCondition(false);
            }
            if (string.Equals(line, "inventoryFilled", StringComparison.OrdinalIgnoreCase))
            {
                return new InventoryContentCondition(true);
            }

            // Inventory wrapper:
            // inventoryAny <condition>
            if (line.StartsWith("inventoryAny", StringComparison.OrdinalIgnoreCase))
            {
                var rest = line.Substring("inventoryAny".Length);
                if (rest.Length > 0 && char.IsWhiteSpace(rest[0]))
                {
                    string nestedLine = rest.Trim();
                    if (nestedLine.Length > 0)
                    {
                        var nested = ParseLine(nestedLine, sink);
                        return new InventoryAnyCondition(nested);
                    }
                }

                sink?.Add(line, "condition");
                return FalseCondition.Instance;
            }

            // Regex pattern
            if (line.StartsWith("@"))
            {
                return new CodeRegexCondition(new Regex(line.Substring(1), RegexOptions.None));
            }

            // Exact code pattern: domain:path  (no wildcards)
            // Treat it as exact code match (equivalent to regex ^domain:path$)
            if (Regex.IsMatch(line, @"^[A-Za-z0-9_]+:[A-Za-z0-9_\-]+$"))
            {
                return new CodeGlobCondition(line);
            }

            // Glob pattern
            if (line.Contains("*") || line.Contains("?"))
            {
                // A code never contains a space, so a pattern that does can match nothing at all -
                // and since a block ANDs its conditions, one such line kills the whole block in
                // silence. `game:planks *` is the classic: it looks like "planks, any amount", but
                // it is a pattern for a code with a space in it.
                if (ContainsWhitespace(line)) sink?.Add(line, "pattern-space");

                return new CodeGlobCondition(line);
            }

            // Comparison: temperature>1100, isBaked=true, ...
            var m = Regex.Match(line, "^(\\w+)([><=]+)(.+)$");
            if (m.Success)
            {
                string name = m.Groups[1].Value;
                if (!IsValidName(name))
                {
                    sink?.Add(line, "condition");
                    return FalseCondition.Instance;
                }

                return new AttributeComparisonCondition(
                    name,
                    m.Groups[2].Value,
                    m.Groups[3].Value.Trim()
                );
            }

            // Boolean / truthy attribute: isBaked, temperature, etc.
            if (!IsValidName(line))
            {
                sink?.Add(line, "condition");
                return FalseCondition.Instance;
            }

            return new AttributeExistsCondition(line);
        }

        private static readonly Regex blockTemperatureRegex = new Regex(@"^blockTemperature\s*(?<op>>=|<=|==|=|>|<)\s*(?<value>-?\d+(?:[\.,]\d+)?)$", RegexOptions.IgnoreCase);

        /// <summary>`blockTemperature>300`: the block's own heat, not a stack's. The bare word without a comparison is a mistake worth naming.</summary>
        private static bool TryParseBlockTemperature(string line, bool negate, PaperErrorSink sink, out ICondition condition)
        {
            condition = null;
            if (!line.StartsWith("blockTemperature", StringComparison.OrdinalIgnoreCase)) return false;

            Match match = blockTemperatureRegex.Match(line);
            if (!match.Success
                || !double.TryParse(match.Groups["value"].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                sink?.Add(line, "condition");
                condition = FalseCondition.Instance;
                return true;
            }

            condition = new BlockTemperatureCondition(match.Groups["op"].Value, value, negate);
            return true;
        }

        private static bool TryParseInventoryAmountCondition(string line, PaperErrorSink sink, out ICondition condition)
        {
            condition = null;

            Match match = inventoryAmountRegex.Match(line);
            if (!match.Success)
            {
                return false;
            }

            string pattern = match.Groups["pattern"].Value;
            if (!TryParseCodePatternCondition(pattern, out ICondition codeCondition))
            {
                return false;
            }

            string amountText = match.Groups["amount"].Value.Replace(',', '.');
            if (!decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount))
            {
                return false;
            }

            InventoryAmountComparison comparison = match.Groups["mode"].Value switch
            {
                "+" => InventoryAmountComparison.AtLeast,
                "-" => InventoryAmountComparison.AtMost,
                _ => InventoryAmountComparison.Exact
            };

            int? slotNumber = null;
            Group slotGroup = match.Groups["slot"];

            if (slotGroup.Success)
            {
                if (!int.TryParse(slotGroup.Value, out int parsedSlot) || parsedSlot < 1)
                {
                    // Slots are numbered from one everywhere else in a paper; `slot 0` is a
                    // mistake worth naming rather than quietly reading as the first slot.
                    sink?.Add(line, "slot");
                    condition = FalseCondition.Instance;
                    return true;
                }

                slotNumber = parsedSlot;
            }

            condition = new InventoryAmountCondition(codeCondition, amount, comparison, slotNumber);
            return true;
        }

        private static bool TryParseCodePatternCondition(string pattern, out ICondition condition)
        {
            condition = null;

            if (pattern.StartsWith("@"))
            {
                condition = new CodeRegexCondition(new Regex(pattern.Substring(1), RegexOptions.None));
                return true;
            }

            if (Regex.IsMatch(pattern, @"^[A-Za-z0-9_]+:[A-Za-z0-9_\-]+$"))
            {
                condition = new CodeGlobCondition(pattern);
                return true;
            }

            if (pattern.Contains("*") || pattern.Contains("?"))
            {
                condition = new CodeGlobCondition(pattern);
                return true;
            }

            return false;
        }
    }

    public static class PaperConditionsEvaluator
    {
        public static bool Evaluate(string conditionsText, ItemStack stack, IDictionary<string, object> ctx = null)
        {
            var compiled = PaperConditionsParser.Parse(conditionsText);
            return compiled.Evaluate(stack, ctx);
        }
    }
}
