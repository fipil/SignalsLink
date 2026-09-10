using System;
using System.Collections.Generic;
using System.Text;
using signals.src;
using signals.src.signalNetwork;
using SignalsLink.src.signals.cargo;
using SignalsLink.src.signals.managedchute.transporting;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.manageddock
{
    /// <summary>
    /// The freight dock: it moves goods between whatever is standing around it — a storage yard
    /// today, and whatever else registers a holder later.
    ///
    /// <b>A router, not a warehouse.</b> A header names BOTH ends (<c>from train to yard</c>), so
    /// goods go from one other party straight to another without being carried through this crate.
    /// <c>unload X</c> and <c>load X</c> are the short ways of saying "to me" and "from me", and
    /// then the crate is simply one of the two ends. Every condition and directive means what it
    /// always meant; the header only says what <c>in source</c> and <c>in target</c> point at.
    ///
    /// Server side only. Writing into somebody else's inventory from the client is how items get
    /// duplicated.
    /// </summary>
    public class BEManagedDock : BlockEntityOpenableContainer, IBESignalReceptor, IPaperConditionsHost, ISignalBuffer, IConditionOutputSink
    {
        public const int SlotCount = 48;

        private const byte InputPin = 0;
        private const byte OutputPin = 1;
        private const byte UnlimitedTransfer = 15;

        /// <summary>
        /// How often the dock looks around, by what it found last time.
        ///
        /// A yard does not walk away and a train does not arrive between two frames, so scanning at
        /// full speed all the time would be paying for nothing. Working at one second, on the other
        /// hand, would make loading a train painful.
        /// </summary>
        private const int IdleRateMs = 1000;      // nothing found: only looking
        private const int WaitingRateMs = 200;    // something found: watching for it to be ready
        private const int WorkingRateMs = 50;     // moving goods

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

        // One evaluator per section, so that everything that already works on a whole paper —
        // transfers, directives, actions — works on a section without knowing what a section is.
        private readonly Dictionary<string, PaperConditionsEvaluator> sectionEvaluators = new Dictionary<string, PaperConditionsEvaluator>();

        private CargoHolderRegistry registry;

        public override InventoryBase Inventory => inventory;

        public override string InventoryClassName => "manageddock";

        public int SignalInputsCount => 2;   // Input, Output

        /// <summary>A dock knows two ends, so every block has to say which way it is going.</summary>
        public bool SupportsSections => true;
        public bool RequiresSections => true;

        /// <summary>
        /// Judges the two ends of a header while the paper is being read, so that a holder
        /// nobody has ever heard of is a mistake the player is told about - not a dock that
        /// stands there doing nothing with a paper that reads perfectly well.
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
                (id, inv) => new ItemSlotUniversal(inv));
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
        /// Changes how often the dock runs. A tick listener cannot be re-rated while it runs, so it
        /// is thrown away and registered again — there are only a handful of these changes, so it
        /// is cheaper than running fast and counting skipped ticks.
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

            // `# debug` on the paper turns on the same per-device trace the damper has, and it
            // starts BEFORE the early outs: a dock that does nothing because it has no credit is
            // exactly the case somebody switches this on for.
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

            // Nothing to carry and nothing to report: an idle dock costs a tick of nothing, rather
            // than a flood fill of the yard five times a second.
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

            byte output = DockPass.Run(sections,
                (section, actionsBlocked) => RunSection(section, actionsBlocked, ref sawHolder),
                out bool actionPerformed);

            SetOutput(output);

            // Back into "waiting", never into "idle": the train is still standing there, the dock
            // has merely run out of work for a moment. Dropping to a one second beat here would
            // mean a chute could top the crate up and nothing would happen until the train left.
            SetTickRate(!sawHolder ? IdleRateMs : actionPerformed ? WorkingRateMs : WaitingRateMs);
        }

        /// <summary>
        /// One section: find the other party, show it to the paper as one inventory and run the
        /// pass. Null when there is nothing there to run against — a train that has not arrived
        /// must not stop the yard section below it from working.
        /// </summary>
        private SectionPassResult RunSection(ConditionSection section, bool actionsBlocked, ref bool sawHolder)
        {
            if (section.Direction == null) return null;      // no header: reported as a paper error
            if (!section.EndsAreComplete) return null;       // half-written header: likewise

            IReadOnlyList<ICargoHold> sources = EndOf(section, section.SourceTokens, ref sawHolder);
            IReadOnlyList<ICargoHold> targets = sources == null ? null : EndOf(section, section.TargetTokens, ref sawHolder);

            if (ConditionDebug.Enabled)
            {
                ConditionDebug.Log("section '" + section.Header + "'"
                    + " source=" + Describe(section.SourceTokens, sources)
                    + " target=" + Describe(section.TargetTokens, targets));
            }

            if (sources == null || targets == null) return null;

            PaperConditionsEvaluator evaluator = EvaluatorFor(section);
            DriverResult output = RunOutputRail(evaluator, sources, targets);

            bool hasCredit = unlimited || remaining > 0;
            bool moved = !actionsBlocked && hasCredit && Move(evaluator, sources, targets);

            return new SectionPassResult(moved, output.HasOutput(), output.GetOutput());
        }

        /// <summary>How one end of a header turned out, for the trace.</summary>
        private static string Describe(IReadOnlyList<string> tokens, IReadOnlyList<ICargoHold> holds)
        {
            string what = tokens.Count == 0 ? "<the crate>" : string.Join(" ", tokens);

            return what + (holds == null ? " NOT FOUND" : " (" + holds.Count + " holds)");
        }

        /// <summary>
        /// One end of a section: this crate when the header said nothing there, otherwise whatever
        /// holder it named. Null when the named holder is not there or is not ready — a train that
        /// has not arrived must not stop the section below it from working.
        /// </summary>
        private IReadOnlyList<ICargoHold> EndOf(ConditionSection section, IReadOnlyList<string> tokens, ref bool sawHolder)
        {
            if (tokens.Count == 0) return ownHolds;

            ICargoHolder holder = FindHolder(section, tokens);
            if (holder == null) return null;

            sawHolder = true;

            if (!holder.IsReady || holder.Holds.Count == 0) return null;

            return holder.Holds;
        }

        /// <summary>
        /// The crate itself, as a hold like any other.
        ///
        /// It carries its own position, so a transfer to or from it is built by the same factory
        /// every other device uses and behaves exactly as a chute pointed at a chest would.
        /// </summary>
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
            public bool IsEmpty => dock.inventory.Empty;

            public void MarkDirty()
            {
                dock.MarkDirty();
            }
        }

        private IReadOnlyList<ICargoHold> ownHolds;

        /// <summary>
        /// The other party of one section, kept between ticks.
        ///
        /// Searching is much the most expensive thing the dock does — a square of the world two
        /// levels deep, and a flood fill for every yard in it — and the answer barely ever changes:
        /// paving does not walk away. So it is found once and kept, and only the reading of what
        /// stands on it is dropped each tick (<see cref="ICargoHolder.Refresh"/>).
        ///
        /// It is looked for again when the paper changes, when a neighbour changes, and otherwise
        /// every few seconds — the slow path that catches a yard being paved further out, where
        /// nothing next to the dock ever moved.
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

            // Something that can drive away is looked for again every tick, inventories and all:
            // a remembered wagon is one the goods might be written into after it has left.
            // Anything that stays put is remembered - and a search that found NOTHING is worth
            // remembering too, since that is the case that would otherwise repeat every tick.
            if (cacheable) holders[key] = new HolderSearch(holder, now);
            else holders.Remove(key);

            return holder;
        }

        /// <summary>How long a found holder is trusted before the world is asked again.</summary>
        private const long HolderCacheMs = 3000;

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

        /// <summary>
        /// Something was built or broken next door. It might be the paving, so the next tick looks
        /// again rather than working with what was there before.
        /// </summary>
        public void OnNeighbourBlockChange(BlockPos neibpos)
        {
            holders.Clear();
        }

        /// <summary>
        /// The output rail, answered against the holder <b>as a whole</b>: every hold shown as one
        /// inventory, so <c>in target game:firewood 500-</c> is a question about the whole yard
        /// rather than about whichever corner of it is being filled at the moment.
        ///
        /// Reading is where the composed view belongs. Putting goods in is not - see
        /// <see cref="Move"/>.
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
        /// Moves one action's worth of goods, working through the holds <b>in order</b> and taking
        /// the first one that can do something.
        ///
        /// Each hold is moved to or from through the ordinary transfer machinery, chosen by what is
        /// actually at the two ends. That is what makes a storage yard work at all: a column of
        /// piles is not slots that can be written to - it has to be built and taken apart block by
        /// block - and the world transfers already know how, including growing a column to
        /// <c>height N</c> and taking from the top of it.
        ///
        /// Nearest hold first is the whole point of the order: the near column fills to its height
        /// before the next is started.
        ///
        /// The default amount is <b>everything that matches</b>, not one piece: an <c>unload</c>
        /// without an <c>amount</c> is meant to empty the wagon, and a piece per action would take
        /// all day. <c>amount N</c> is unchanged and still means N or nothing.
        ///
        /// One action costs <b>one credit</b>, whatever it carried. Charging per piece would make
        /// the Input pin unusable on a device whose one action is "all of it".
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

        /// <summary>
        /// The transfer between two holds, chosen by what each of them actually is.
        ///
        /// A hold that names a place in the world goes through the same factory every other device
        /// uses, so it inherits every kind of end that already works — ground columns, layered
        /// piles, portable containers. A hold that is an inventory is written to directly.
        /// </summary>
        private IItemTransfer TransferFor(ICargoHold source, ICargoHold target, PaperConditionsEvaluator evaluator)
        {
            if (source.Pos != null && target.Pos != null)
            {
                IItemTransfer transfer = ItemTransferFactory.CreateTransfer(Api, source.Pos, target.Pos, 0, 0, evaluator);
                Imply(transfer, target);

                return transfer;
            }

            if (source.Pos != null)
            {
                if (target.Inventory == null) return null;

                return new WorldToInventoryTransfer(Api, source.Pos, target.Inventory, 0, evaluator);
            }

            if (source.Inventory == null) return null;

            if (target.Pos != null)
            {
                InventoryToWorldTransfer transfer = new InventoryToWorldTransfer(Api, source.Inventory, 0, target.Pos, 0, evaluator);
                Imply(transfer, target);

                return transfer;
            }

            if (target.Inventory == null) return null;

            return new InventoryToInventoryTransfer(Api, source.Inventory, target.Inventory, null, 0, 0, evaluator);
        }

        /// <summary>
        /// Under a header the ground is already implied, so <c>target ground</c> has nothing left to
        /// say and the player should not have to write it. Only where the goods really do lie on the
        /// ground — putting things into a crate is not that.
        /// </summary>
        private static void Imply(IItemTransfer transfer, ICargoHold target)
        {
            if (transfer is not InventoryToWorldTransfer world || target is DockHold) return;

            world.GroundImplied = true;

            // And it is a yard column, which holds it to two more rules: one kind of goods per
            // column, and nothing that cannot be stacked into a pile. See InventoryToWorldTransfer.
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

        /// <summary>
        /// Opens the crate. One undivided inventory, six rows of eight — the paper picks by what is
        /// in a slot, never by where the slot is, so there is nothing to divide.
        /// </summary>
        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            // The server has to be told the inventory is in use, or nothing that happens in the
            // dialog reaches it.
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

            // The edge detector of the Input pin is saved with the credit: without it the pin looks
            // like it went 0 -> N after every load, which credits a batch nobody asked for.
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
            BlockSelection selection = forPlayer?.CurrentBlockSelection;

            if (selection?.SelectionBoxIndex < SignalInputsCount)
            {
                base.GetBlockInfo(forPlayer, dsc);
                return;
            }

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
