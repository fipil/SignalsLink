using signals.src;
using signals.src.hangingwires;
using signals.src.signalNetwork;
using SignalsLink.src.signals.link;
using SignalsLink.src.signals.managedchute;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SignalsLink.Testing;

public sealed class ChuteRig
{
    public const string Name = "chute-basic";
    private const string Paper = "game:firewood\namount 8\nkeep 8\n\ngame:firewood 8\noutput 9";
    private readonly ICoreServerAPI api;
    private readonly Func<string> checkCaller;
    private readonly Caller caller;
    private readonly HangingWiresMod wires;
    private readonly SignalNetworkMod signals;
    private readonly Action<string, string> record;
    private readonly WireConnection[] connections;
    private readonly Dictionary<string, Block> blocks = new();
    private Item firewood;
    private bool switchOn;
    public ArenaLayout Layout { get; }

    public ChuteRig(ICoreServerAPI api, IPlayer player, Caller caller, ArenaLayout layout, Action<string, string> record)
    {
        this.api = api; this.caller = caller; Layout = layout; this.record = record;
        checkCaller = () => CallerProblem(api, player, layout);
        wires = api.ModLoader.GetModSystem<HangingWiresMod>();
        signals = api.ModLoader.GetModSystem<SignalNetworkMod>();
        connections = new[] {
            new WireConnection(new NodePos(layout.Power, 0), new NodePos(layout.Switch, 0)),
            new WireConnection(new NodePos(layout.Switch, 1), new NodePos(layout.Chute, 0)),
            new WireConnection(new NodePos(layout.Chute, 3), new NodePos(layout.Output, 0))
        };
    }

    // Keep the game-player boundary replaceable for tests of world ownership and cleanup.
    internal ChuteRig(ICoreServerAPI api, ArenaLayout layout, Action<string, string> record, Func<string> checkCaller)
        : this(api, null, new Caller(), layout, record) { this.checkCaller = checkCaller; }

    public void ResolveAssets()
    {
        if (wires == null || signals?.netManager == null) throw new InvalidOperationException("Signals network is not ready.");
        foreach (string code in Layout.Cells().Select(Layout.ExpectedCode).Where(c => c != null).Distinct())
        {
            var block = api.World.GetBlock(new AssetLocation(code));
            if (block == null || block.Id == 0) throw new InvalidOperationException("Missing block asset: " + code);
            blocks[code] = block;
        }
        firewood = api.World.GetItem(new AssetLocation("game:firewood"))
            ?? throw new InvalidOperationException("Missing item asset: game:firewood");
    }

    public string CheckEnvironment()
    {
        string callerProblem = checkCaller();
        if (callerProblem != null) return callerProblem;
        foreach (BlockPos pos in Layout.Cells())
            if (api.World.BlockAccessor.GetChunkAtBlockPos(pos) == null) return "unloaded chunk at " + pos;
        return null;
    }

    private static string CallerProblem(ICoreServerAPI api, IPlayer player, ArenaLayout layout)
    {
        if (player?.Entity == null || !api.World.AllOnlinePlayers.Any(p => p.PlayerUID == player.PlayerUID)) return "caller disconnected";
        if (!player.HasPrivilege(Privilege.controlserver) || player.WorldData.CurrentGameMode != EnumGameMode.Creative)
            return "caller must remain creative and have controlserver privilege";
        if (player.Entity.Pos.AsBlockPos.dimension != layout.Location.Dimension
            || player.Entity.Pos.SquareDistanceTo(layout.At(4, 1, 2).ToVec3d()) > 48 * 48) return "caller left the arena (48 blocks)";
        return null;
    }

    public string Preflight(bool previouslyOwned)
    {
        string environment = CheckEnvironment();
        if (environment != null) return environment;
        foreach (BlockPos pos in Layout.Cells())
        {
            Block solid = api.World.BlockAccessor.GetBlock(pos, BlockLayersAccess.Solid);
            Block fluid = api.World.BlockAccessor.GetBlock(pos, BlockLayersAccess.Fluid);
            if (fluid.Id != 0) return "unexpected fluid " + fluid.Code + " at " + pos;
            if (solid.Id != 0 && (!previouslyOwned || !Layout.IsOwnedBlock(pos, solid.Code.ToString())))
                return "unexpected block " + solid.Code + " at " + pos;
        }
        foreach (var wire in wires.data.connections)
            if ((Layout.Contains(wire.pos1.blockPos) || Layout.Contains(wire.pos2.blockPos))
                && (!previouslyOwned || !connections.Any(owned => SameWire(owned, wire)))) return "foreign wire at " + wire.pos1 + " -> " + wire.pos2;
        var links = api.ModLoader.GetModSystem<LinkNetworkMod>();
        if (links?.data.connections.Any(c => Layout.Contains(c.pos1.blockPos) || Layout.Contains(c.pos2.blockPos)) == true)
            return "foreign hose/sleeve attached inside the arena";
        foreach (Entity entity in Entities())
            if (entity is not EntityItem item || !previouslyOwned || item.Itemstack?.Collectible.Code.ToString() != "game:firewood")
                return "unexpected entity " + entity.Code + " at " + entity.Pos;
        return null;
    }

