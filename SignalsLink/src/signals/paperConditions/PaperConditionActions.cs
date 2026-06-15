using System.Collections.Generic;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.paperConditions
{
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
}