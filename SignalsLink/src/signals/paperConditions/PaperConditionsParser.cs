using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

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
                byte? targetSlot = null;
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

                    // Special directive: output N  (N = 1..14)
                    if (line.StartsWith("output ", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 2 && parts[1] == ".")
                        {
                            outputValue = ConditionBlock.DefaultOutputValue;
                            continue;
                        }

                        if (parts.Length == 2 && byte.TryParse(parts[1], out byte val) && val >= 1 && val <= 14)
                        {
                            outputValue = val;
                            continue;
                        }

                        errors?.Add(line);
                        continue;
                    }

                    if (TryParseTargetDirective(line, out byte parsedTargetSlot, out bool parsedRequireTargetEmpty))
                    {
                        targetSlot = parsedTargetSlot;
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
                    // Default output value when none specified: 15
                    blocks.Add(new ConditionBlock(conditions, outputValue ?? 15, new PaperConditionDirectives(targetSlot, amount, requireTargetEmpty), actions));
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

        private static bool TryParseTargetDirective(string line, out byte targetSlot, out bool requireTargetEmpty)
        {
            targetSlot = 0;
            requireTargetEmpty = false;

            if (!line.StartsWith("target ", StringComparison.OrdinalIgnoreCase)) return false;

            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && byte.TryParse(parts[1], out targetSlot) && targetSlot >= 1 && targetSlot <= 14)
            {
                return true;
            }

            if (parts.Length == 3 && byte.TryParse(parts[1], out targetSlot) && targetSlot >= 1 && targetSlot <= 14 && parts[2].Equals("ifEmpty", StringComparison.OrdinalIgnoreCase))
            {
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
            byte matchedBlockIndex;
            PaperConditionDirectives directives;
            return Evaluate(stack, ctx, out matchedBlockIndex, out directives);
        }

        // Nová verze s indexem prvního splněného bloku (1-based), 0 = žádný
        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx, out byte matchedBlockIndex)
        {
            return Evaluate(stack, ctx, out matchedBlockIndex, out _);
        }

        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx, out byte matchedBlockIndex, out PaperConditionDirectives directives)
        {
            PaperConditionMatchResult matchResult;
            bool matched = TryMatch(stack, ctx, out matchResult);
            matchedBlockIndex = 0;
            directives = PaperConditionDirectives.Empty;

            if (matched)
            {
                matchedBlockIndex = matchResult.OutputValue;
                directives = matchResult.Directives;
            }

            return matched;
        }

        public bool TryMatch(ItemStack stack, IDictionary<string, object> ctx, out PaperConditionMatchResult matchResult)
        {
            for (int i = 0; i < blocks.Count; i++)
            {
                if (blocks[i].TryMatch(stack, ctx))
                {
                    matchResult = blocks[i].CreateMatchResult();
                    return true;
                }
            }

            matchResult = PaperConditionMatchResult.NoMatch;
            return false;
        }

        public IReadOnlyList<IConditionAction> GetMatchingActions(ItemStack stack, IDictionary<string, object> ctx)
        {
            List<IConditionAction> actions = null;

            for (int i = 0; i < blocks.Count; i++)
            {
                if (!blocks[i].HasActions) continue;
                if (!blocks[i].MatchesActionContext(stack, ctx)) continue;

                actions ??= new List<IConditionAction>();
                actions.AddRange(blocks[i].Actions);
            }

            return actions != null ? actions : Array.Empty<IConditionAction>();
        }
    }

    public class ConditionBlock
    {
        private readonly List<ScopedCondition> conditions;
        private readonly List<IConditionAction> actions;

        public const byte DefaultOutputValue = byte.MaxValue;

        public byte OutputValue { get; }
        public PaperConditionDirectives Directives { get; }
        public IReadOnlyList<IConditionAction> Actions => actions;
        public bool HasActions => actions.Count > 0;
        public bool CanSelectSource => conditions.Any(condition => condition.Scope == InventoryConditionScope.Source);

        public ConditionBlock(List<ScopedCondition> conditions, byte outputValue, PaperConditionDirectives directives, List<IConditionAction> actions)
        {
            this.conditions = conditions ?? new List<ScopedCondition>();
            OutputValue = outputValue;
            Directives = directives ?? PaperConditionDirectives.Empty;
            this.actions = actions ?? new List<IConditionAction>();
        }

        public bool TryMatch(ItemStack stack, IDictionary<string, object> ctx)
        {
            if (!CanSelectSource) return false;

            foreach (var c in conditions)
            {
                if (!c.Evaluate(stack, ctx, true)) return false;
            }

            return true;
        }

        public bool MatchesActionContext(ItemStack stack, IDictionary<string, object> ctx)
        {
            foreach (var c in conditions)
            {
                if (!c.Evaluate(stack, ctx, false)) return false;
            }

            return true;
        }

        public PaperConditionMatchResult CreateMatchResult()
        {
            return new PaperConditionMatchResult(OutputValue, Directives, actions);
        }
    }

    public enum InventoryConditionScope
    {
        Source,
        Target
    }

    public sealed class ScopedCondition
    {
        public ICondition Condition { get; }
        public InventoryConditionScope Scope { get; }

        public ScopedCondition(ICondition condition, InventoryConditionScope scope)
        {
            Condition = condition ?? FalseCondition.Instance;
            Scope = scope;
        }

        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx, bool isSelectionEvaluation)
        {
            IInventory inventory = ResolveInventory(ctx);
            IDictionary<string, object> scopedCtx = BuildScopedContext(ctx, inventory);

            if (Condition is IInventoryCondition inventoryCondition)
            {
                return inventoryCondition.Evaluate(stack, inventory, scopedCtx, Scope, isSelectionEvaluation);
            }

            if (isSelectionEvaluation && Scope == InventoryConditionScope.Source && stack?.Collectible != null)
            {
                return Condition.Evaluate(stack, scopedCtx);
            }

            return InventoryConditionResolver.AnyMatch(inventory, scopedCtx, Condition);
        }

        private IInventory ResolveInventory(IDictionary<string, object> ctx)
        {
            string key = Scope == InventoryConditionScope.Target ? "targetInventory" : "sourceInventory";
            if (ctx != null && ctx.TryGetValue(key, out var obj) && obj is IInventory inventory)
            {
                return inventory;
            }

            if (ctx != null && ctx.TryGetValue("inventory", out obj) && obj is IInventory fallbackInventory)
            {
                return fallbackInventory;
            }

            return null;
        }

        private static IDictionary<string, object> BuildScopedContext(IDictionary<string, object> ctx, IInventory inventory)
        {
            var scopedCtx = ctx != null
                ? new Dictionary<string, object>(ctx)
                : new Dictionary<string, object>();

            if (inventory != null)
            {
                scopedCtx["inventory"] = inventory;
            }

            return scopedCtx;
        }
    }

    public sealed class PaperConditionMatchResult
    {
        public static readonly PaperConditionMatchResult NoMatch = new PaperConditionMatchResult(0, PaperConditionDirectives.Empty, Array.Empty<IConditionAction>());

        public byte OutputValue { get; }
        public PaperConditionDirectives Directives { get; }
        public IReadOnlyList<IConditionAction> Actions { get; }

        public PaperConditionMatchResult(byte outputValue, PaperConditionDirectives directives, IReadOnlyList<IConditionAction> actions)
        {
            OutputValue = outputValue;
            Directives = directives ?? PaperConditionDirectives.Empty;
            Actions = actions ?? (IReadOnlyList<IConditionAction>)Array.Empty<IConditionAction>();
        }
    }

    public interface IConditionAction
    {
        bool Execute(IDictionary<string, object> ctx);
    }

    public sealed class SealConditionAction : IConditionAction
    {
        public bool Execute(IDictionary<string, object> ctx)
        {
            if (ctx == null) return false;
            if (!ctx.TryGetValue("targetBlockEntity", out var obj) || obj is not BlockEntityBarrel barrel) return false;
            if (barrel.Sealed) return false;

            barrel.SealBarrel();
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

    public interface IInventoryCondition : ICondition
    {
        bool Evaluate(ItemStack stack, IInventory inventory, IDictionary<string, object> ctx, InventoryConditionScope scope, bool isSelectionEvaluation);
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
            foreach (string code in ConditionCodeHelper.GetCodes(stack))
            {
                if (regex.IsMatch(code)) return true;
            }

            return false;
        }
    }

    public class CodeRegexCondition : ICondition
    {
        private readonly Regex regex;
        public CodeRegexCondition(Regex regex) { this.regex = regex; }

        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx)
        {
            foreach (string code in ConditionCodeHelper.GetCodes(stack))
            {
                if (regex.IsMatch(code)) return true;
            }

            return false;
        }
    }

    public static class ConditionCodeHelper
    {
        public static IEnumerable<string> GetCodes(ItemStack stack)
        {
            if (stack?.Collectible?.Code == null) yield break;

            string collectibleCode = stack.Collectible.Code.ToString();
            if (!string.IsNullOrEmpty(collectibleCode))
            {
                yield return collectibleCode;
            }

            foreach (string spilledCode in GetLiquidSpilledCodes(stack))
            {
                if (!string.IsNullOrEmpty(spilledCode) && !string.Equals(spilledCode, collectibleCode, StringComparison.Ordinal))
                {
                    yield return spilledCode;
                }
            }
        }

        private static IEnumerable<string> GetLiquidSpilledCodes(ItemStack stack)
        {
            var whenSpilled = stack?.ItemAttributes?["waterTightContainerProps"]?["whenSpilled"];
            if (whenSpilled == null) yield break;

            string stackCode = whenSpilled["stack"]?["code"].AsString(null);
            if (!string.IsNullOrEmpty(stackCode))
            {
                yield return stackCode;
            }

            Dictionary<string, JsonItemStack> stackByFillLevel = whenSpilled["stackByFillLevel"]?.AsObject<Dictionary<string, JsonItemStack>>(null);
            if (stackByFillLevel == null) yield break;

            foreach (JsonItemStack jsonStack in stackByFillLevel.Values)
            {
                string fillLevelCode = jsonStack?.Code?.ToString();
                if (!string.IsNullOrEmpty(fillLevelCode))
                {
                    yield return fillLevelCode;
                }
            }
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

    public sealed class InventoryAmountCondition : IInventoryCondition
    {
        private readonly ICondition innerCondition;
        private readonly decimal expectedAmount;
        private readonly InventoryAmountComparison comparison;

        public InventoryAmountCondition(ICondition innerCondition, decimal expectedAmount, InventoryAmountComparison comparison)
        {
            this.innerCondition = innerCondition ?? FalseCondition.Instance;
            this.expectedAmount = expectedAmount;
            this.comparison = comparison;
        }

        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx)
        {
            return innerCondition.Evaluate(stack, ctx);
        }

        public bool Evaluate(ItemStack stack, IInventory inventory, IDictionary<string, object> ctx, InventoryConditionScope scope, bool isSelectionEvaluation)
        {
            if (inventory == null) return false;

            if (isSelectionEvaluation && scope == InventoryConditionScope.Source)
            {
                if (stack?.Collectible == null) return false;
                if (!innerCondition.Evaluate(stack, ctx)) return false;
            }

            decimal actualAmount = InventoryConditionResolver.GetMatchingAmount(inventory, ctx, innerCondition);

            return comparison switch
            {
                InventoryAmountComparison.AtLeast => actualAmount >= expectedAmount,
                InventoryAmountComparison.AtMost => actualAmount <= expectedAmount,
                _ => actualAmount == expectedAmount
            };
        }
    }

    public enum InventoryAmountComparison
    {
        Exact,
        AtLeast,
        AtMost
    }

    public static class InventoryConditionResolver
    {
        public static bool AnyMatch(IInventory inventory, IDictionary<string, object> ctx, ICondition condition)
        {
            if (inventory == null || condition == null) return false;

            foreach (var slot in inventory)
            {
                if (slot?.Empty != false) continue;

                ItemStack slotStack = slot.Itemstack;
                if (slotStack?.Collectible == null) continue;

                if (condition.Evaluate(slotStack, ctx))
                {
                    return true;
                }

                if (slotStack.Block is BlockLiquidContainerBase liquidContainer)
                {
                    ItemStack contentStack = liquidContainer.GetContent(slotStack);
                    if (contentStack?.Collectible != null && condition.Evaluate(contentStack, ctx))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static decimal GetMatchingAmount(IInventory inventory, IDictionary<string, object> ctx, ICondition condition)
        {
            if (inventory == null || condition == null) return 0;

            decimal totalAmount = 0;

            foreach (var slot in inventory)
            {
                if (slot?.Empty != false) continue;

                ItemStack slotStack = slot.Itemstack;
                if (slotStack?.Collectible == null) continue;

                if (condition.Evaluate(slotStack, ctx))
                {
                    totalAmount += GetStackAmount(slotStack);
                    continue;
                }

                if (slotStack.Block is BlockLiquidContainerBase liquidContainer)
                {
                    ItemStack contentStack = liquidContainer.GetContent(slotStack);
                    if (contentStack?.Collectible != null && condition.Evaluate(contentStack, ctx))
                    {
                        totalAmount += GetStackAmount(contentStack);
                    }
                }
            }

            return totalAmount;
        }

        private static decimal GetStackAmount(ItemStack stack)
        {
            var props = BlockLiquidContainerBase.GetContainableProps(stack);
            if (props != null && props.ItemsPerLitre > 0)
            {
                return decimal.Round(stack.StackSize / (decimal)props.ItemsPerLitre, 2, MidpointRounding.ToZero);
            }

            return stack?.StackSize ?? 0;
        }
    }

    public class InventoryAnyCondition : ICondition
    {
        private readonly ICondition inner;

        public InventoryAnyCondition(ICondition inner)
        {
            this.inner = inner ?? FalseCondition.Instance;
        }

        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx)
        {
            if (ctx == null)
            {
                return false;
            }

            if (!ctx.TryGetValue("inventory", out var obj) || obj is not IInventory inventory)
            {
                return false;
            }

            foreach (var slot in inventory)
            {
                if (slot?.Empty != false)
                {
                    continue;
                }

                var slotStack = slot.Itemstack;
                if (slotStack?.Collectible == null)
                {
                    continue;
                }

                if (inner.Evaluate(slotStack, ctx))
                {
                    return true;
                }

                if (slotStack.Block is BlockLiquidContainerBase liquidContainer)
                {
                    ItemStack contentStack = liquidContainer.GetContent(slotStack);
                    if (contentStack?.Collectible != null && inner.Evaluate(contentStack, ctx))
                    {
                        return true;
                    }
                }

                if (slotStack.Collectible is ILiquidInterface && inner.Evaluate(slotStack, ctx))
                {
                    return true;
                }
            }

            return false;
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
