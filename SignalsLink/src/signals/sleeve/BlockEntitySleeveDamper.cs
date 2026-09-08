using signals.src;
using signals.src.signalNetwork;
using SignalsLink.src.signals.link;
using SignalsLink.src.signals.managedchute.transporting;
using SignalsLink.src.signals.paperConditions;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SignalsLink.src.signals.sleeve
{
    /// <summary>
    /// Damper block entity — the sleeve counterpart of <c>BlockEntityHoseValve</c>. Signals anchors:
    /// index 0 = Input, index 1 = Output (isSource). Index 2 = sleeve anchor (handled by the block).
    ///
    /// The convention is the same as the valve's: <b>a damper always pulls towards itself</b>, and
    /// the active one is whichever has an Input signal. Its own end is the block it is mounted on
    /// (a container), or — when it stands on the floor — the block it faces, which makes it a
    /// ground endpoint that places into and picks up from the world.
    /// </summary>
    public class BlockEntitySleeveDamper : BlockEntity, IBESignalReceptor, IPaperConditionsHost, ISignalBuffer, ILinkMountHost, IRetentionModeHost, IConditionOutputSink
    {
        public const int INPUT = 0;
        public const int OUTPUT = 1;
        public const int SLEEVE = 2;

        private const byte UNLIMITED_TRANSFER = 15;

        private int checkRateMs = 200;
        // Throughput cap: how many items this damper may move per tick. An atomic `amount N`
        // batch may overshoot it — it is one action, not N ticks worth of flow.
        private int maxItemsPerTick = 1;

        public byte signalState;
        private int remaining;
        private bool unlimited;

        /// <summary>Does this damper currently have Input credit (a batch, or continuous)?</summary>
        public bool HasInput => unlimited || remaining > 0;

        // Output anchor (index 1), driven by `output N` on paper (see IConditionOutputSink) and
        // pushed on the signal tick.
        public byte outputState;
        private byte? lastPushedOutput;

        private SignalNetworkMod signalMod;

        // Picked at random on each audible pulse, so a row of dampers does not clatter in unison.
        private static readonly AssetLocation[] transferSounds =
        {
            new AssetLocation("signalslink:sounds/effect/sleeve1"),
            new AssetLocation("signalslink:sounds/effect/sleeve2"),
            new AssetLocation("signalslink:sounds/effect/sleeve3"),
        };

        private string conditionsText = null;
        private PaperConditionsEvaluator conditionsEvaluator = new PaperConditionsEvaluator();

        // Flow pulse: bumped server-side whenever the transfer sound plays (synced to clients), so
        // the client can make the sleeve sway in time with the audible surge. flowFar is the anchor
        // at the far end of the segment that is actually flowing, so a damper with several sleeves
        // wobbles only that one; stored relative to Pos (see To/FromTreeAttributes).
        private int flowPulse;
        private int lastClientFlowPulse = -1;
        private NodePos flowFar;

        // Idle backoff: after repeated unproductive attempts (source empty, target full, no host,
        // dangling sleeve) the damper runs the heavier pull less often, then snaps back to full
        // rate as soon as something moves. Waiting for an arbitration turn does NOT back off.
        private int noWorkStreak;
        private int tickSkip;

        // How this damper treats the room boundary it sits in. Only meaningful for the ceiling
        // variant, which is the one that forms a full collar; kept for the others so the setting
        // survives a mount swap. The authoritative copy lives here, in the tree attributes; the
        // registry is only the lookup table Block.GetRetention can read.
        private int retentionMode = link.RetentionMode.Cooling;

        // Round-robin cursor over the damper's sleeve lines: the far anchor currently being served.
        // Stored as an anchor rather than an index, because the source list is rebuilt every tick.
        private NodePos currentSource;

        public int SignalInputsCount => 3; // 2 Signals anchors + 1 sleeve anchor (selection boxes 0..2)

        public string ConditionsText
        {
            get => conditionsText;
            set
            {
                conditionsText = value;
                conditionsEvaluator?.SetConditionsText(conditionsText);
                remaining = 0; // reconfiguring clears the pending batch (it may no longer be valid)
                MarkDirty();
            }
        }

        private PaperConditionsEvaluator ConditionsEvaluator => conditionsEvaluator;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            conditionsEvaluator?.SetConditionsText(conditionsText);

            signalMod = api.ModLoader.GetModSystem<SignalNetworkMod>();
            signalMod.RegisterSignalTickListener(OnSignalNetworkTick);

            PublishRetentionMode();

            if (api is ICoreServerAPI)
            {
                checkRateMs = Block?.Attributes?["item-checkrateMs"].AsInt(200) ?? 200;
                maxItemsPerTick = Block?.Attributes?["sleeve-items-per-tick"].AsInt(1) ?? 1;
                RegisterDelayedCallback(dt =>
                {
                    UpdateMountState();
                    RegisterGameTickListener(MoveItems, checkRateMs);
                }, 10 + api.World.Rand.Next(200));
            }
            else if (api is ICoreClientAPI)
            {
                RegisterGameTickListener(ClientFlowTick, 50);
            }
        }

        /// <summary>Called by the block when a neighbour changes — the host may have appeared/disappeared.</summary>
        public void OnNeighbourChanged(BlockPos neibpos)
        {
            if (Api is ICoreServerAPI) UpdateMountState();
        }

        #region Mount state (shape) — hung on host / stand (no host or floor) / roof (ceiling)

        /// <summary>
        /// Swaps the block to the correct mount variant: side=up → roof; a container to exchange
        /// cargo with → hung (no legs, it hangs off that container); otherwise → stand (legs).
        ///
        /// Unlike the valve this asks about the CARGO block rather than the block the damper sits
        /// on, which makes both placements behave alike: on a wall the cargo block is the block it
        /// hangs on, on the floor it is the block it faces. A damper standing on the floor is
        /// therefore the same stand variant, and it is the placement (side=down) — not the block
        /// code — that makes it a ground endpoint. One shape less to tell apart in the world.
        /// </summary>
        private void UpdateMountState()
        {
            if (Api is not ICoreServerAPI) return;

            Block cur = Api.World.BlockAccessor.GetBlock(Pos);
            string orientation = cur.Variant["orientation"];
            string side = cur.Variant["side"];
            if (orientation == null || side == null) return;

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
            if (side == "up") return "sleevedamperroof";                      // ceiling → collar shape
            return HasContainerHost() ? "sleevedamper" : "sleevedamperstand"; // hung / stand
        }

        /// <summary>Is there a container at the other end — the block this damper exchanges cargo with?</summary>
        private bool HasContainerHost()
        {
            BlockPos p = GetCargoPos(Api.World, Pos);
            return p != null
                && Api.World.BlockAccessor.GetBlockEntity(p) is IBlockEntityContainer c
                && c.Inventory != null;
        }

        #endregion

        #region Cargo position

        /// <summary>
        /// The block a damper exchanges cargo with: always the one it is mounted against. A damper
        /// standing on the floor is placed as a mount on the air block away from the player (see
        /// <c>BlockBehaviorLinkCover.floorAsWall</c>), so it needs no case of its own — that air
        /// block is what it puts cargo down on and picks it back up from.
        /// </summary>
        public static BlockPos GetCargoPos(IWorldAccessor world, BlockPos damperPos)
        {
            Block b = world.BlockAccessor.GetBlock(damperPos);
            string sideCode = b?.Variant?["side"];
            BlockFacing side = sideCode != null ? BlockFacing.FromCode(sideCode) : null;
            return side == null ? null : damperPos.AddCopy(side);
        }

        #endregion

        /// <summary>
        /// Server tick wrapper: gates the (heavier) pull behind an idle backoff so a damper that
        /// can't currently do anything stops re-resolving its source and conditions every tick.
        /// </summary>
        private void MoveItems(float dt)
        {
            if (Api is not ICoreServerAPI) return;

            // Blocks with an `output` action are evaluated on EVERY tick, before anything else and
            // whatever the Input pin says. That is the whole point of the damper having an Output
            // pin: it is meant to be able to watch its own end of the line and report on it, the
            // way a BlockSensor does. Hanging that off a transfer meant the pin froze the moment
            // there was nothing to carry — no Input, no sleeve, an empty source, a full target, or
            // simply waiting for its turn on the line.
            EvaluateOutputs();

            if (!HasInput) { noWorkStreak = 0; tickSkip = 0; return; } // truly idle → cheap no-op

            // A damper whose conditions produce an `output` has to stay responsive so the pin can
            // react the moment the source state changes; such dampers skip the backoff.
            if (conditionsEvaluator != null && conditionsEvaluator.HasAnyOutput)
            {
                TryPull();
                return;
            }

            int stride = 1 + System.Math.Min(noWorkStreak, 7);
            if (++tickSkip < stride) return;
            tickSkip = 0;

            int status = TryPull();
            if (status == 0) noWorkStreak = 0;                                        // moved → full rate
            else if (status == 1) noWorkStreak = System.Math.Min(noWorkStreak + 1, 64); // blocked → back off
            // status == 2 (waiting for our arbitration turn): keep the current rate so two facing
            // dampers keep alternating without lag.
        }

        /// <summary>
        /// Runs the paper conditions purely for their <c>output</c> blocks. Nothing is moved and no
        /// buffer is spent; the transfer is built only so the conditions see the same two ends they
        /// would during a real attempt.
        /// </summary>
        private void EvaluateOutputs()
        {
            // No `output` block on the paper means the pin is a constant zero, and a damper with
            // nothing on the other end of the sleeve has nothing to report either. Both are
            // written out rather than skipped: a pin that is merely left alone is how it used to
            // freeze on a stale value.
            if (conditionsEvaluator == null || !conditionsEvaluator.HasAnyOutput) { SetOutput(0); return; }

            LinkNetworkMod linkMod = Api.ModLoader.GetModSystem<LinkNetworkMod>();
            if (linkMod == null) { SetOutput(0); return; }

            BlockPos myCargoPos = GetCargoPos(Api.World, Pos);
            if (myCargoPos == null) { SetOutput(0); return; }

            List<LinkSource> sources = linkMod.GetOtherEndpoints(Api.World, new NodePos(Pos, SLEEVE));
            if (sources.Count == 0) { SetOutput(0); return; }

            // Whichever source the rotation is on, so `in source` means the same thing here as it
            // would in a transfer.
            int index = sources.FindIndex(s => s.Endpoint == currentSource);
            BlockPos farCargoPos = GetCargoPos(Api.World, sources[index < 0 ? 0 : index].Endpoint.blockPos);
            if (farCargoPos == null) return;

            ItemTransferFactory
                .CreateTransfer(Api, farCargoPos, myCargoPos, 0, 0, ConditionsEvaluator, this)
                ?.EvaluateOutputs();
        }

        /// <summary>
        /// One pull attempt. The damper may have several sleeves on its anchor; it round-robins over
        /// the connected sources, staying on one for as long as it keeps delivering.
        /// </summary>
        /// <returns>0 = moved, 1 = blocked (nothing to do), 2 = waiting for our arbitration turn.</returns>
        private int TryPull()
        {
            LinkNetworkMod linkMod = Api.ModLoader.GetModSystem<LinkNetworkMod>();
            if (linkMod == null) return 1;

            NodePos myAnchor = new NodePos(Pos, SLEEVE);
            List<LinkSource> sources = linkMod.GetOtherEndpoints(Api.World, myAnchor);
            if (sources.Count == 0) return 1;

            BlockPos myCargoPos = GetCargoPos(Api.World, Pos);
            if (myCargoPos == null) return 1;

            int budget = unlimited ? maxItemsPerTick : System.Math.Min(maxItemsPerTick, remaining);
            if (budget <= 0) return 1;

            // Resume where the cursor left off. Stored as the far anchor rather than an index,
            // because the source list is rebuilt every tick and its length may change.
            int start = sources.FindIndex(s => s.Endpoint == currentSource);
            if (start < 0) start = 0;

            bool waiting = false;

            // At most one full rotation per tick: with four empty sources and one full, the full one
            // must still be found within the same tick or throughput would collapse.
            for (int i = 0; i < sources.Count; i++)
            {
                LinkSource candidate = sources[(start + i) % sources.Count];
                int status = TryPullFrom(linkMod, myAnchor, candidate, myCargoPos, budget);

                if (status == 0)
                {
                    currentSource = candidate.Endpoint; // keep draining this source while it delivers
                    return 0;
                }

                if (status == 2) waiting = true; // the far damper holds the turn; try another source
            }

            // Nothing anywhere. Park the cursor on the next source so the next tick starts there and
            // one stuck line cannot pin the rotation.
            currentSource = sources[(start + 1) % sources.Count].Endpoint;
            return waiting ? 2 : 1;
        }

        /// <summary>One pull attempt from a single source (the far endpoint of one sleeve line).</summary>
        /// <returns>0 = moved, 1 = nothing to pull here, 2 = waiting for our arbitration turn.</returns>
        private int TryPullFrom(LinkNetworkMod linkMod, NodePos myAnchor, LinkSource source, BlockPos myCargoPos, int budget)
        {
            NodePos far = source.Endpoint;

            // Contention only exists when the far end is ALSO an active damper; then the two must
            // take turns (arbitration), otherwise they fight and stall.
            bool contested = IsFarActiveDamper(far);
            if (contested && !linkMod.IsOnTurn(myAnchor, far)) return 2;

            // Everything from here on has to reach the arbitration hand-off at the bottom. Bailing
            // out early while holding the turn deadlocks the line: the other damper is told to wait
            // for a turn that is never passed on, and neither of them ever moves anything again.
            BlockPos farCargoPos = GetCargoPos(Api.World, far.blockPos);

            // Deliberately never cached: the source changes with the rotation, and a cached transfer
            // would keep a reference to an inventory that a chunk reload has already replaced —
            // exactly the dead-inventory bug the chute had to grow an identity check for.
            // Slot signals are 0: the damper has no Source/Target pins, slot choice is done with the
            // `source N` / `target N` directives on paper.
            // Passing ourselves as the output sink is what lets `output N` on paper drive the
            // Output pin; the ManagedChute passes none, because it has no such pin.
            IItemTransfer transfer = farCargoPos == null
                ? null
                : ItemTransferFactory.CreateTransfer(Api, farCargoPos, myCargoPos, 0, 0, ConditionsEvaluator, this);

            bool moved = false;
            decimal movedTotal = 0;

            if (transfer != null)
            {
                ItemStackMoveOperation op = new ItemStackMoveOperation(
                    Api.World, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, 1);

                while (movedTotal < budget)
                {
                    TransferOperationResult result = transfer.TryMove(op);
                    if (!result.Success) break;

                    moved = true;
                    movedTotal += result.MovedAmount;

                    if (!unlimited)
                    {
                        remaining -= result.TriggerCost;
                        if (remaining < 0) remaining = 0; // an atomic amount-op may overshoot the last bit
                        MarkDirty();
                        if (remaining <= 0) break;
                    }
                }
            }

            // Occasional rustle while transporting. The sleeve sways in sync with this audible
            // pulse (see flowPulse → client TriggerWobble).
            if (moved && Api.World.Rand.NextDouble() < 0.2)
            {
                AssetLocation sound = transferSounds[Api.World.Rand.Next(transferSounds.Length)];
                Api.World.PlaySoundAt(sound, Pos, 0.0, range: 8f, volume: 0.5f);
                flowFar = source.FirstHop; // only this segment should wobble
                flowPulse++;
                MarkDirty();
            }

            // Alternation: pass the turn to the other damper when we can't move or we finished our
            // batch, so two facing dampers feed each other in turns instead of both pulling at once.
            if (contested)
            {
                bool finishedBatch = !unlimited && remaining <= 0;
                if (!moved || finishedBatch) linkMod.PassToken(myAnchor, far);
            }

            return moved ? 0 : 1;
        }

        /// <summary>Is the far endpoint another damper that currently has Input (i.e. contends for the line)?</summary>
        private bool IsFarActiveDamper(NodePos far)
        {
            return Api.World.BlockAccessor.GetBlockEntity(far.blockPos) is BlockEntitySleeveDamper d && d.HasInput;
        }

        private void ClientFlowTick(float dt)
        {
            if (Api is not ICoreClientAPI) return;

            if (flowPulse != lastClientFlowPulse)
            {
                lastClientFlowPulse = flowPulse;
                Api.ModLoader.GetModSystem<LinkNetworkMod>()?.Renderer?.TriggerWobble(new NodePos(Pos, SLEEVE), flowFar);
            }
        }

        #region Room sealing (ceiling variant)

        public bool SupportsRetentionMode => Block?.Code?.FirstCodePart() == "sleevedamperroof";

        public int RetentionMode => retentionMode;

        public void CycleRetentionMode()
        {
            retentionMode = link.RetentionMode.Next(retentionMode);
            PublishRetentionMode();
            MarkDirty();
        }

        private void PublishRetentionMode()
        {
            Api?.ModLoader.GetModSystem<RetentionModeRegistry>()?.Publish(Pos, retentionMode);
        }

        private void WithdrawRetentionMode()
        {
            Api?.ModLoader.GetModSystem<RetentionModeRegistry>()?.Withdraw(Pos);
        }

        #endregion

        #region Signals

        public void OnValueChanged(NodePos pos, byte value)
        {
            if (pos.index == INPUT) ProcessInput(value);
            // Output (index 1) is a source — it is not driven by an incoming signal.
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

        /// <summary>Sets the damper's Output anchor.</summary>
        public void SetOutput(byte value)
        {
            if (outputState == value) return;
            outputState = value;
            MarkDirty();
        }

        /// <summary>
        /// What one evaluation pass computed for the pin, including the 0 of a pass in which no
        /// `output` block held. See <see cref="IConditionOutputSink"/>: the pin is a function of
        /// the current state, so it is written on every pass and never left holding a stale value.
        /// </summary>
        public void ApplyOutput(int pin, byte value)
        {
            SetOutput(value);
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
            WithdrawRetentionMode();
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            signalMod?.DisposeSignalTickListener(OnSignalNetworkTick);
            WithdrawRetentionMode();
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
            flowPulse = tree.GetInt("flowPulse", 0);
            retentionMode = tree.GetInt("retentionMode", link.RetentionMode.Cooling);
            PublishRetentionMode(); // no-op before Initialize; republished from there

            // Far anchor of the flowing segment, stored as an offset from Pos. Index < 0 = none.
            int flowFarIndex = tree.GetInt("flowFarIndex", -1);
            flowFar = flowFarIndex < 0 || Pos == null
                ? null
                : new NodePos(Pos.AddCopy(tree.GetInt("flowFarDX", 0), tree.GetInt("flowFarDY", 0), tree.GetInt("flowFarDZ", 0)), flowFarIndex);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetString("conditionsText", ConditionsText);
            tree.SetBool("unlimited", unlimited);
            tree.SetInt("remaining", remaining);
            tree.SetInt("signalState", signalState);
            tree.SetInt("outputState", outputState);
            tree.SetInt("flowPulse", flowPulse);
            tree.SetInt("retentionMode", retentionMode);

            tree.SetInt("flowFarIndex", flowFar == null ? -1 : flowFar.index);
            if (flowFar != null && Pos != null)
            {
                tree.SetInt("flowFarDX", flowFar.blockPos.X - Pos.X);
                tree.SetInt("flowFarDY", flowFar.blockPos.Y - Pos.Y);
                tree.SetInt("flowFarDZ", flowFar.blockPos.Z - Pos.Z);
            }
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);

            var selection = forPlayer?.CurrentBlockSelection;
            if (selection?.SelectionBoxIndex < SignalInputsCount) return;

            // Show the Input buffer only when targeting the damper body.
            if (unlimited) dsc.AppendLine(Lang.Get("signalslink:managedchute-info-unlimited"));
            else if (remaining > 0) dsc.AppendLine(Lang.Get("signalslink:managedchute-info-remaining", remaining));

            if (SupportsRetentionMode)
            {
                dsc.AppendLine(Lang.Get("signalslink:retention-label",
                    Lang.Get(link.RetentionMode.LangKey(retentionMode))));
            }
        }

        /// <summary>Wrench (sneak) clears the pending buffer and stops continuous mode.</summary>
        public void ClearBuffer()
        {
            if (remaining == 0 && !unlimited) return;
            remaining = 0;
            unlimited = false;
            MarkDirty();
        }
    }
}
