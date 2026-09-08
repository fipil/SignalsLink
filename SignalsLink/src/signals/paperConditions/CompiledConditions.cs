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

        /// <summary>
        /// The blocks in the order they stand on the paper. This is what the unified
        /// <see cref="ConditionDriver"/> walks; everything else here is the older per-entry-point
        /// API kept until the last host is migrated.
        /// </summary>
        public IReadOnlyList<ConditionBlock> Blocks => blocks;

        /// <summary>True if any block specifies an <c>output</c> action.</summary>
        public bool HasAnyOutput
        {
            get
            {
                foreach (ConditionBlock b in blocks) if (b.HasExplicitOutput) return true;
                return false;
            }
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

    public class ConditionBlock : IDriverBlock
    {
        private readonly List<ScopedCondition> conditions;
        private readonly List<IConditionAction> actions;

        public const byte DefaultOutputValue = byte.MaxValue;

        public byte OutputValue { get; }
        /// <summary>
        /// True if `output` (N or `.`) was actually specified in this block.
        /// False = `output` omitted; OutputValue then keeps the effective default 15 for the
        /// BlockSensor (no behavior change). ManagedHose reads this flag and, when false, does
        /// not touch its Output anchor at all (it holds the last value).
        /// </summary>
        public bool HasExplicitOutput { get; }
        public PaperConditionDirectives Directives { get; }
        public IReadOnlyList<IConditionAction> Actions => actions;
        public bool HasActions => actions.Count > 0;
        public bool CanSelectSource => conditions.Any(condition => condition.Scope == InventoryConditionScope.Source);

        // --- IDriverBlock: the little the unified driver needs to know about a block.
        public bool IsOutputBlock => HasExplicitOutput;

        /// <summary>Every device has a single Output pin today; see IDriverBlock.OutputPin.</summary>
        public int OutputPin => 0;

        public ConditionBlock(List<ScopedCondition> conditions, byte outputValue, bool hasExplicitOutput, PaperConditionDirectives directives, List<IConditionAction> actions)
        {
            this.conditions = conditions ?? new List<ScopedCondition>();
            OutputValue = outputValue;
            HasExplicitOutput = hasExplicitOutput;
            Directives = directives ?? PaperConditionDirectives.Empty;
            this.actions = actions ?? new List<IConditionAction>();
        }

        public bool TryMatch(ItemStack stack, IDictionary<string, object> ctx)
        {
            // A source-scoped condition is what picks the slot to move FROM, so a transfer block
            // without one is meaningless. A block that only sets the Output pin moves nothing and
            // needs no slot — requiring one there made `in target … output N` impossible to write,
            // because every condition in it is scoped to the target.
            if (!HasExplicitOutput && !CanSelectSource) return false;

            foreach (var c in conditions)
            {
                if (!c.Evaluate(stack, ctx, true)) return false;
            }

            // Directive validity (e.g. `target N ifEmpty`) is part of block validity: once a
            // block's target slot is no longer empty it stops matching, so evaluation falls
            // through to the next block. For blocks without such directives this is a no-op
            // (Directives.Evaluate returns true).
            if (!Directives.Evaluate(ctx)) return false;

            return true;
        }

        /// <summary>
        /// Do this block's conditions hold as an <b>output</b> block?
        ///
        /// Two things differ from the action rail, and both follow from there being no item in
        /// hand: nothing is being selected, and there is no source stack to select it from. So
        /// every condition is asked about the <b>target</b> — the end the device itself sits on —
        /// and asked in the plain "is this true of that inventory?" form.
        ///
        /// Without this an amount condition written with no scope prefix could never hold in an
        /// output block: the source-scoped selection path starts by demanding a stack, and an
        /// output block has none. `game:firewood 96 / output 5` was silently dead while the same
        /// block without the 96 worked.
        /// </summary>
        public bool OutputConditionsHold(IDictionary<string, object> ctx)
        {
            foreach (var c in conditions)
            {
                if (!c.EvaluateAsOutput(ctx)) return false;
            }
            return true;
        }

        /// <summary>
        /// True if every condition in the block holds (each in its scope; AND). Unlike
        /// <see cref="TryMatch(ItemStack, IDictionary{string, object})"/> this does NOT require a
        /// source-scoped condition and does NOT check directives (`ifEmpty`) — those belong to
        /// the consumer when it tries to execute the block's action. Used by the unified
        /// <see cref="CompiledConditions.RunFirst"/> driver.
        /// </summary>
        public bool ConditionsHold(ItemStack stack, IDictionary<string, object> ctx)
        {
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
            return new PaperConditionMatchResult(OutputValue, HasExplicitOutput, Directives, actions);
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
            return Evaluate(stack, ctx, isSelectionEvaluation, Scope);
        }

        /// <summary>
        /// Evaluation for the output rail: no stack, nothing being selected, and the target scope
        /// regardless of what the line says. See <see cref="ConditionBlock.OutputConditionsHold"/>.
        /// </summary>
        public bool EvaluateAsOutput(IDictionary<string, object> ctx)
        {
            return Evaluate(null, ctx, false, InventoryConditionScope.Target);
        }

        private bool Evaluate(ItemStack stack, IDictionary<string, object> ctx, bool isSelectionEvaluation, InventoryConditionScope scope)
        {
            IInventory inventory = ResolveInventory(ctx, scope);
            IDictionary<string, object> scopedCtx = BuildScopedContext(ctx, inventory);

            if (Condition is IInventoryCondition inventoryCondition)
            {
                return inventoryCondition.Evaluate(stack, inventory, scopedCtx, scope, isSelectionEvaluation);
            }

            if (isSelectionEvaluation && scope == InventoryConditionScope.Source && stack?.Collectible != null)
            {
                return Condition.Evaluate(stack, scopedCtx);
            }

            return InventoryConditionResolver.AnyMatch(inventory, scopedCtx, Condition);
        }

        private IInventory ResolveInventory(IDictionary<string, object> ctx, InventoryConditionScope scope)
        {
            if (ctx == null) return null;

            string key = scope == InventoryConditionScope.Target ? "targetInventory" : "sourceInventory";
            if (ctx.TryGetValue(key, out var obj) && obj is IInventory inventory)
            {
                return inventory;
            }

            // The plain "inventory" key is only a fallback for hosts that know of ONE inventory at
            // all - the BlockSensor watching a single container. In a transfer it is the source, so
            // handing it to a target-scoped condition would quietly measure the wrong end: an
            // `in target` count would report what is in the chest. Better no inventory than the
            // other one.
            bool hasSides = ctx.ContainsKey("sourceInventory") || ctx.ContainsKey("targetInventory");
            if (!hasSides && ctx.TryGetValue("inventory", out obj) && obj is IInventory fallbackInventory)
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
        public static readonly PaperConditionMatchResult NoMatch = new PaperConditionMatchResult(0, false, PaperConditionDirectives.Empty, Array.Empty<IConditionAction>());

        public byte OutputValue { get; }
        public bool HasExplicitOutput { get; }
        public PaperConditionDirectives Directives { get; }
        public IReadOnlyList<IConditionAction> Actions { get; }

        public PaperConditionMatchResult(byte outputValue, bool hasExplicitOutput, PaperConditionDirectives directives, IReadOnlyList<IConditionAction> actions)
        {
            OutputValue = outputValue;
            HasExplicitOutput = hasExplicitOutput;
            Directives = directives ?? PaperConditionDirectives.Empty;
            Actions = actions ?? (IReadOnlyList<IConditionAction>)Array.Empty<IConditionAction>();
        }
    }
}