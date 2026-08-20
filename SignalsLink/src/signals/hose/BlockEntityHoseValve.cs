using signals.src;
using signals.src.signalNetwork;
using SignalsLink.src.signals.paperConditions;
using System.Text;
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
        /// Server tick: while this valve has Input credit, pull liquid from the far end of its
        /// hose into its own host inventory. Alternation between two active valves (arbitration)
        /// comes in step 6; for now an Input-carrying valve simply pulls.
        /// </summary>
        private void MoveLiquid(float dt)
        {
            if (!HasInput) return;
            if (Api is not ICoreServerAPI) return;

            HoseNetworkMod hoseMod = Api.ModLoader.GetModSystem<HoseNetworkMod>();
            if (hoseMod == null) return;

            NodePos myAnchor = new NodePos(Pos, HOSE);
            NodePos far = hoseMod.GetOtherEndpoint(Api.World, myAnchor);
            if (far == null) return;

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
                if (hostInv == null) return; // wall without host → idle
            }

            // Contention only exists when the far end is ALSO an active valve; then the two
            // valves must take turns (arbitration), otherwise they fight and stall.
            bool contested = IsFarActiveValve(far);
            if (contested && !hoseMod.IsOnTurn(myAnchor, far)) return;

            decimal litres = unlimited ? maxLitresPerTick : System.Math.Min((decimal)remaining, maxLitresPerTick);

            var transfer = new HoseLiquidTransfer(Api, hostInv, hostPos, far, conditionsEvaluator, discard);
            HoseLiquidTransfer.Result result = transfer.TryMove(litres);

            if (result.HasExplicitOutput) SetOutput(result.OutputValue);

            bool moved = result.Transfer.Success;
            if (moved && !unlimited)
            {
                remaining -= result.Transfer.TriggerCost;
                if (remaining < 0) remaining = 0;
                MarkDirty();
            }

            // Occasional water splash while transporting (mirrors the chute's random sound).
            if (moved && Api.World.Rand.NextDouble() < 0.2)
            {
                Api.World.PlaySoundAt(waterSound, Pos, 0.0, range: 8f, volume: 0.5f);
            }

            // Alternation: pass the turn to the other valve when we can't move (source empty /
            // target full) or we finished our batch. Two facing valves then drain each other
            // in turns instead of both pulling at once.
            if (contested)
            {
                bool finishedBatch = !unlimited && remaining <= 0;
                if (!moved || finishedBatch) hoseMod.PassToken(myAnchor, far);
            }
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
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetString("conditionsText", ConditionsText);
            tree.SetBool("unlimited", unlimited);
            tree.SetInt("remaining", remaining);
            tree.SetInt("signalState", signalState);
            tree.SetInt("outputState", outputState);
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
        }
    }
}
