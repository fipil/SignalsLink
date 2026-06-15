using System;
using System.Collections.Generic;
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
        private readonly double value;

        public AttributeComparisonCondition(string attr, string op, string value)
        {
            this.attr = attr;
            this.op = op;

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
                else if (a is BoolAttribute ba) v = ba.value ? 1.0 : 0.0;
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
}