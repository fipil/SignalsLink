using System.Collections.Generic;
using SignalsLink.src.signals.managedchute.transporting;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.paperConditions
{
    /// <summary>
    /// <c>isPressing</c> - is the fruit press in this scope screwed down right now? A block
    /// condition like <c>isBurning</c>: it reads the scope's position, not the stack. False when
    /// there is no press there.
    /// </summary>
    public sealed class PressingCondition : IInventoryCondition
    {
        private readonly bool expected;

        public PressingCondition(bool expected)
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

            BlockEntityFruitPress press = FruitPressAccess.At(world?.BlockAccessor, pos);
            return press != null && FruitPressMash.IsPressing(press) == expected;
        }

        private static T Get<T>(IDictionary<string, object> ctx, string key) where T : class
            => ctx != null && ctx.TryGetValue(key, out object value) ? value as T : null;
    }

    /// <summary>The press this block acts on: the target end first, else the source end.</summary>
    internal static class PressInContext
    {
        public static BlockEntityFruitPress Find(IDictionary<string, object> ctx)
        {
            if (ctx == null) return null;
            if (ctx.TryGetValue("targetBlockEntity", out object t) && t is BlockEntityFruitPress target) return target;
            if (ctx.TryGetValue("sourceBlockEntity", out object s) && s is BlockEntityFruitPress source) return source;
            return null;
        }
    }

    /// <summary><c>do press</c> - screw the press down. Nothing happens on a press already screwed down.</summary>
    public sealed class PressConditionAction : IConditionAction
    {
        public bool Execute(IDictionary<string, object> ctx) => FruitPressMash.Press(PressInContext.Find(ctx));
    }

    /// <summary><c>do release</c> - let the screw back up. Nothing happens on a released press.</summary>
    public sealed class ReleaseConditionAction : IConditionAction
    {
        public bool Execute(IDictionary<string, object> ctx) => FruitPressMash.Release(PressInContext.Find(ctx));
    }
}