    private IEnumerable<Entity> Entities() => api.World.GetEntitiesAround(Layout.At(4, 2, 2).ToVec3d(), 8, 5,
        e => e is not EntityPlayer && Layout.Contains(e.Pos.AsBlockPos));

    private static bool SameWire(WireConnection a, WireConnection b) =>
        (a.pos1 == b.pos1 && a.pos2 == b.pos2) || (a.pos1 == b.pos2 && a.pos2 == b.pos1);

    public void Build()
    {
        // Containers and supports precede the chute. SetBlock invokes the real BE lifecycle.
        foreach (BlockPos pos in Layout.Cells().Where(p => p.Y == Layout.Location.Y)) Place(pos);
        foreach (BlockPos pos in new[] { Layout.Target, Layout.Source, Layout.Power, Layout.Switch, Layout.Output, Layout.Chute }) Place(pos);
        switchOn = false;
    }

    private void Place(BlockPos pos)
    {
        Block block = blocks[Layout.ExpectedCode(pos)];
        var stack = new ItemStack(block);
        if (pos.Equals(Layout.Source) || pos.Equals(Layout.Target)) stack.Attributes.SetString("type", "normal-generic");
        api.World.BlockAccessor.SetBlock(block.Id, pos, stack);
        api.World.BlockAccessor.MarkBlockDirty(pos);
    }

    private IInventory Inventory(BlockPos pos) => (api.World.BlockAccessor.GetBlockEntity(pos) as IBlockEntityContainer)?.Inventory;
    private BEManagedChute Chute => api.World.BlockAccessor.GetBlockEntity(Layout.Chute) as BEManagedChute;
    private ISignalNode Node(BlockPos pos, int pin) => signals.GetDeviceAt(pos)?.GetNodeAt(new NodePos(pos, pin));
    private int Value(BlockPos pos, int pin) => Node(pos, pin)?.value ?? -1;
    private bool NetworkReady(BlockPos pos, int pin)
    {
        var node = Node(pos, pin);
        return node?.netId is long id && signals.netManager.networks.ContainsKey(id);
    }
    private int Count(BlockPos pos)
    {
        var inv = Inventory(pos);
        if (inv == null) return -1;
        if (inv.Any(s => !s.Empty && s.Itemstack.Collectible.Code.ToString() != "game:firewood")) return -1;
        return inv.Sum(s => s.StackSize);
    }
    public string Snapshot() => "source=" + Count(Layout.Source) + ";target=" + Count(Layout.Target)
        + ";input=" + Value(Layout.Chute, 0) + ";output=" + Value(Layout.Output, 0)
        + ";chute=" + api.World.BlockAccessor.GetBlock(Layout.Chute).Code;

    private bool Ready() => Chute?.Api != null && Inventory(Layout.Source)?.Count > 1 && Inventory(Layout.Target)?.Count > 0
        && connections.All(c => Node(c.pos1.blockPos, c.pos1.index) != null && Node(c.pos2.blockPos, c.pos2.index) != null);

    private void Connect()
    {
        foreach (WireConnection wire in connections)
            if (!wires.TryToAddConnection(wire)) throw new InvalidOperationException("Could not add wire " + wire.pos1 + " -> " + wire.pos2);
        Chute.ConditionsText = Paper;
        Seed(Layout.Source, 4, 12); // the atomic batch must span real slots and registered items
        Seed(Layout.Target);
        record("PAPER", Paper);
    }

    private void Seed(BlockPos pos, params int[] counts)
    {
        var inv = Inventory(pos) ?? throw new InvalidOperationException("Missing inventory at " + pos);
        foreach (ItemSlot slot in inv) { slot.Itemstack = null; slot.MarkDirty(); }
        for (int i = 0; i < counts.Length; i++) { inv[i].Itemstack = new ItemStack(firewood, counts[i]); inv[i].MarkDirty(); }
        api.World.BlockAccessor.GetBlockEntity(pos).MarkDirty();
        record("FIXTURE", "inventory at " + pos + " set to " + counts.Sum());
    }

    private void Toggle(bool on)
    {
        if (switchOn == on) return;
        Block block = api.World.BlockAccessor.GetBlock(Layout.Switch);
        block.Activate(api.World, caller, new BlockSelection { Position = Layout.Switch, Face = BlockFacing.UP });
        switchOn = on;
        record("SWITCH", on ? "on" : "off");
    }

    private bool State(int source, int target, int input, int output) => Count(Layout.Source) == source
        && Count(Layout.Target) == target && Value(Layout.Chute, 0) == input && Value(Layout.Output, 0) == output
        && NetworkReady(Layout.Output, 0) && !Entities().Any();

