using System.Collections.Generic;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.managedchute.transporting
{
    /// <summary>
    /// Builds the context the paper conditions are answered against — the one place that decides
    /// what <c>in source</c> and <c>in target</c> can see.
    ///
    /// It used to be filled in four different places, each a little differently, which is why the
    /// same paper could mean different things on a chute, a valve and a damper, and why
    /// <c>do seal</c> quietly did nothing wherever the barrel was not passed along. A device says
    /// what its two ends are; everything that follows from them is derived here.
    /// </summary>
    public static class ConditionContext
    {
        public static IDictionary<string, object> Build(ICoreAPI api, ItemStack stack,
            IInventory sourceInventory = null, BlockPos sourcePos = null,
            IInventory targetInventory = null, BlockPos targetPos = null)
        {
            IDictionary<string, object> ctx =
                ItemConditionContextUtil.BuildContext(api.World, stack) ?? new Dictionary<string, object>();

            // Directives that ask about a block (`target firepit`) and block-state conditions
            // (`isBurning`) need the world itself.
            ctx["world"] = api.World;

            if (sourceInventory != null)
            {
                ctx["sourceInventory"] = sourceInventory;

                // The bare `inventory` key is what a host that knows of only ONE inventory reads.
                // In a transfer that is the source.
                ctx["inventory"] = sourceInventory;
            }

            if (sourcePos != null) ctx["sourceBlockPos"] = sourcePos;

            SetTarget(api, ctx, targetInventory, targetPos);
            return ctx;
        }

        public static void SetTarget(ICoreAPI api, IDictionary<string, object> ctx, IInventory targetInventory, BlockPos targetPos)
        {
            if (targetInventory != null) ctx["targetInventory"] = targetInventory;
            if (targetPos == null) return;

            ctx["targetBlockPos"] = targetPos;
            Complete(api, ctx);
        }

        /// <summary>
        /// Derives what follows from the target position, after a device has filled its own end in.
        /// Today that is the barrel <c>do seal</c> needs; deriving it here rather than in each
        /// transfer is why the action used to work in some of them and silently do nothing in the
        /// rest.
        /// </summary>
        public static void Complete(ICoreAPI api, IDictionary<string, object> ctx)
        {
            if (ctx.ContainsKey("targetBlockEntity")) return;
            if (!ctx.TryGetValue("targetBlockPos", out object posObj) || posObj is not BlockPos pos) return;

            if (api.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityBarrel barrel)
            {
                ctx["targetBlockEntity"] = barrel;
            }
        }
    }
}
