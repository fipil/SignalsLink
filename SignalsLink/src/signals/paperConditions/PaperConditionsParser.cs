using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;

namespace SignalsLink.src.signals.paperConditions
{
    public static class PaperConditionsParser
    {
        private static readonly Regex inventoryAmountRegex = new Regex("^(?<pattern>@\\S+|\\S*[\\*\\?]\\S*|[A-Za-z0-9_]+:\\S+)\\s+(?<amount>\\d+(?:[\\.,]\\d+)?)(?<mode>[+-]?)$", RegexOptions.Compiled);

        public static CompiledConditions Parse(string text, List<string> errors = null)
        {
            var paragraphs = Regex.Split(text, "\\n\\s*\\n");
            var blocks = new List<ConditionBlock>();

            foreach (var p in paragraphs)
            {
                var conditions = new List<ScopedCondition>();
                var actions = new List<IConditionAction>();
                byte? outputValue = null;
                bool hasExplicitOutput = false;
                byte? targetSlot = null;
                bool targetGround = false;
                bool requireTargetEmpty = false;
                decimal? amount = null;
                InventoryConditionScope currentScope = InventoryConditionScope.Source;

                foreach (var rawLine in p.Split('\n'))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0) continue;
                    if (line.StartsWith("#") || line.StartsWith("//")) continue;

                    if (TryParseScopeDirective(line, out InventoryConditionScope parsedScope))
                    {
                        currentScope = parsedScope;
                        continue;
                    }

                    if (line.StartsWith("in ", StringComparison.OrdinalIgnoreCase))
                    {
                        errors?.Add(line);
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

                        errors?.Add(line);
                        continue;
                    }

                    if (TryParseTargetDirective(line, out byte? parsedTargetSlot, out bool parsedTargetGround, out bool parsedRequireTargetEmpty))
                    {
                        targetSlot = parsedTargetSlot;
                        targetGround = parsedTargetGround;
                        requireTargetEmpty = parsedRequireTargetEmpty;
                        continue;
                    }

                    if (line.StartsWith("target ", StringComparison.OrdinalIgnoreCase))
                    {
                        errors?.Add(line);
                        continue;
                    }

                    if (TryParseAmountDirective(line, out decimal parsedAmount))
                    {
                        amount = parsedAmount;
                        continue;
                    }

                    if (line.StartsWith("amount ", StringComparison.OrdinalIgnoreCase))
                    {
                        errors?.Add(line);
                        continue;
                    }

                    if (TryParseAction(line, out IConditionAction action))
                    {
                        actions.Add(action);
                        continue;
                    }

                    if (line.StartsWith("do ", StringComparison.OrdinalIgnoreCase))
                    {
                        errors?.Add(line);
                        continue;
                    }

                    conditions.Add(new ScopedCondition(ParseLine(line, errors), currentScope));
                }

                if (conditions.Count > 0 || actions.Count > 0)
                {
                    // OutputValue keeps the effective default 15 when `output` is not
                    // specified — for the BlockSensor (no behavior change). HasExplicitOutput
                    // records whether `output` was actually specified; ManagedHose reads it (see spec §6).
                    blocks.Add(new ConditionBlock(conditions, outputValue ?? 15, hasExplicitOutput, new PaperConditionDirectives(targetSlot, targetGround, amount, requireTargetEmpty), actions));
                }
            }

            return new CompiledConditions(blocks);
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

        private static bool TryParseTargetDirective(string line, out byte? targetSlot, out bool targetGround, out bool requireTargetEmpty)
        {
            targetSlot = null;
            targetGround = false;
            requireTargetEmpty = false;

            if (!line.StartsWith("target ", StringComparison.OrdinalIgnoreCase)) return false;

            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[1].Equals("ground", StringComparison.OrdinalIgnoreCase))
            {
                targetGround = true;
                return true;
            }

            if (parts.Length == 2 && byte.TryParse(parts[1], out byte parsedTargetSlot) && parsedTargetSlot >= 1 && parsedTargetSlot <= 14)
            {
                targetSlot = parsedTargetSlot;
                return true;
            }

            if (parts.Length == 3 && byte.TryParse(parts[1], out parsedTargetSlot) && parsedTargetSlot >= 1 && parsedTargetSlot <= 14 && parts[2].Equals("ifEmpty", StringComparison.OrdinalIgnoreCase))
            {
                targetSlot = parsedTargetSlot;
                requireTargetEmpty = true;
                return true;
            }

            return false;
        }

        private static bool TryParseAmountDirective(string line, out decimal amount)
        {
            amount = 0;

            if (!line.StartsWith("amount ", StringComparison.OrdinalIgnoreCase)) return false;

            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 2 && decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
        }

        private static readonly Regex validNameRegex = new Regex("^[A-Za-z0-9_]+$", RegexOptions.Compiled);

        private static bool IsValidName(string name)
        {
            return validNameRegex.IsMatch(name);
        }

        private static ICondition ParseLine(string line, List<string> errors)
        {
            // NOT prefix: !something  -> handled first
            if (line.StartsWith("!"))
            {
                var inner = ParseLine(line.Substring(1).TrimStart(), errors);
                return new NotCondition(inner);
            }

            if (TryParseInventoryAmountCondition(line, out ICondition inventoryAmountCondition))
            {
                return inventoryAmountCondition;
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
                        var nested = ParseLine(nestedLine, errors);
                        return new InventoryAnyCondition(nested);
                    }
                }

                errors?.Add(line);
                return FalseCondition.Instance;
            }

            // Regex pattern
            if (line.StartsWith("@"))
            {
                return new CodeRegexCondition(new Regex(line.Substring(1), RegexOptions.Compiled));
            }

            // Exact code pattern: domain:path  (no wildcards)
            // Treat it as exact code match (equivalent to regex ^domain:path$)
            if (Regex.IsMatch(line, @"^[A-Za-z0-9_]+:[A-Za-z0-9_\-]+$"))
            {
                // Build a regex that matches this code exactly
                string pattern = "^" + Regex.Escape(line) + "$";
                return new CodeRegexCondition(new Regex(pattern, RegexOptions.Compiled));
            }

            // Glob pattern
            if (line.Contains("*") || line.Contains("?"))
            {
                return new CodeGlobCondition(line);
            }

            // Comparison: temperature>1100, isBaked=true, ...
            var m = Regex.Match(line, "^(\\w+)([><=]+)(.+)$");
            if (m.Success)
            {
                string name = m.Groups[1].Value;
                if (!IsValidName(name))
                {
                    errors?.Add(line);
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
                errors?.Add(line);
                return FalseCondition.Instance;
            }

            return new AttributeExistsCondition(line);
        }

        private static bool TryParseInventoryAmountCondition(string line, out ICondition condition)
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

            condition = new InventoryAmountCondition(codeCondition, amount, comparison);
            return true;
        }

        private static bool TryParseCodePatternCondition(string pattern, out ICondition condition)
        {
            condition = null;

            if (pattern.StartsWith("@"))
            {
                condition = new CodeRegexCondition(new Regex(pattern.Substring(1), RegexOptions.Compiled));
                return true;
            }

            if (Regex.IsMatch(pattern, @"^[A-Za-z0-9_]+:[A-Za-z0-9_\-]+$"))
            {
                string exactPattern = "^" + Regex.Escape(pattern) + "$";
                condition = new CodeRegexCondition(new Regex(exactPattern, RegexOptions.Compiled));
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
