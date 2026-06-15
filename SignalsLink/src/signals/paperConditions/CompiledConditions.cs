using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace SignalsLink.src.signals.paperConditions
{
    public class CompiledConditions
    {
        private readonly List<ConditionBlock> blocks;

        public CompiledConditions(List<ConditionBlock> blocks)
        {
            this.blocks = blocks;
        }

        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx)
        {
            byte matchedBlockIndex;
            PaperConditionDirectives directives;
            return Evaluate(stack, ctx, out matchedBlockIndex, out directives);
        }

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
}