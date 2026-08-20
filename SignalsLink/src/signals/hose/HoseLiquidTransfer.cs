using System.Collections.Generic;
using signals.src.signalNetwork;
using SignalsLink.src.signals.managedchute.transporting;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.hose
{
    /// <summary>
    /// One liquid transfer step for a hose valve: pulls liquid from the far endpoint of the
    /// hose line (a remote valve's host inventory, or an intake = world water) into this
    /// valve's own host inventory. Built on the shared <see cref="LiquidTransferService"/>.
    /// Adds two ManagedHose rules: lava is never transferred, and the deposited liquid is
    /// cooled to ambient (so hot water arrives cold).
    /// </summary>
    public class HoseLiquidTransfer
    {
        private readonly ICoreAPI api;
        private readonly IInventory targetInv;
        private readonly BlockPos targetPos;
        private readonly NodePos farEndpoint;
        private readonly PaperConditionsEvaluator conditions;
        private readonly LiquidTransferService liquid;
        private readonly bool discard;

        // Position of the far source's host block (barrel/bucket/…), set by ResolveSource.
        // Null for an intake (world water) — there is no block entity to redraw there.
        private BlockPos srcHostPos;

        /// <param name="discard">
        /// Drain mode (výlevka): the valve has no host — liquid pulled from the far end is poured
        /// out (particles) and consumed instead of stored. <paramref name="targetInv"/> is null and
        /// <paramref name="targetPos"/> is the valve's own position (used for the particles).
        /// </param>
        public HoseLiquidTransfer(ICoreAPI api, IInventory targetInv, BlockPos targetPos, NodePos farEndpoint, PaperConditionsEvaluator conditions, bool discard = false)
        {
            this.api = api;
            this.targetInv = targetInv;
            this.targetPos = targetPos;
            this.farEndpoint = farEndpoint;
            this.conditions = conditions;
            this.discard = discard;
            this.liquid = new LiquidTransferService(api, targetInv, targetPos);
        }

        public struct Result
        {
            public TransferOperationResult Transfer;
            public bool HasExplicitOutput;
            public byte OutputValue;

            public static Result None => new Result { Transfer = TransferOperationResult.None };
        }

        public Result TryMove(decimal litresRequested)
        {
            // Resolve the source liquid at the far endpoint (remote valve host, or intake water).
            ItemStack sourceLiquid = ResolveSource(out ItemSlot srcSlot, out BlockPos worldWaterPos, out IInventory sourceInv);

            // Target-aware context (valid even when there is nothing to pull).
            IDictionary<string, object> ctx = BuildContext(sourceLiquid, sourceInv);

            // No conditions → default action (transfer, or discard in drain mode).
            if (conditions == null || !conditions.HasConditions)
            {
                if (sourceLiquid == null) return Result.None;
                return new Result { Transfer = DefaultAction(sourceLiquid, srcSlot, worldWaterPos, PaperConditionDirectives.Empty, litresRequested, ctx) };
            }

            // Unified rule (docs/paper-conditions.md): run the FIRST block whose conditions hold
            // and whose action actually does work. A block's action is either the default
            // (transfer) or an explicit action that replaces it (`output N`, `do seal`).
            Result result = Result.None;
            conditions.RunFirst(sourceLiquid, ctx, match =>
            {
                Result? r = ExecuteBlock(match, sourceLiquid, srcSlot, worldWaterPos, litresRequested, ctx);
                if (r.HasValue) { result = r.Value; return true; }
                return false;
            });
            return result;
        }

        /// <summary>
        /// Tries to perform one block's action. Returns the result if the action did work (this
        /// block wins), or null if it did nothing (evaluation falls through to the next block).
        /// </summary>
        private Result? ExecuteBlock(PaperConditionMatchResult match, ItemStack sourceLiquid, ItemSlot srcSlot, BlockPos worldWaterPos, decimal litresRequested, IDictionary<string, object> ctx)
        {
            // 1) Explicit action `do seal` — replaces the transfer.
            if (match.Actions.Count > 0)
            {
                bool did = false;
                for (int i = 0; i < match.Actions.Count; i++) if (match.Actions[i].Execute(ctx)) did = true;
                return did ? new Result { Transfer = TransferOperationResult.None } : (Result?)null;
            }

            // 2) Explicit action `output N` — replaces the transfer (the valve has an Output pin).
            //    The block's action IS output, so it never falls back to a transfer. Values 0..15
            //    set the pin; the `output .` sentinel (255) is sensor-only → no work → next block.
            if (match.HasExplicitOutput)
            {
                return match.OutputValue <= 15
                    ? new Result { Transfer = TransferOperationResult.None, HasExplicitOutput = true, OutputValue = match.OutputValue }
                    : (Result?)null;
            }

            // 3) Default action — transfer liquid (or discard in drain mode), shaped by directives.
            if (sourceLiquid == null) return null;
            TransferOperationResult res = DefaultAction(sourceLiquid, srcSlot, worldWaterPos, match.Directives, litresRequested, ctx);
            return res.Success ? new Result { Transfer = res } : (Result?)null;
        }

        private TransferOperationResult DefaultAction(ItemStack sourceLiquid, ItemSlot srcSlot, BlockPos worldWaterPos, PaperConditionDirectives directives, decimal litresRequested, IDictionary<string, object> ctx)
        {
            return discard
                ? DiscardLiquid(sourceLiquid, srcSlot, worldWaterPos, directives, litresRequested)
                : TransferLiquid(sourceLiquid, srcSlot, worldWaterPos, directives, litresRequested, ctx);
        }

        /// <summary>
        /// Performs the default transfer for a block. Returns None if it moved nothing (source
        /// empty, target full, `ifEmpty` not satisfied, or the source is lava). On success cools
        /// the deposited liquid to ambient (hot water arrives cold).
        /// </summary>
        private TransferOperationResult TransferLiquid(ItemStack sourceLiquid, ItemSlot srcSlot, BlockPos worldWaterPos, PaperConditionDirectives directives, decimal litresRequested, IDictionary<string, object> ctx)
        {
            // ManagedHose rule: never transfer lava.
            if (IsLava(sourceLiquid)) return TransferOperationResult.None;

            // `target N ifEmpty` is part of transfer feasibility.
            if (!directives.Evaluate(ctx)) return TransferOperationResult.None;

            byte targetSlotSignal = directives.TargetSlot ?? 0;
            ItemSlot dst = liquid.GetTargetSlot(sourceLiquid, targetSlotSignal);
            if (dst == null) return TransferOperationResult.None;

            decimal litres = directives.Amount ?? litresRequested;

            TransferOperationResult res = srcSlot != null
                ? liquid.TryMoveFromItemSlot(srcSlot, dst, litres, directives.HasAmountOverride)
                : liquid.TryMoveFromWorldSource(worldWaterPos, dst, litres, directives.HasAmountOverride);

            if (res.Success)
            {
                CoolToAmbient(dst);
                dst.MarkDirty();
                srcSlot?.MarkDirty();
                MarkSourceDirty(); // redraw the source block (e.g. a bucket's fill level)
            }
            return res;
        }

        /// <summary>
        /// Drain (výlevka): pulls liquid from the far source and pours it out — consumes it from
        /// the source (a barrel), or "pulls" from an intake (infinite world water), and spawns
        /// droplet particles. Nothing is stored. Returns None if there was nothing to pull.
        /// </summary>
        private TransferOperationResult DiscardLiquid(ItemStack sourceLiquid, ItemSlot srcSlot, BlockPos worldWaterPos, PaperConditionDirectives directives, decimal litresRequested)
        {
            if (IsLava(sourceLiquid)) return TransferOperationResult.None;

            WaterTightContainableProps props = BlockLiquidContainerBase.GetContainableProps(sourceLiquid);
            if (props == null || props.ItemsPerLitre <= 0) return TransferOperationResult.None;

            decimal litres = decimal.Round(directives.Amount ?? litresRequested, 2, System.MidpointRounding.ToZero);
            if (litres <= 0) return TransferOperationResult.None;

            int wantItems = (int)(props.ItemsPerLitre * (float)litres);
            if (wantItems <= 0) return TransferOperationResult.None;

            int movedItems;
            if (srcSlot != null)
            {
                // Container source (barrel etc.): take out only what's available.
                movedItems = System.Math.Min(wantItems, srcSlot.StackSize);
                if (movedItems <= 0) return TransferOperationResult.None;
                srcSlot.TakeOut(movedItems);
                srcSlot.MarkDirty();
                MarkSourceDirty(); // redraw the source block (e.g. a bucket's fill level)
            }
            else
            {
                // World-water source (intake) is effectively infinite.
                movedItems = wantItems;
            }

            SpawnDrainParticles();

            decimal movedLitres = decimal.Round(movedItems / (decimal)props.ItemsPerLitre, 2, System.MidpointRounding.ToZero);
            if (movedLitres <= 0) return TransferOperationResult.None;
            int triggerCost = directives.HasAmountOverride ? 1 : (int)movedLitres;
            if (triggerCost <= 0) triggerCost = 1;
            return new TransferOperationResult(movedLitres, triggerCost, true);
        }

        private void SpawnDrainParticles()
        {
            if (targetPos == null) return;
            // Pour into the block the drain faces (targetPos): spawn near its top and let the
            // droplets fall — they just spill and soak in (nothing is stored in the world).
            Vec3d min = new Vec3d(targetPos.X + 0.3, targetPos.Y + 0.55, targetPos.Z + 0.3);
            Vec3d max = new Vec3d(targetPos.X + 0.7, targetPos.Y + 0.75, targetPos.Z + 0.7);
            SimpleParticleProperties p = new SimpleParticleProperties(
                4, 8,
                ColorUtil.ToRgba(160, 60, 90, 200),
                min, max,
                new Vec3f(-0.1f, -0.6f, -0.1f), new Vec3f(0.1f, -0.2f, 0.1f),
                0.9f, 0.4f, 0.15f, 0.35f, EnumParticleModel.Cube);
            p.WithTerrainCollision = true;
            api.World.SpawnParticles(p);
        }

        private ItemStack ResolveSource(out ItemSlot srcSlot, out BlockPos worldWaterPos, out IInventory sourceInv)
        {
            srcSlot = null;
            worldWaterPos = null;
            sourceInv = null;

            if (farEndpoint?.blockPos == null) return null;

            Block farBlock = api.World.BlockAccessor.GetBlock(farEndpoint.blockPos);

            // Intake -> passive world-water source.
            if (farBlock is BlockHoseIntake)
            {
                BlockPos waterPos = FindWaterSource(farEndpoint.blockPos);
                if (waterPos == null) return null;
                if (!liquid.TryResolveWorldLiquidSource(waterPos, out ItemStack waterStack)) return null;
                worldWaterPos = waterPos;
                return waterStack;
            }

            // Otherwise: a remote valve with a host inventory -> its liquid slot.
            IInventory farInv = GetHostInventory(farEndpoint.blockPos, out srcHostPos);
            if (farInv == null) return null;
            sourceInv = farInv;

            ItemSlot liquidSlot = FindLiquidSlot(farInv);
            if (liquidSlot == null || liquidSlot.Empty) return null;
            srcSlot = liquidSlot;
            return liquid.GetLiquidStackForTransfer(liquidSlot.Itemstack);
        }

        private BlockPos FindWaterSource(BlockPos intakePos)
        {
            if (liquid.TryResolveWorldLiquidSource(intakePos, out _)) return intakePos;
            foreach (BlockFacing f in BlockFacing.ALLFACES)
            {
                BlockPos p = intakePos.AddCopy(f);
                if (liquid.TryResolveWorldLiquidSource(p, out _)) return p;
            }
            return null;
        }

        private IInventory GetHostInventory(BlockPos valvePos, out BlockPos hostPos)
        {
            // The far valve's host = the block it is mounted on (its `side` direction).
            Block b = api.World.BlockAccessor.GetBlock(valvePos);
            string side = b?.Variant?["side"];
            BlockFacing face = side != null ? BlockFacing.FromCode(side) : null;
            hostPos = valvePos.AddCopy(face ?? BlockFacing.DOWN);

            if (api.World.BlockAccessor.GetBlockEntity(hostPos) is IBlockEntityContainer c && c.Inventory != null)
                return c.Inventory;
            hostPos = null;
            return null;
        }

        /// <summary>
        /// Redraw the far source's host block after we drained it. A barrel re-tesselates itself
        /// on inventory change, but a bucket (and similar liquid containers) only refresh their
        /// fill-level mesh when their block entity is explicitly told to redraw.
        /// </summary>
        private void MarkSourceDirty()
        {
            if (srcHostPos != null) api.World.BlockAccessor.GetBlockEntity(srcHostPos)?.MarkDirty(true);
        }

        private static ItemSlot FindLiquidSlot(IInventory inv)
        {
            for (int i = 0; i < inv.Count; i++)
            {
                ItemSlot s = inv[i];
                if (s != null && !s.Empty && s.Itemstack.Collectible.IsLiquid()) return s;
            }
            return null;
        }

        private static bool IsLava(ItemStack liquidStack)
        {
            string path = liquidStack?.Collectible?.Code?.Path;
            return path != null && path.Contains("lava");
        }

        private void CoolToAmbient(ItemSlot dst)
        {
            if (dst?.Itemstack?.Collectible == null) return;

            float ambient = 15f;
            ClimateCondition climate = api.World.BlockAccessor.GetClimateAt(targetPos);
            if (climate != null) ambient = climate.Temperature;

            dst.Itemstack.Collectible.SetTemperature(api.World, dst.Itemstack, ambient, false);
        }

        private IDictionary<string, object> BuildContext(ItemStack sourceLiquid, IInventory sourceInv)
        {
            // Item context (temperature, stackSize, …) only when there IS a source liquid;
            // actions like `do seal` are target-scoped and work without it.
            IDictionary<string, object> ctx = sourceLiquid != null
                ? (ItemConditionContextUtil.BuildContext(api.World, sourceLiquid) ?? new Dictionary<string, object>())
                : new Dictionary<string, object>();

            if (sourceInv != null) ctx["sourceInventory"] = sourceInv;
            ctx["targetInventory"] = targetInv;
            ctx["inventory"] = sourceInv ?? targetInv;
            ctx["targetBlockPos"] = targetPos;

            if (api.World.BlockAccessor.GetBlockEntity(targetPos) is BlockEntityBarrel barrel)
                ctx["targetBlockEntity"] = barrel;

            return ctx;
        }
    }
}
