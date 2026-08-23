using signals.src;
using signals.src.signalNetwork;
using SignalsLink.src.signals.paperConditions;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SignalsLink.src.signals.hose
{
    /// <summary>
    /// Valve block entity. Signals anchors: index 0 = Input (like ManagedChute), index 1 =
    /// Output (isSource — its value is decided by paper conditions and pushed on the signal
    /// tick, like the BlockSensor). Index 2 = hose anchor (handled by the block, not the BE).
    /// Liquid transfer, host detection and shape swap are added in step 5+.
    /// </summary>
    public class BlockEntityHoseValve : BlockEntity, IBESignalReceptor, IPaperConditionsHost
    {
        public const int INPUT = 0;
        public const int OUTPUT = 1;
        public const int HOSE = 2;

        private const byte UNLIMITED_TRANSFER = 15;

        private int checkRateMs = 200;
        // Flow-rate cap: how many litres this valve may move per tick. Keeps the transfer
        // gradual (a whole barrel takes a while) instead of emptying in one tick.
        private decimal maxLitresPerTick = 1.0m;

        public byte signalState;
        private int remaining;
        private bool unlimited;

        /// <summary>Does this valve currently have Input credit (a batch, or continuous)?</summary>
        public bool HasInput => unlimited || remaining > 0;

        // Output anchor (index 1) — holds its last value until paper conditions override it.
        public byte outputState;
        private byte? lastPushedOutput;

        private SignalNetworkMod signalMod;

        private static readonly AssetLocation waterSound = new AssetLocation("sounds/block/water");

        private string conditionsText = null;
        private PaperConditionsEvaluator conditionsEvaluator = new PaperConditionsEvaluator();

        // Drain (výlevka) visuals: the server bumps drainPulse + stores the poured liquid on every
        // successful drain tick (synced to clients); the client renders spout particles for a short
        // window after each pulse, tinted with the liquid's colour.
        private int drainPulse;
        private ItemStack drainLiquid;
        private int lastClientPulse = -1;
        private long lastDrainMs;

        // Flow pulse: bumped server-side whenever the random flow sound plays (synced to clients),
        // so the client can wobble the hose in time with the audible surge of liquid.
        private int flowPulse;
        private int lastClientFlowPulse = -1;

        // Idle backoff: after repeated unproductive pull attempts (source empty, target full, no
        // host, dangling hose …) the valve runs the heavier pull less often, then snaps back to
        // full rate as soon as something moves. Waiting for an arbitration turn does NOT back off,
        // so two facing valves keep alternating snappily.
        private int noWorkStreak;
        private int tickSkip;

        public int SignalInputsCount => 3; // 2 Signals anchors + 1 hose anchor (selection boxes 0..2)

        public string ConditionsText
        {
            get => conditionsText;
            set
            {
                conditionsText = value;
                conditionsEvaluator?.SetConditionsText(conditionsText);
                MarkDirty();
            }
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            conditionsEvaluator?.SetConditionsText(conditionsText);

            signalMod = api.ModLoader.GetModSystem<SignalNetworkMod>();
            signalMod.RegisterSignalTickListener(OnSignalNetworkTick);

            if (api is ICoreServerAPI)
            {
                checkRateMs = Block?.Attributes?["liquid-checkrateMs"].AsInt(200) ?? 200;
                maxLitresPerTick = (decimal)(Block?.Attributes?["hose-litres-per-tick"].AsFloat(1.0f) ?? 1.0f);
                RegisterDelayedCallback(dt =>
                {
                    UpdateMountState();
                    RegisterGameTickListener(MoveLiquid, checkRateMs);
                }, 10 + api.World.Rand.Next(200));
            }
            else if (api is ICoreClientAPI)
            {
                RegisterGameTickListener(ClientDrainTick, 50);
            }
        }

        /// <summary>Called by the block when a neighbour changes — the host may have appeared/disappeared.</summary>
        public void OnNeighbourChanged(BlockPos neibpos)
        {
            if (Api is ICoreServerAPI) UpdateMountState();
        }

        #region Mount state (shape) — hung on host / stand (no host) / drain (floor)

        /// <summary>
        /// Swaps the block to the correct `mount` variant based on placement + host presence:
        /// side=down → drain (výlevka); wall + host → hung (no legs); wall + no host → stand (legs).
        /// Mirrors <c>BEBlockSensor.UpdateBlockState</c>.
        /// </summary>
        private void UpdateMountState()
        {
            if (Api is not ICoreServerAPI) return;

            Block cur = Api.World.BlockAccessor.GetBlock(Pos);
            string orientation = cur.Variant["orientation"];
            string side = cur.Variant["side"];
            if (orientation == null || side == null) return;

            // The three mount states are three separate block codes (each with only
            // orientation+side variants, so rotation works exactly like ManagedChute).
            string desired = DesiredBlockCode(side);
            if (cur.FirstCodePart() == desired) return;

            Block newBlock = Api.World.GetBlock(new AssetLocation("signalslink", desired + "-" + orientation + "-" + side));
            if (newBlock != null)
            {
                Api.World.BlockAccessor.ExchangeBlock(newBlock.Id, Pos);
                Api.World.BlockAccessor.MarkBlockDirty(Pos);
            }
        }

        private string DesiredBlockCode(string side)
        {
            if (side == "down") return "hosevalvedrain";                // floor → drain (výlevka)
            return HasContainerHost() ? "hosevalve" : "hosevalvestand";  // wall → hung (host) / stand (air)
        }

        private bool HasContainerHost()
        {
            BlockPos p = Pos.AddCopy(GetSideFace());
            return Api.World.BlockAccessor.GetBlockEntity(p) is IBlockEntityContainer c && c.Inventory != null;
        }

        #endregion

        /// <summary>
        /// Server tick wrapper: gates the (heavier) pull behind an idle backoff so a valve that
        /// can't currently do anything stops re-resolving its source/conditions every tick.
        /// </summary>
        private void MoveLiquid(float dt)
        {
            if (Api is not ICoreServerAPI) return;
            if (!HasInput) { noWorkStreak = 0; tickSkip = 0; return; } // truly idle → cheap no-op

            // --- idle-backoff TEMPORARILY DISABLED for testing (an output-only valve was not
            // re-evaluating fast enough to drop its output). Runs TryPull every tick instead. ---
            //int stride = 1 + System.Math.Min(noWorkStreak, 7);
            //if (++tickSkip < stride) return;
            //tickSkip = 0;
            //
            //int status = TryPull();
            //if (status == 0) noWorkStreak = 0;                                   // moved → full rate
            //else if (status == 1) noWorkStreak = System.Math.Min(noWorkStreak + 1, 64); // blocked → back off
            //// status == 2 (waiting for our arbitration turn): keep the current rate so two facing
            //// valves keep alternating without lag.

            TryPull();
        }

        /// <summary>
        /// One pull attempt: while this valve has Input credit, pull liquid from the far end of its
        /// hose into its host (or pour it out in drain mode).
        /// </summary>
        /// <returns>0 = moved, 1 = blocked (nothing to do), 2 = waiting for our arbitration turn.</returns>
        private int TryPull()
        {
            HoseNetworkMod hoseMod = Api.ModLoader.GetModSystem<HoseNetworkMod>();
            if (hoseMod == null) return 1;

            NodePos myAnchor = new NodePos(Pos, HOSE);
            NodePos far = hoseMod.GetOtherEndpoint(Api.World, myAnchor);
            if (far == null) return 1;

            // Placement decides the mode:
            //  - floor (side=down)  → drain (výlevka): pull + pour out, no host inventory;
            //  - wall + host        → transfer into the host;
            //  - wall + no host     → idle.
            bool discard = GetSideFace() == BlockFacing.DOWN;
            IInventory hostInv = null;
            BlockPos hostPos;
            if (discard)
            {
                hostPos = Pos.AddCopy(GetOrientationFace()); // drain pours into the block it faces
            }
            else
            {
                hostInv = GetHostInventory(out hostPos);
                if (hostInv == null) return 1; // wall without host → idle
            }

            // Contention only exists when the far end is ALSO an active valve; then the two
            // valves must take turns (arbitration), otherwise they fight and stall.
            bool contested = IsFarActiveValve(far);
            if (contested && !hoseMod.IsOnTurn(myAnchor, far)) return 2;

            decimal litres = unlimited ? maxLitresPerTick : System.Math.Min((decimal)remaining, maxLitresPerTick);

            var transfer = new HoseLiquidTransfer(Api, hostInv, hostPos, far, conditionsEvaluator, discard);
            HoseLiquidTransfer.Result result = transfer.TryMove(litres);

            if (result.HasExplicitOutput) SetOutput(result.OutputValue);

            bool moved = result.Transfer.Success;

            // Drain mode: publish the poured liquid + a pulse so clients render spout particles.
            if (moved && discard && result.DrainedLiquid != null)
            {
                drainLiquid = result.DrainedLiquid;
                drainPulse++;
                MarkDirty();
            }

            if (moved && !unlimited)
            {
                remaining -= result.Transfer.TriggerCost;
                if (remaining < 0) remaining = 0;
                MarkDirty();
            }

            // Occasional water splash while transporting (mirrors the chute's random sound). The
            // hose wobbles in sync with this audible pulse (see flowPulse → client TriggerWobble).
            if (moved && Api.World.Rand.NextDouble() < 0.2)
            {
                Api.World.PlaySoundAt(waterSound, Pos, 0.0, range: 8f, volume: 0.5f);
                flowPulse++;
                MarkDirty();
            }

            // Alternation: pass the turn to the other valve when we can't move (source empty /
            // target full) or we finished our batch. Two facing valves then drain each other
            // in turns instead of both pulling at once.
            if (contested)
            {
                bool finishedBatch = !unlimited && remaining <= 0;
                if (!moved || finishedBatch) hoseMod.PassToken(myAnchor, far);
            }

            return moved ? 0 : 1;
        }

        /// <summary>Is the far endpoint another valve that currently has Input (i.e. contends for the line)?</summary>
        private bool IsFarActiveValve(NodePos far)
        {
            return Api.World.BlockAccessor.GetBlockEntity(far.blockPos) is BlockEntityHoseValve v && v.HasInput;
        }

        /// <summary>Face the valve is mounted on (its `side` variant); the host block sits there.</summary>
        private BlockFacing GetSideFace()
        {
            string side = Block?.Variant?["side"];
            BlockFacing f = side != null ? BlockFacing.FromCode(side) : null;
            return f ?? BlockFacing.DOWN;
        }

        /// <summary>Face the valve points to (its `orientation` variant); the drain pours there.</summary>
        private BlockFacing GetOrientationFace()
        {
            string o = Block?.Variant?["orientation"];
            BlockFacing f = o != null ? BlockFacing.FromCode(o) : null;
            return f ?? BlockFacing.NORTH;
        }

        /// <summary>
        /// This valve's host block = the block it is mounted on, i.e. the neighbour in the
        /// `side` direction. That block's inventory is where the valve deposits liquid.
        /// </summary>
        private IInventory GetHostInventory(out BlockPos hostPos)
        {
            hostPos = Pos.AddCopy(GetSideFace());
            if (Api.World.BlockAccessor.GetBlockEntity(hostPos) is IBlockEntityContainer c && c.Inventory != null)
            {
                return c.Inventory;
            }
            hostPos = null;
            return null;
        }

        #region Drain (výlevka) particles — client-side

        /// <summary>
        /// World position of the spout mouth. The mouth is authored in shape (0..16) coords in the
        /// local frame (orientation=north, spout on the north face); here it is rotated onto the
        /// valve's current facing so the droplets leave the visible spout. Configurable per model
        /// via the block attribute <c>drainSpout</c> {x,y,z}.
        /// </summary>
        private Vec3d GetDrainMouthWorldPos()
        {
            float mx = 8f, my = 5f, mz = 0f;
            var cfg = Block?.Attributes?["drainSpout"];
            if (cfg != null && cfg.Exists)
            {
                mx = cfg["x"].AsFloat(mx);
                my = cfg["y"].AsFloat(my);
                mz = cfg["z"].AsFloat(mz);
            }

            double lx = mx / 16.0, ly = my / 16.0, lz = mz / 16.0;
            double dx = lx - 0.5, dz = lz - 0.5; // horizontal offset from block centre

            // Rotate the local offset so that local north (−Z) maps onto the current facing.
            BlockFacing f = GetOrientationFace();
            double ox, oz;
            if (f == BlockFacing.EAST) { ox = -dz; oz = dx; }
            else if (f == BlockFacing.SOUTH) { ox = -dx; oz = -dz; }
            else if (f == BlockFacing.WEST) { ox = dz; oz = -dx; }
            else { ox = dx; oz = dz; } // NORTH (and any non-horizontal fallback)

            return new Vec3d(Pos.X + 0.5 + ox, Pos.Y + ly, Pos.Z + 0.5 + oz);
        }

        private void ClientDrainTick(float dt)
        {
            if (Api is not ICoreClientAPI) return;

            // Wobble the hose in sync with the flow sound.
            if (flowPulse != lastClientFlowPulse)
            {
                lastClientFlowPulse = flowPulse;
                Api.ModLoader.GetModSystem<HoseNetworkMod>()?.Renderer?.TriggerWobble(new NodePos(Pos, HOSE));
            }

            if (drainPulse != lastClientPulse)
            {
                lastClientPulse = drainPulse;
                lastDrainMs = Api.World.ElapsedMilliseconds;
            }

            // Keep a smooth stream for a short window after each drain pulse (~one server tick).
            if (drainLiquid != null && Api.World.ElapsedMilliseconds - lastDrainMs < 300)
            {
                SpawnDrainMouthParticles(drainLiquid);
            }
        }

        private void SpawnDrainMouthParticles(ItemStack liquid)
        {
            if (Api is not ICoreClientAPI capi || liquid?.Collectible == null) return;

            int color = liquid.Collectible.GetRandomColor(capi, liquid);
            Vec3d mouth = GetDrainMouthWorldPos();

            SimpleParticleProperties p = new SimpleParticleProperties(
                1, 2,
                color,
                new Vec3d(mouth.X - 0.02, mouth.Y - 0.02, mouth.Z - 0.02),
                new Vec3d(mouth.X + 0.02, mouth.Y + 0.02, mouth.Z + 0.02),
                new Vec3f(-0.12f, -0.35f, -0.12f),  // min velocity: mostly downward, some spread
                new Vec3f(0.12f, -0.15f, 0.12f),    // max velocity
                1.4f,   // life length — long enough to reach the ground
                0.25f,  // gravity — accelerates the fall
                0.20f, 0.44f, // min/max size (2× larger — the droplets were too tiny)
                EnumParticleModel.Cube);
            p.WithTerrainCollision = true;

            capi.World.SpawnParticles(p);
        }

        #endregion

        #region Signals

        public void OnValueChanged(NodePos pos, byte value)
        {
            if (pos.index == INPUT) ProcessInput(value);
            // Output (index 1) is a source — its value is not driven by an incoming signal but by paper conditions.
        }

        private void ProcessInput(byte value)
        {
            if (signalState == value) return;

            if (value >= 1 && value <= 7)
            {
                remaining += 1 << (value - 1);
            }

            if (value == UNLIMITED_TRANSFER)
            {
                unlimited = true;
            }
            else if (signalState == UNLIMITED_TRANSFER && value != UNLIMITED_TRANSFER)
            {
                unlimited = false;
            }

            signalState = value;
            MarkDirty();
        }

        /// <summary>Sets the valve's Output anchor (a side effect of paper conditions, see spec §6/§7).</summary>
        public void SetOutput(byte value)
        {
            if (outputState == value) return;
            outputState = value;
            MarkDirty();
        }

        private void OnSignalNetworkTick()
        {
            BEBehaviorSignalConnector beb = GetBehavior<BEBehaviorSignalConnector>();
            if (beb == null) return;
            if (lastPushedOutput == outputState) return;

            ISignalNode node = beb.GetNodeAt(new NodePos(this.Pos, OUTPUT));
            if (node == null) return;

            signalMod.netManager.UpdateSource(node, outputState);
            lastPushedOutput = outputState;
            MarkDirty();
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            signalMod?.DisposeSignalTickListener(OnSignalNetworkTick);
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            signalMod?.DisposeSignalTickListener(OnSignalNetworkTick);
        }

        #endregion

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            ConditionsText = tree.GetString("conditionsText", null);
            unlimited = tree.GetBool("unlimited", false);
            remaining = tree.GetInt("remaining", 0);
            signalState = (byte)tree.GetInt("signalState", 0);
            outputState = (byte)tree.GetInt("outputState", 0);
            drainPulse = tree.GetInt("drainPulse", 0);
            flowPulse = tree.GetInt("flowPulse", 0);
            drainLiquid = tree.GetItemstack("drainLiquid");
            drainLiquid?.ResolveBlockOrItem(worldForResolving);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetString("conditionsText", ConditionsText);
            tree.SetBool("unlimited", unlimited);
            tree.SetInt("remaining", remaining);
            tree.SetInt("signalState", signalState);
            tree.SetInt("outputState", outputState);
            tree.SetInt("drainPulse", drainPulse);
            tree.SetInt("flowPulse", flowPulse);
            if (drainLiquid != null) tree.SetItemstack("drainLiquid", drainLiquid);
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
        }
    }
}
