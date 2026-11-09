using System.Collections.Generic;
using SignalsLink.src.signals.managedchute.transporting;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.paperConditions
{
    /// <summary>
    /// <c>firepitReady</c> - is the firepit in this scope lit or ready to be lit? A block
    /// condition like <c>isBurning</c>: a boiler when cold or lit, a firepit block when burning or
    /// holding fuel. False for a burnt-out firepit and where there is none at all.
    /// </summary>
    public sealed class FirepitReadyCondition : IInventoryCondition
    {
        private readonly bool expected;

        public FirepitReadyCondition(bool expected)
        {
            this.expected = expected;
        }

        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx)
            => EvaluateAt(ctx, InventoryConditionScope.Source);

        public bool Evaluate(ItemStack stack, IInventory inventory, IDictionary<string, object> ctx, InventoryConditionScope scope, bool isSelectionEvaluation)
            => EvaluateAt(ctx, scope);

        private bool EvaluateAt(IDictionary<string, object> ctx, InventoryConditionScope scope)
        {
            IWorldAccessor world = Get<IWorldAccessor>(ctx, "world");
            BlockPos pos = Get<BlockPos>(ctx, scope == InventoryConditionScope.Target ? "targetBlockPos" : "sourceBlockPos")
                        ?? Get<BlockPos>(ctx, "blockPos");

            if (world == null || pos == null) return false;

            return FirepitConstruction.IsReadyAt(world, pos) == expected;
        }

        private static T Get<T>(IDictionary<string, object> ctx, string key) where T : class
            => ctx != null && ctx.TryGetValue(key, out object value) ? value as T : null;
    }
}
