using System.Collections.Generic;
using signals.src.signalNetwork;
using SignalsLink.src.signals.managedchute.transporting;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using SignalsLink.src.signals.link;

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
        // Upper bound on litres this move may transfer (the remaining Input buffer). An `amount M`
        // directive is capped by it, so the buffer is a hard "litres left to move" limit
        // (buffer 3 + amount 6 → move only 3). decimal.MaxValue = unlimited (signal 15).
        private readonly decimal maxTransfer;

        // Position of the far source's host block (barrel/bucket/…), set by ResolveSource.
        // Null for an intake (world water) — there is no block entity to redraw there.
        private BlockPos srcHostPos;

        /// <param name="discard">
        /// Drain mode (výlevka): the valve has no host — liquid pulled from the far end is poured
        /// out (particles) and consumed instead of stored. <paramref name="targetInv"/> is null and
        /// <paramref name="targetPos"/> is the valve's own position (used for the particles).
        /// </param>
        public HoseLiquidTransfer(ICoreAPI api, IInventory targetInv, BlockPos targetPos, NodePos farEndpoint, PaperConditionsEvaluator conditions, bool discard = false, decimal maxTransfer = decimal.MaxValue)
        {
            this.api = api;
            this.targetInv = targetInv;
            this.targetPos = targetPos;
            this.farEndpoint = farEndpoint;
            this.conditions = conditions;
            this.discard = discard;
            this.maxTransfer = maxTransfer;
            this.liquid = new LiquidTransferService(api, targetInv, targetPos);
        }

        public struct Result
        {
            public TransferOperationResult Transfer;

            /// <summary>
            /// True once an evaluation pass has run, which is whenever the valve is alive. The pin
            /// then takes <see cref="Output"/> unconditionally - the 0 of a pass in which no
            /// `output` block held included.
            /// </summary>
            public bool OutputComputed;
            public byte Output;
            // In drain mode, the liquid that was poured out (used by the BE to spawn mouth
            // particles tinted with the liquid's colour). Null unless a drain actually happened.
            public ItemStack DrainedLiquid;

            public static Result None => new Result { Transfer = TransferOperationResult.None };
        }

        /// <param name="actionsBlocked">
        /// Closes the action rail: the valve has no input credit, or is waiting for its turn on
        /// the line. The pass still runs, so the Output pin keeps reporting the current state
        /// instead of freezing on the last thing that moved it.
        /// </param>
        public Result TryMove(decimal litresRequested, bool actionsBlocked = false)
        {
            // Resolve the source liquid at the far endpoint (remote valve host, or intake water).
            ItemStack sourceLiquid = ResolveSource(out ItemSlot srcSlot, out BlockPos worldWaterPos, out IInventory sourceInv);

            // Target-aware context (valid even when there is nothing to pull).
            IDictionary<string, object> ctx = BuildContext(sourceLiquid, sourceInv);

            Result result = Result.None;

            IReadOnlyList<ConditionBlock> blocks = conditions?.GetBlocks();

            // No paper at all: the default action, and a pin nothing can drive, so it reads 0.
            if (blocks == null || blocks.Count == 0)
            {
                if (!actionsBlocked && sourceLiquid != null)
                {
                    result.Transfer = DefaultAction(sourceLiquid, srcSlot, worldWaterPos, PaperConditionDirectives.Empty, litresRequested, ctx);
                }

                result.OutputComputed = true;
                result.Output = 0;
            }
            else
            {
                // The unified pass (paper-conditions-rules-v2.md): one walk in paper order that
                // carries the output rail and the action rail at the same time.
                Result acted = Result.None;

                DriverResult driven = ConditionDriver.Run(
                    blocks,
                    actionsBlocked,
                    block => block.OutputConditionsHold(ctx),
                    block =>
                    {
                        Result? r = ExecuteBlock(block, sourceLiquid, srcSlot, worldWaterPos, litresRequested, ctx);
                        if (!r.HasValue) return false;
                        acted = r.Value;
                        return true;
                    });

                result = acted;
                result.OutputComputed = true;
                result.Output = driven.GetOutput();
            }

            // Tell the BE which liquid was poured out, so it can render mouth particles.
            if (discard && result.Transfer.Success) result.DrainedLiquid = sourceLiquid;
            return result;
        }

        /// <summary>
        /// Tries to perform one action block. Returns the result if the action did work (the rail
        /// then closes), or null if it did nothing (the walk falls through to the next block).
        ///
        /// The predicate here is deliberately not the one the item transfers use: they also demand
        /// a source-scoped condition, because that is what picks the slot to take from. A valve has
        /// no slot to pick, the far end of the hose IS the source, so demanding one would kill
        /// perfectly ordinary papers such as `in target / *water* 50-`.
        /// </summary>
        private Result? ExecuteBlock(ConditionBlock block, ItemStack sourceLiquid, ItemSlot srcSlot, BlockPos worldWaterPos, decimal litresRequested, IDictionary<string, object> ctx)
        {
            // `source N` names the slot to draw from at the far end, so it has to be resolved
            // before the conditions are asked - they are asked about that liquid, not about
            // whichever one the default rule happened to find first. Used to be ignored here in
            // silence, which is the worst way for a directive to not work.
            if (block.Directives.SourceSlot.HasValue)
            {
                sourceLiquid = ResolveSource(out srcSlot, out worldWaterPos, out IInventory blockSourceInv, block.Directives.SourceSlot);
                ctx = BuildContext(sourceLiquid, blockSourceInv);
            }

            if (!block.ConditionsHold(sourceLiquid, ctx)) return null;

            // Directive validity is block validity: a `target N ifEmpty` block stops matching once
            // its slot fills and the walk moves on. The older liquid path skipped this, which is
            // why the same paper behaved differently on a valve than on a damper.
            if (!block.Directives.Evaluate(ctx)) return null;

            // 1) Explicit action `do seal` - replaces the transfer.
            if (block.HasActions)
            {
                bool did = false;
                for (int i = 0; i < block.Actions.Count; i++) if (block.Actions[i].Execute(ctx)) did = true;
                return did ? new Result { Transfer = TransferOperationResult.None } : (Result?)null;
            }

            // 2) Default action - transfer liquid (or discard in drain mode), shaped by directives.
            if (sourceLiquid == null) return null;
            TransferOperationResult res = DefaultAction(sourceLiquid, srcSlot, worldWaterPos, block.Directives, litresRequested, ctx);
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

            // Cap by the remaining buffer: `amount M` never moves more than what is left to move.
            decimal litres = System.Math.Min(directives.Amount ?? litresRequested, maxTransfer);

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

            // Cap by the remaining buffer (see maxTransfer): the drain consumes at most what's left.
            decimal litres = decimal.Round(System.Math.Min(directives.Amount ?? litresRequested, maxTransfer), 2, System.MidpointRounding.ToZero);
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

            // Particles are rendered client-side by the BE (see BlockEntityHoseValve), tinted
            // with the liquid's colour and emitted from the configured spout mouth.

            decimal movedLitres = decimal.Round(movedItems / (decimal)props.ItemsPerLitre, 2, System.MidpointRounding.ToZero);
            if (movedLitres <= 0) return TransferOperationResult.None;
            // Buffer model B: the cost is the litres actually moved (not a flat 1 per amount-op),
            // so the Input buffer counts down in real litres.
            int triggerCost = (int)movedLitres;
            if (triggerCost <= 0) triggerCost = 1;
            return new TransferOperationResult(movedLitres, triggerCost, true);
        }

        /// <param name="sourceSlot">
        /// The `source N` directive, when a block carries one: draw from exactly that slot of the
        /// far host and from no other. An intake (world water) has no slots, so it ignores this.
        /// </param>
        private ItemStack ResolveSource(out ItemSlot srcSlot, out BlockPos worldWaterPos, out IInventory sourceInv, byte? sourceSlot = null)
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

            ItemSlot liquidSlot = sourceSlot.HasValue ? GetLiquidSlotAt(farInv, sourceSlot.Value) : FindLiquidSlot(farInv);
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

        /// <summary>The far host slot a `source N` directive names, or null if it holds no liquid.</summary>
        private static ItemSlot GetLiquidSlotAt(IInventory inv, byte slotNumber)
        {
            int index = slotNumber - 1;
            if (index < 0 || index >= inv.Count) return null;

            ItemSlot slot = inv[index];
            return slot != null && !slot.Empty && slot.Itemstack.Collectible.IsLiquid() ? slot : null;
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
            IDictionary<string, object> ctx =
                ConditionContext.Build(api, sourceLiquid, sourceInv, srcHostPos, targetInv, targetPos);

            // An intake is world water, so there is no source inventory to be the single one a
            // bare `inventory` means. The target is then the only one there is.
            if (sourceInv == null && targetInv != null) ctx["inventory"] = targetInv;

            return ctx;
        }
    }
}
