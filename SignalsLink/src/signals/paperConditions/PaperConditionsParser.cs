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
        public static CompiledConditions Parse(string text)
        {
            var paragraphs = Regex.Split(text, "\\n\\s*\\n");
            var blocks = new List<ConditionBlock>();

            foreach (var p in paragraphs)
            {
                var lines = new List<ICondition>();
                foreach (var rawLine in p.Split('\n'))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0) continue;
                    if (line.StartsWith("#") || line.StartsWith("//")) continue;

                    lines.Add(ParseLine(line));
                }

                if (lines.Count > 0)
                    blocks.Add(new ConditionBlock(lines));
            }

            return new CompiledConditions(blocks);
        }

        private static ICondition ParseLine(string line)
        {
            // Regex pattern
            if (line.StartsWith("@"))
            {
                return new CodeRegexCondition(new Regex(line.Substring(1), RegexOptions.Compiled));
            }

            // Glob pattern
            if (line.Contains("*") || line.Contains("?"))
            {
                return new CodeGlobCondition(line);
            }

            // Comparison: temperature>1100
            var m = Regex.Match(line, "^(\\w+)([><=]+)(.+)$");
            if (m.Success)
            {
                return new AttributeComparisonCondition(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);
            }

            // Boolean attribute: isBaked
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

        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx)
        {
            foreach (var block in blocks)
            {
                if (block.Evaluate(stack, ctx)) return true;
            }
            return false;
        }
    }

    public class ConditionBlock
    {
        private readonly List<ICondition> conditions;

        public ConditionBlock(List<ICondition> conditions)
        {
            this.conditions = conditions;
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
            return regex.IsMatch(stack.Collectible.Code.ToString());
        }
    }

    public class CodeRegexCondition : ICondition
    {
        private readonly Regex regex;
        public CodeRegexCondition(Regex regex) { this.regex = regex; }
        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx)
        {
            return regex.IsMatch(stack.Collectible.Code.ToString());
        }
    }

    public class AttributeExistsCondition : ICondition
    {
        private readonly string attr;
        public AttributeExistsCondition(string attr) { this.attr = attr; }

        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx)
        {
            return stack.Attributes?.HasAttribute(attr) == true;
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
            this.value = double.Parse(value);
        }

        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx)
        {
            double v;

            // 1) Zkusit stack.Attributes
            if (stack.Attributes != null && stack.Attributes.HasAttribute(attr))
            {
                var a = stack.Attributes[attr];

                if (a is FloatAttribute fa) v = fa.value;
                else if (a is DoubleAttribute da) v = da.value;
                else if (a is IntAttribute ia) v = ia.value;
                else return false;
            }
            // 2) Fallback na ctx (virtuální hodnoty typu temperature, atd.)
            else if (ctx != null && ctx.TryGetValue(attr, out var obj) && obj is IConvertible)
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
}
