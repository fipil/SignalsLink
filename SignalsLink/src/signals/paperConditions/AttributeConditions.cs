using System;
using System.Collections.Generic;
using System.Globalization;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace SignalsLink.src.signals.paperConditions
{
    public class AttributeExistsCondition : ICondition
    {
        private readonly string attr;

        public AttributeExistsCondition(string attr)
        {
            this.attr = attr;
        }

        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx)
        {
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
                        return true;
                }
            }

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

            return false;
        }
    }

    public class AttributeComparisonCondition : ICondition
    {
        private readonly string attr;
        private readonly string op;
        private readonly double? numericValue;
        private readonly string stringValue;

        public AttributeComparisonCondition(string attr, string op, string value)
        {
            this.attr = attr;
            this.op = op;

            var trimmed = value.Trim();

            if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                numericValue = 1.0;
            }
            else if (trimmed.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                numericValue = 0.0;
            }
            else if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedValue))
            {
                numericValue = parsedValue;
            }
            else
            {
                stringValue = trimmed;
            }
        }

        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx)
        {
            object contextValue = null;
            if (ctx != null && ctx.TryGetValue(attr, out contextValue) && contextValue is string contextString)
            {
                return (op == "=" || op == "==") && string.Equals(contextString, stringValue, StringComparison.OrdinalIgnoreCase);
            }

            if (stack?.Attributes != null && stack.Attributes[attr] is StringAttribute stringAttribute)
            {
                return (op == "=" || op == "==") && string.Equals(stringAttribute.value, stringValue, StringComparison.OrdinalIgnoreCase);
            }

            if (!numericValue.HasValue) return false;

            double v;

            if (contextValue is IConvertible)
            {
                try
                {
                    v = Convert.ToDouble(contextValue);
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
                else if (a is BoolAttribute ba) v = ba.value ? 1.0 : 0.0;
                else return false;
            }
            else
            {
                return false;
            }

            return op switch
            {
                ">" => v > numericValue.Value,
                ">=" => v >= numericValue.Value,
                "<" => v < numericValue.Value,
                "<=" => v <= numericValue.Value,
                "=" or "==" => Math.Abs(v - numericValue.Value) < 0.0001,
                _ => false
            };
        }
    }
}