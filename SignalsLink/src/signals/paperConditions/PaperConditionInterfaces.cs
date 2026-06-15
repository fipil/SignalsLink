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