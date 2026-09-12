using System;
using System.Collections.Generic;
using System.Text;
using signals.src;
using signals.src.signalNetwork;
using SignalsLink.src.signals.cargo;
using SignalsLink.src.signals.managedchute.transporting;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.manageddock
{
    /// <summary>
    /// The freight dock: a router, not a warehouse. A header names both ends
    /// (<c>from train to yard</c>); <c>unload X</c> / <c>load X</c> are the short forms where the
    /// crate is one of them. Server side only.
    /// </summary>
    public class BEManagedDock : BlockEntityOpenableContainer, IBESignalReceptor, IPaperConditionsHost, ISignalBuffer, IConditionOutputSink
    {
        public const int SlotCount = 48;

        private const byte InputPin = 0;
        private const byte OutputPin = 1;
        private const byte UnlimitedTransfer = 15;

        /// <summary>How often the dock looks around, by what it found last time.</summary>
        private const int IdleRateMs = DockTickRate.IdleMs;      // nothing found: only looking
        private const int WaitingRateMs = DockTickRate.WaitingMs; // something found: watching for it

        private InventoryGeneric inventory;
        private long tickListenerId;
        private int tickRateMs;

        private byte signalState;
        private int remaining;
        private bool unlimited;

        public byte outputState;
        private byte? lastPushedOutput;
        private SignalNetworkMod signalMod;

        private string conditionsText;
        private PaperConditionsEvaluator conditionsEvaluator;

        // One evaluator per section, so whole-paper machinery works on a section unchanged.
        private readonly Dictionary<string, PaperConditionsEvaluator> sectionEvaluators = new Dictionary<string, PaperConditionsEvaluator>();

        private CargoHolderRegistry registry;

        public override InventoryBase Inventory => inventory;

        public override string InventoryClassName => "manageddock";

        public int SignalInputsCount => 2;   // Input, Output

        /// <summary>A dock knows two ends, so every block has to say which way it is going.</summary>
        public bool SupportsSections => true;
        public bool RequiresSections => true;

        /// <summary>
        /// Judges both ends while the paper is being read, so an unknown holder is a reported
        /// mistake rather than a dock quietly doing nothing.
        /// </summary>
        public void CheckHeader(ConditionSection section, PaperErrorSink errors)
        {
            if (registry == null) return;

            if (section.SourceTokens.Count > 0) registry.TryResolve(section.SourceTokens, section.Header, errors, out _);
            if (section.TargetTokens.Count > 0) registry.TryResolve(section.TargetTokens, section.Header, errors, out _);
        }

        public BEManagedDock()
        {
            // NOTE: the id must contain a dash - VS splits className/instanceId on it.
            inventory = new InventoryGeneric(SlotCount, "manageddock-0", null,
                (id, inv) => new ItemSlotGoodsOrLiquid(inv));
        }

        public string ConditionsText
        {
            get => conditionsText;
            set
            {
                conditionsText = value;
                conditionsEvaluator?.SetConditionsText(conditionsText);
                sectionEvaluators.Clear();
                holders.Clear();      // a new paper may name a different yard
                remaining = 0;      // reconfiguring drops the pending batch; it may make no sense now
                MarkDirty();
            }
        }

        public void ClearBuffer()
        {
            if (remaining == 0 && !unlimited) return;

            remaining = 0;
            unlimited = false;
            MarkDirty();
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            inventory.LateInitialize("manageddock-" + Pos.X + "/" + Pos.Y + "/" + Pos.Z, api);
            inventory.Pos = Pos;
            inventory.ResolveBlocksOrItems();

            signalMod = api.ModLoader.GetModSystem<SignalNetworkMod>();
            signalMod.RegisterSignalTickListener(OnSignalNetworkTick);

            registry = api.ModLoader.GetModSystem<CargoHolderRegistry>();

            ownHolds = new ICargoHold[] { new DockHold(this) };

            if (api is ICoreServerAPI)
            {
                RegisterDelayedCallback(dt => SetTickRate(IdleRateMs), 10 + api.World.Rand.Next(200));
            }
        }

        // ---------------------------------------------------------------- the tick

        /// <summary>
        /// A tick listener cannot be re-rated while it runs, so it is registered again.
        /// </summary>
        private void SetTickRate(int rateMs)
        {
            if (tickRateMs == rateMs && tickListenerId != 0) return;

            if (tickListenerId != 0) UnregisterGameTickListener(tickListenerId);

            tickRateMs = rateMs;
            tickListenerId = RegisterGameTickListener(OnServerTick, rateMs);
        }

        private void OnServerTick(float dt)
        {
            if (Api?.World == null || Api is not ICoreServerAPI) return;

            // Starts BEFORE the early outs: "no credit" is exactly what this gets switched on for.
            bool trace = ConditionDebug.IsMarked(conditionsText);
            if (trace) ConditionDebug.Begin(Api.Logger, "dock@" + Pos);

            try
            {
                Tick();
            }
            finally
            {
                if (trace) ConditionDebug.End();
            }
        }

        private void Tick()
        {
            IReadOnlyList<ConditionSection> sections = ConditionsEvaluator.GetSections();

            if (sections == null || sections.Count == 0)
            {
                if (ConditionDebug.Enabled) ConditionDebug.Log("no paper");
                SetOutput(0);
                SetTickRate(IdleRateMs);
                return;
            }

            // An idle dock must not cost a flood fill of the yard five times a second.
            if (!unlimited && remaining <= 0 && !ConditionsEvaluator.HasAnyOutput)
            {
                if (ConditionDebug.Enabled) ConditionDebug.Log("no credit on the Input pin and no output block, so nothing to do");
                SetOutput(0);
                SetTickRate(IdleRateMs);
                return;
            }

            RunSections(sections);
        }

        private void RunSections(IReadOnlyList<ConditionSection> sections)
        {
            bool sawHolder = false;
            searchedLive = false;

            byte output = DockPass.Run(sections,
                (section, actionsBlocked) => RunSection(section, actionsBlocked, ref sawHolder),
                out bool actionPerformed);

            SetOutput(output);
            SetTickRate(DockTickRate.Next(sawHolder, actionPerformed, searchedLive));
        }

        /// <summary>
        /// One section. Null when there is nothing to run against - a train that has not arrived
        /// must not stop the section below it.
        /// </summary>
        private SectionPassResult RunSection(ConditionSection section, bool actionsBlocked, ref bool sawHolder)
        {
            if (section.Direction == null) return null;      // no header: reported as a paper error
            if (!section.EndsAreComplete) return null;       // half-written header: likewise

            IReadOnlyList<ICargoHold> sources =
                CompositeHold.Over(Api, EndOf(section, section.SourceTokens, ref sawHolder, out string sourceWhy));

            // The far end is not looked for when the near one failed, and the trace must not
            // report that as a second failure - it sent me hunting for a fault that was not there.
            string targetWhy = "(not looked at)";
            IReadOnlyList<ICargoHold> targets = sources == null
                ? null
                : CompositeHold.Over(Api, EndOf(section, section.TargetTokens, ref sawHolder, out targetWhy));

            if (ConditionDebug.Enabled)
            {
                ConditionDebug.Log("section '" + section.Header + "'"
                    + " source=" + Describe(section.SourceTokens, sources, sourceWhy)
                    + " target=" + Describe(section.TargetTokens, targets, targetWhy));
            }

            if (sources == null || targets == null) return null;

            PaperConditionsEvaluator evaluator = EvaluatorFor(section);
            DriverResult output = RunOutputRail(evaluator, sources, targets);

            bool hasCredit = unlimited || remaining > 0;
            bool moved = !actionsBlocked && hasCredit && Move(evaluator, sources, targets);

            return new SectionPassResult(moved, output.HasOutput(), output.GetOutput());
        }

        /// <summary>
        /// How one end of a header turned out, for the trace.
        ///
        /// The reason is the point. "not found" and "found but not standing" are one word apart
        /// and hours apart to debug.
        /// </summary>
        private static string Describe(IReadOnlyList<string> tokens, IReadOnlyList<ICargoHold> holds, string why)
        {
            string what = tokens.Count == 0 ? "<the crate>" : string.Join(" ", tokens);

            if (why != null) return what + " " + why;

            // One hold names itself; several are only ever several because they were not composed.
            return what + " (" + (holds.Count == 1 ? holds[0].Code : holds.Count + " holds") + ")";
        }

        /// <summary>
        /// This crate when the header said nothing there, otherwise the holder it named. Null when
        /// that holder is missing or not ready; <paramref name="why"/> says which.
        /// </summary>
        private IReadOnlyList<ICargoHold> EndOf(ConditionSection section, IReadOnlyList<string> tokens,
            ref bool sawHolder, out string why)
        {
            why = null;

            if (tokens.Count == 0) return ownHolds;

            ICargoHolder holder = FindHolder(section, tokens);

            if (holder == null)
            {
                why = "NOT FOUND";
                return null;
            }

            sawHolder = true;

            if (!holder.IsReady)
            {
                why = "found but NOT READY (a train has to be standing)";
                return null;
            }

            if (holder.Holds.Count == 0)
            {
                why = "found but has NO HOLDS";
                return null;
            }

            return holder.Holds;
        }

        /// <summary>The crate itself, as a hold like any other.</summary>
        private sealed class DockHold : ICargoHold
        {
            private readonly BEManagedDock dock;

            public DockHold(BEManagedDock dock)
            {
                this.dock = dock;
            }

            public string Code => "dock";
            public IInventory Inventory => dock.inventory;
            public BlockPos Pos => dock.Pos;

            // A container that stands somewhere: the position serves blocks, the slots wagons.
            public bool IsContainer => true;

            public bool IsEmpty => dock.inventory.Empty;

            public void MarkDirty()
            {
                dock.MarkDirty();
            }
        }

        private IReadOnlyList<ICargoHold> ownHolds;

        /// <summary>
        /// The other party of one section, kept between ticks because searching is much the most
        /// expensive thing the dock does. Only what stands on it is re-read each tick
        /// (<see cref="ICargoHolder.Refresh"/>).
        /// </summary>
        private ICargoHolder FindHolder(ConditionSection section, IReadOnlyList<string> tokens)
        {
            long now = Api.World.ElapsedMilliseconds;
            string key = section.Header + " " + string.Join(" ", tokens);

            if (holders.TryGetValue(key, out HolderSearch cached) && now - cached.FoundAt < HolderCacheMs)
            {
                cached.Holder?.Refresh();
                return cached.Holder;
            }

            ICargoHolder holder = null;
            bool cacheable = true;

            if (registry != null && registry.TryResolve(tokens, section.Header, null, out CargoHolderRequest request))
            {
                request.Finder.TryFind(Api.World, Pos, request.Selector, out holder);
                cacheable = request.Finder.Cacheable;
            }

            // A remembered wagon is one the goods might be written into after it has left.
            // A search that found nothing is worth remembering too.
            if (cacheable)
            {
                holders[key] = new HolderSearch(holder, now);
            }
            else
            {
                holders.Remove(key);

                // What the beat is held back for: this runs again next tick, whole.
                searchedLive = true;
            }

            return holder;
        }

        /// <summary>How long a found holder is trusted before the world is asked again.</summary>
        private const long HolderCacheMs = 3000;

        /// <summary>Reset each pass: what THIS one did, not what the dock generally does.</summary>
        private bool searchedLive;

        private readonly Dictionary<string, HolderSearch> holders = new Dictionary<string, HolderSearch>();

        private sealed class HolderSearch
        {
            public ICargoHolder Holder { get; }
            public long FoundAt { get; }

            public HolderSearch(ICargoHolder holder, long foundAt)
            {
                Holder = holder;
                FoundAt = foundAt;
            }
        }

        /// <summary>It might have been the paving, so look again next tick.</summary>
        public void OnNeighbourBlockChange(BlockPos neibpos)
        {
            holders.Clear();
        }

        /// <summary>
        /// The output rail, answered against the holder as a whole rather than whichever corner of
        /// it is being filled.
        /// </summary>
        private DriverResult RunOutputRail(PaperConditionsEvaluator evaluator,
            IReadOnlyList<ICargoHold> sources, IReadOnlyList<ICargoHold> targets)
        {
            IReadOnlyList<ConditionBlock> blocks = evaluator.GetBlocks();
            if (blocks == null || blocks.Count == 0) return DriverResult.Nothing;

            IDictionary<string, object> ctx = ConditionContext.Build(Api, null,
                Composed(sources), sources[0].Pos, Composed(targets), targets[0].Pos);

            return ConditionDriver.Run(blocks, true, block => block.OutputConditionsHold(ctx), null);
        }

        private CompositeInventory Composed(IReadOnlyList<ICargoHold> holds)
        {
            CompositeInventory composite = new CompositeInventory(Api);
            foreach (ICargoHold hold in holds) composite.Append(hold);

            return composite;
        }

        /// <summary>
        /// Moves one action's worth of goods: holds in order, first one that can do something wins.
        ///
        /// The default amount is everything that matches, not one piece - an <c>unload</c> is meant
        /// to empty the wagon. One action costs one credit whatever it carried.
        /// </summary>
        private bool Move(PaperConditionsEvaluator evaluator,
            IReadOnlyList<ICargoHold> sources, IReadOnlyList<ICargoHold> targets)
        {
            ItemStackMoveOperation op = new ItemStackMoveOperation(
                Api.World, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, EverythingThatMatches);

            int tried = 0;

            foreach ((ICargoHold source, ICargoHold target) in DockPairs.Order(sources, targets, MaxPairsPerTick))
            {
                if (ReferenceEquals(source, target)) continue;

                tried++;

                IItemTransfer transfer = TransferFor(source, target, evaluator);

                if (transfer == null)
                {
                    if (ConditionDebug.Enabled) ConditionDebug.Log("  " + source.Code + " -> " + target.Code + ": no transfer for these two ends");
                    continue;
                }

                TransferOperationResult result = transfer.TryMove(op);

                if (!result.Success)
                {
                    if (ConditionDebug.Enabled) ConditionDebug.Log("  " + source.Code + " -> " + target.Code + ": " + transfer.GetType().Name + " moved nothing");
                    continue;
                }

                source.MarkDirty();
                target.MarkDirty();

                if (!unlimited)
                {
                    remaining--;
                    if (remaining < 0) remaining = 0;
                }

                MarkDirty();
                return true;
            }

            if (ConditionDebug.Enabled)
            {
                ConditionDebug.Log("  nothing moved; " + tried + " pair(s) tried, "
                    + sources.Count + " source hold(s), " + targets.Count + " target hold(s)");
            }

            return false;
        }

        /// <summary>How many pairs one tick may try before giving up until the next one.</summary>
        private const int MaxPairsPerTick = 64;

        /// <summary>The transfer between two holds; see <see cref="TransferRouting"/>.</summary>
        private IItemTransfer TransferFor(ICargoHold source, ICargoHold target, PaperConditionsEvaluator evaluator)
        {
            switch (TransferRouting.Between(source, target))
            {
                case TransferRoute.BetweenPlaces:
                {
                    IItemTransfer transfer = ItemTransferFactory.CreateTransfer(Api, source.Pos, target.Pos, 0, 0, evaluator);
                    Imply(transfer, target);

                    return transfer;
                }

                case TransferRoute.BetweenInventories:
                {
                    InventoryToInventoryTransfer transfer =
                        new InventoryToInventoryTransfer(Api, source.Inventory, target.Inventory, null, 0, 0, evaluator);

                    transfer.CarriesLiquid = true;

                    return transfer;
                }

                case TransferRoute.InventoryToPlace:
                {
                    InventoryToWorldTransfer transfer = new InventoryToWorldTransfer(Api, source.Inventory, 0, target.Pos, 0, evaluator);
                    Imply(transfer, target);

                    return transfer;
                }

                case TransferRoute.PlaceToInventory:
                    return new WorldToInventoryTransfer(Api, source.Pos, target.Inventory, 0, evaluator);
            }

            return null;
        }

        /// <summary>
        /// Under a header the ground is implied, so the player need not write <c>target ground</c>.
        /// Only where the goods really do lie on the ground.
        /// </summary>
        private static void Imply(IItemTransfer transfer, ICargoHold target)
        {
            // The dock carries liquid; the chute and the damper do not, and the factory does not
            // know which device asked it for a transfer.
            if (transfer is InventoryToInventoryTransfer between) between.CarriesLiquid = true;

            if (transfer is not InventoryToWorldTransfer world || target is DockHold) return;

            world.GroundImplied = true;

            // A yard column: one kind of goods per column, stacking goods only.
            // See InventoryToWorldTransfer.
            world.YardColumn = true;
        }

        /// <summary>Big enough to mean "as much as there is", small enough not to overflow anything.</summary>
        private const int EverythingThatMatches = 100000;

        private PaperConditionsEvaluator ConditionsEvaluator
        {
            get
            {
                if (conditionsEvaluator == null)
                {
                    conditionsEvaluator = new PaperConditionsEvaluator();
                    conditionsEvaluator.SetConditionsText(ConditionsText);
                }

                return conditionsEvaluator;
            }
        }

        private PaperConditionsEvaluator EvaluatorFor(ConditionSection section)
        {
            if (sectionEvaluators.TryGetValue(section.Header, out PaperConditionsEvaluator evaluator)) return evaluator;

            evaluator = new PaperConditionsEvaluator();
            evaluator.SetConditionsText(section.Text);
            sectionEvaluators[section.Header] = evaluator;

            return evaluator;
        }

        /// <summary>Opens the crate: one undivided inventory, six rows of eight.</summary>
        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            // The server has to be told the inventory is in use.
            if (Api.Side == EnumAppSide.Server)
            {
                byPlayer.InventoryManager?.OpenInventory(Inventory);
                return true;
            }

            Vintagestory.API.Client.ICoreClientAPI capi = Api as Vintagestory.API.Client.ICoreClientAPI;
            if (capi == null) return true;

            if (invDialog == null)
            {
                invDialog = new Vintagestory.API.Client.GuiDialogBlockEntityInventory(
                    Lang.Get("signalslink:manageddock-title"), Inventory, Pos, 8, capi);

                invDialog.OnClosed += () =>
                {
                    invDialog = null;
                    capi.Network.SendBlockEntityPacket(Pos, (int)Vintagestory.API.Client.EnumBlockEntityPacketId.Close);
                    byPlayer.InventoryManager?.CloseInventory(Inventory);
                };

                invDialog.TryOpen();

                capi.Network.SendPacketClient(Inventory.Open(byPlayer));
                byPlayer.InventoryManager?.OpenInventory(Inventory);
            }
            else
            {
                invDialog.TryClose();
            }

            Api.Logger.Notification("[SignalsLink] dock dialog on " + Api.Side
                + ": inventory=" + Inventory.Count + " slots, dialog=" + (invDialog == null ? "closed" : "open"));

            return true;
        }

        // ---------------------------------------------------------------- signals

        public void OnValueChanged(NodePos pos, byte value)
        {
            if (pos.index != InputPin) return;
            if (signalState == value) return;

            if (value >= 1 && value <= 7) remaining += 1 << (value - 1);

            if (value == UnlimitedTransfer)
            {
                unlimited = true;
            }
            else if (signalState == UnlimitedTransfer)
            {
                unlimited = false;   // leaving 15 closes continuous mode but keeps the credit
            }

            signalState = value;
            MarkDirty();
        }

        public void SetOutput(byte value)
        {
            if (outputState == value) return;

            outputState = value;
            MarkDirty();
        }

        public void ApplyOutput(int pin, byte value)
        {
            SetOutput(value);
        }

        private void OnSignalNetworkTick()
        {
            BEBehaviorSignalConnector beb = GetBehavior<BEBehaviorSignalConnector>();
            if (beb == null) return;
            if (lastPushedOutput == outputState) return;

            ISignalNode node = beb.GetNodeAt(new NodePos(Pos, OutputPin));
            if (node == null) return;

            signalMod.netManager.UpdateSource(node, outputState);
            lastPushedOutput = outputState;
            MarkDirty();
        }

        // ---------------------------------------------------------------- lifecycle

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

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);

            ConditionsText = tree.GetString("conditionsText", null);

            unlimited = tree.GetBool("unlimited", false);
            remaining = tree.GetInt("remaining", 0);

            // Saved with the credit, or the pin looks like it went 0 -> N after every load.
            signalState = (byte)tree.GetInt("signalState", 0);
            outputState = (byte)tree.GetInt("outputState", 0);

            MeshAngle = tree.GetFloat("meshAngle", MeshAngle);
        }

        /// <summary>
        /// Which way it was set down, in radians. Kept here rather than in a block variant, the way
        /// a crate or a chest does it: four codes cannot say "roughly facing me", and a variant per
        /// angle would be a hundred blocks in the creative inventory.
        /// </summary>
        public float MeshAngle { get; set; }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            tree.SetString("conditionsText", ConditionsText);
            tree.SetBool("unlimited", unlimited);
            tree.SetInt("remaining", remaining);
            tree.SetInt("signalState", signalState);
            tree.SetInt("outputState", outputState);
            tree.SetFloat("meshAngle", MeshAngle);
        }

        /// <summary>
        /// Drawn from here rather than with the chunk, because the angle is this block's own. The
        /// unrotated mesh is built once per wood and shared; only the turning is per crate.
        /// </summary>
        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
        {
            if (Block == null || Api is not ICoreClientAPI) return false;

            tesselator.TesselateBlock(Block, out MeshData mesh);
            if (mesh == null) return false;

            // Drawn unturned while turning is off, so a crate set down before it was switched off
            // does not stand at an angle with its wires running to the wrong corner.
            mesher.AddMeshData(BlockManagedDock.Turning ? mesh.Rotate(Origin, 0, MeshAngle, 0) : mesh);

            return true;
        }

        /// <summary>The middle of the block, which is what everything here turns about.</summary>
        public static readonly Vec3f Origin = new Vec3f(0.5f, 0.5f, 0.5f);

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            BlockSelection selection = forPlayer?.CurrentBlockSelection;

            // Looking at a wire anchor should say what the anchor is and nothing else. Handing the
            // base class the question here was the fault: this crate IS a container, so the base
            // answered with everything in it.
            if (selection?.SelectionBoxIndex < SignalInputsCount) return;

            if (unlimited)
            {
                dsc.AppendLine(Lang.Get("signalslink:managedchute-info-unlimited"));
            }
            else if (remaining > 0)
            {
                dsc.AppendLine(Lang.Get("signalslink:managedchute-info-remaining", remaining));
            }

            base.GetBlockInfo(forPlayer, dsc);
        }
    }
}
