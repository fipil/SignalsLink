using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace SignalsLink.src.signals.paperConditions
{
    public static class PaperConditionsParser
    {
        public static CompiledConditions Parse(string text, List<string> errors = null)
        {
            var paragraphs = Regex.Split(text, "\\n\\s*\\n");
            var blocks = new List<ConditionBlock>();

            foreach (var p in paragraphs)
            {
                var lines = new List<ICondition>();
                byte? outputValue = null;
                foreach (var rawLine in p.Split('\n'))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0) continue;
                    if (line.StartsWith("#") || line.StartsWith("//")) continue;

                    // Special directive: output N  (N = 1..14)
                    if (line.StartsWith("output ", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 2 && byte.TryParse(parts[1], out byte val) && val >= 1 && val <= 14)
                        {
                            outputValue = val;
                            continue;
                        }

                        errors?.Add(line);
                        continue;
                    }

                    lines.Add(ParseLine(line, errors));
                }

                if (lines.Count > 0)
                {
                    // Default output value when none specified: 15
                    blocks.Add(new ConditionBlock(lines, outputValue ?? 15));
                }
            }

            return new CompiledConditions(blocks);
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
    }

    public static class PaperConditionsEvaluator
    {
        public static bool Evaluate(string conditionsText, ItemStack stack, IDictionary<string, object> ctx = null)
        {
            var compiled = PaperConditionsParser.Parse(conditionsText);
            return compiled.Evaluate(stack, ctx);
        }
    }


    // ============================================================
    // Compiled representation
    // ============================================================
    public class CompiledConditions
    {
        private readonly List<ConditionBlock> blocks;

        public CompiledConditions(List<ConditionBlock> blocks)
        {
            this.blocks = blocks;
        }

        // Původní signatura – pro starší volání
        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx)
        {
            byte _;
            return Evaluate(stack, ctx, out _);
        }

        // Nová verze s indexem prvního splněného bloku (1-based), 0 = žádný
        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx, out byte matchedBlockIndex)
        {
            matchedBlockIndex = 0;

            for (int i = 0; i < blocks.Count; i++)
            {
                if (blocks[i].Evaluate(stack, ctx))
                {
                    // Use block's configured output value (1..14) or default (15)
                    matchedBlockIndex = blocks[i].OutputValue;
                    return true;
                }
            }

            return false;
        }
    }

    public class ConditionBlock
    {
        private readonly List<ICondition> conditions;

        public byte OutputValue { get; }

        public ConditionBlock(List<ICondition> conditions, byte outputValue)
        {
            this.conditions = conditions;
            OutputValue = outputValue;
        }

        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx)
        {
            foreach (var c in conditions)
            {
                if (!c.Evaluate(stack, ctx)) return false;
            }
            return true;
        }
    }


    // ============================================================
    // Conditions
    // ============================================================
    public interface ICondition
    {
        bool Evaluate(ItemStack stack, IDictionary<string, object> ctx);
    }

    public class CodeGlobCondition : ICondition
    {
        private readonly Regex regex;

        public CodeGlobCondition(string glob)
        {
            var pattern = "^" + Regex.Escape(glob).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            regex = new Regex(pattern, RegexOptions.Compiled);
        }

        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx)
        {
            var code = stack?.Collectible?.Code?.ToString();
            if (string.IsNullOrEmpty(code)) return false;
            return regex.IsMatch(code);
        }
    }

    public class CodeRegexCondition : ICondition
    {
        private readonly Regex regex;
        public CodeRegexCondition(Regex regex) { this.regex = regex; }

        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx)
        {
            var code = stack?.Collectible?.Code?.ToString();
            if (string.IsNullOrEmpty(code)) return false;
            return regex.IsMatch(code);
        }
    }

    public class AttributeExistsCondition : ICondition
    {
        private readonly string attr;
        public AttributeExistsCondition(string attr) { this.attr = attr; }

        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx)
        {
            // 1) Nejprve stack.Attributes
            if (stack.Attributes != null && stack.Attributes.HasAttribute(attr))
            {
                var a = stack.Attributes[attr];

                switch (a)
                {
                    case IntAttribute ia:
                        return ia.value != 0;
                    case LongAttribute la:
                        return la.value != 0L;
                    case FloatAttribute fa:
                        return Math.Abs(fa.value) > float.Epsilon;
                    case DoubleAttribute da:
                        return Math.Abs(da.value) > double.Epsilon;
                    case StringAttribute sa:
                        return !string.IsNullOrEmpty(sa.value);
                    case BoolAttribute ba:
                        return ba.value;
                    default:
                        // Neznámý typ, ale existuje → považuj za true
                        return true;
                }
            }

            // 2) Fallback na ctx (virtuální hodnoty)
            if (ctx != null && ctx.TryGetValue(attr, out var obj))
            {
                if (obj == null) return false;

                switch (obj)
                {
                    case int i: return i != 0;
                    case long l: return l != 0L;
                    case float f: return Math.Abs(f) > float.Epsilon;
                    case double d: return Math.Abs(d) > double.Epsilon;
                    case bool b: return b;
                    case string s: return !string.IsNullOrEmpty(s);
                    default:
                        return true;
                }
            }

            // 3) Atribut vůbec neexistuje → false
            return false;
        }
    }

    public class AttributeComparisonCondition : ICondition
    {
        private readonly string attr;
        private readonly string op;
        private readonly double value;

        public AttributeComparisonCondition(string attr, string op, string value)
        {
            this.attr = attr;
            this.op = op;

            // Podpora textových booleanů a čísel
            var trimmed = value.Trim();

            if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                this.value = 1.0;
            }
            else if (trimmed.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                this.value = 0.0;
            }
            else
            {
                this.value = double.Parse(trimmed);
            }
        }

        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx)
        {
            double v;

            // 1) Zkusit stack.Attributes
            // 2) Fallback na ctx (virtuální hodnoty typu temperature, durability, atd.)
            if (ctx != null && ctx.TryGetValue(attr, out var obj) && obj is IConvertible)
            {
                try
                {
                    v = Convert.ToDouble(obj);
                }
                catch
                {
                    return false;
                }
            }
            else if (stack?.Attributes != null && stack.Attributes.HasAttribute(attr))
            {
                var a = stack.Attributes[attr];

                if (a is FloatAttribute fa) v = fa.value;
                else if (a is DoubleAttribute da) v = da.value;
                else if (a is IntAttribute ia) v = ia.value;
                else if (a is BoolAttribute ba) v = ba.value ? 1.0 : 0.0; // bool -> 0/1
                else return false;
            }

            else
            {
                return false;
            }

            return op switch
            {
                ">" => v > value,
                ">=" => v >= value,
                "<" => v < value,
                "<=" => v <= value,
                "=" or "==" => Math.Abs(v - value) < 0.0001,
                _ => false
            };
        }
    }

    public class NotCondition : ICondition
    {
        private readonly ICondition inner;

        public NotCondition(ICondition inner)
        {
            this.inner = inner;
        }

        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx)
        {
            return !inner.Evaluate(stack, ctx);
        }
    }

    public class FalseCondition : ICondition
    {
        public static readonly FalseCondition Instance = new FalseCondition();

        private FalseCondition() { }

        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx)
        {
            return false;
        }
    }
}