    public IReadOnlyList<RigStep> Steps()
    {
        RigStep Wait(string name, Action enter, int source, int target, int input, int output) => new(name, enter,
            () => State(source, target, input, output),
            () => $"expected={source},{target},{input},{output};actual=" + Snapshot(), 8000);
        RigStep Hold(string name, int source, int target, int input, int output) => new(name, null,
            () => State(source, target, input, output),
            () => $"expected={source},{target},{input},{output};actual=" + Snapshot(), 5000, StepMode.Stable, 1000);
        return new[] {
            new RigStep("build-and-initialize", Build, Ready, Snapshot, 10000),
            new RigStep("settled-devices", null, Ready, Snapshot, 5000, StepMode.Stable, 500),
            Wait("wire-and-seed", Connect, 16, 0, 0, 0),
            Hold("no-transfer-without-credit", 16, 0, 0, 0),
            Wait("output-rises-without-credit", () => Seed(Layout.Target, 8), 16, 8, 0, 9),
            Hold("output-stays-high", 16, 8, 0, 9),
            Wait("output-returns-to-zero", () => Seed(Layout.Target), 16, 0, 0, 0),
            Hold("output-stays-zero", 16, 0, 0, 0),
            Wait("switch-drives-atomic-batch", () => Toggle(true), 8, 8, 15, 9),
            Hold("keep-prevents-another-batch", 8, 8, 15, 9),
            Wait("switch-disables-input", () => Toggle(false), 8, 8, 0, 9),
            Wait("output-falls-after-input-off", () => Seed(Layout.Target), 8, 0, 0, 0),
            Hold("no-late-transfer-after-off", 8, 0, 0, 0)
        };
    }

    /// <summary>Best effort even after unload/cancel. Never loads chunks or erases foreign blocks.</summary>
    public bool Cleanup()
    {
        bool complete = true;
        if (api.World.BlockAccessor.GetChunkAtBlockPos(Layout.Chute) != null) Chute?.ClearBuffer();
        foreach (WireConnection wire in connections)
        {
            try { wires.TryToRemoveConnection(wire.pos1, wire.pos2); }
            catch (Exception ex) { complete = false; record("CLEANUP_ERROR", ex.ToString()); }
        }
        foreach (BlockPos pos in Layout.Cells().OrderByDescending(p => p.Y))
        {
            try
            {
                if (api.World.BlockAccessor.GetChunkAtBlockPos(pos) == null) { complete = false; continue; }
                Block block = api.World.BlockAccessor.GetBlock(pos, BlockLayersAccess.Solid);
                if (api.World.BlockAccessor.GetBlock(pos, BlockLayersAccess.Fluid).Id != 0)
                { complete = false; record("CLEANUP_PRESERVED", "fluid at " + pos); }
                if (block.Id == 0) continue;
                if (!Layout.IsOwnedBlock(pos, block.Code.ToString()))
                { complete = false; record("CLEANUP_PRESERVED", block.Code + " at " + pos); continue; }
                bool foreignWire = wires.data.connections.Any(w => w.pos1.blockPos.Equals(pos) || w.pos2.blockPos.Equals(pos));
                bool foreignLink = api.ModLoader.GetModSystem<LinkNetworkMod>()?.data.connections.Any(c => c.pos1.blockPos.Equals(pos) || c.pos2.blockPos.Equals(pos)) == true;
                if (foreignWire || foreignLink)
                { complete = false; record("CLEANUP_PRESERVED", "foreign connection at " + pos); continue; }
                // Clear contents before removal; SetBlock must not scatter test items.
                if (Inventory(pos) is IInventory inv) foreach (ItemSlot slot in inv) slot.Itemstack = null;
                api.World.BlockAccessor.SetBlock(0, pos, BlockLayersAccess.Solid);
                api.World.BlockAccessor.MarkBlockDirty(pos);
            }
            catch (Exception ex) { complete = false; record("CLEANUP_ERROR", ex.ToString()); }
        }
        foreach (Entity entity in Entities())
        {
            if (entity is EntityItem item && item.Itemstack?.Collectible.Code.ToString() == "game:firewood") entity.Die(EnumDespawnReason.Removed);
            else { complete = false; record("CLEANUP_PRESERVED", "entity " + entity.Code); }
        }
        foreach (BlockPos pos in Layout.Cells())
        {
            if (api.World.BlockAccessor.GetChunkAtBlockPos(pos) == null) { complete = false; continue; }
            if (api.World.BlockAccessor.GetBlock(pos, BlockLayersAccess.Solid).Id != 0
                || api.World.BlockAccessor.GetBlock(pos, BlockLayersAccess.Fluid).Id != 0) complete = false;
        }
        if (wires.data.connections.Any(w => connections.Any(c => SameWire(c, w)))) complete = false;
        record("CLEANUP", complete ? "complete" : "pending; return with all arena chunks loaded and run /sltest clean");
        return complete;
    }
}
