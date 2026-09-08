using System.Collections.Generic;
using Vintagestory.API.Common;

namespace SignalsLink.src.signals.paperConditions
{
    public interface ICondition
    {
        bool Evaluate(ItemStack stack, IDictionary<string, object> ctx);
    }

    public interface IInventoryCondition : ICondition
    {
        bool Evaluate(ItemStack stack, IInventory inventory, IDictionary<string, object> ctx, InventoryConditionScope scope, bool isSelectionEvaluation);
    }

    public class NotCondition : ICondition, IInventoryCondition
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

        /// <summary>
        /// Negating a condition has to negate the <b>same question</b> it would otherwise have
        /// answered. Without this, `!` dropped to the plain two-argument overload: an amount
        /// condition lost its amount and degraded to a bare code match, and a scoped one lost its
        /// scope. `in target !game:firewood 96` then quietly meant "some slot holds something that
        /// is not firewood" — which an empty target can never satisfy, so it never fired.
        /// </summary>
        public bool Evaluate(ItemStack stack, IInventory inventory, IDictionary<string, object> ctx, InventoryConditionScope scope, bool isSelectionEvaluation)
        {
            if (inner is IInventoryCondition inventoryCondition)
            {
                return !inventoryCondition.Evaluate(stack, inventory, ctx, scope, isSelectionEvaluation);
            }

            // A plain condition asked of an inventory means "any slot matches", so its negation is
            // "no slot matches" - not "some slot fails", which is what the old fallback computed.
            return !InventoryConditionResolver.AnyMatch(inventory, ctx, inner);
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