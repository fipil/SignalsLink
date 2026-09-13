using System.Reflection;
using SignalsLink.src;
using SignalsLink.src.signals.chunkanchor;
using SignalsLink.src.signals.behaviours;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using signals.src;
using signals.src.signalNetwork;
using signals.src.hangingwires;
using signals.src.transmission;

namespace SignalsLink.Tests;

public class AnchorLifecycleTests
{
    [Fact]
    public void Loaded_sleeping_anchor_stays_asleep_and_wakes_with_a_new_window()
    {
        var f = new Fixture();
        f.Manager.Sleep(f.Pos, new[] { 0L }, 24, false);
        var be = f.Create(1000);
        Assert.False(be.Alive);
        Assert.Empty(f.Manager.ColumnsOf(f.Pos));
        f.Now = 24;
        f.Manager.WakeNow(f.Pos);
        Call(f.Manager, "ProcessWork", .1f);
        Assert.True(be.Alive);
        Assert.False(f.Manager.IsAsleep(f.Pos));
        be.SetWakeCycle(1, .25, false);
        f.Tick(be);
        f.Now += .1;
        f.Tick(be);
        Assert.True(be.Alive);
        f.Now += .2;
        f.Tick(be);
        Assert.True(f.Manager.IsAsleep(f.Pos));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Off_or_removed_sleepers_cancel_even_an_already_queued_wake(bool remove)
    {
        var f = new Fixture(); var be = f.Create(1000);
        f.Manager.Sleep(f.Pos, be.Columns, 10, false);
        f.Manager.WakeNow(f.Pos);
        if (remove) be.OnBlockRemoved(); else be.SetSwitchedOn(false);
        Call(f.Manager, "ProcessWork", .1f);
        Assert.False(f.Manager.IsAsleep(f.Pos));
        Assert.Empty(f.Manager.ColumnsOf(f.Pos));
    }

    [Fact]
    public void Overlapping_anchors_release_only_the_last_claim()
    {
        var f = new Fixture();
        var other = new BlockPos(1, 1, 1);
        f.Manager.SetColumns(f.Pos, new[] { 0L });
        f.Manager.SetColumns(other, new[] { 0L });
        f.Jobs.Tick(0);
        f.Manager.Release(f.Pos);
        Assert.Empty(f.Released);
        f.Manager.Release(other);
        Assert.Equal(new[] { 0L }, f.Released);
    }

    [Fact]
    public void Automatic_gear_insertion_revives_with_a_player_nearby()
    {
        var f = new Fixture(); var be = f.Create(0);
        Assert.False(be.Alive);
        be.Inventory[0].Itemstack = TestStacks.Item("game:gear-temporal");
        be.Inventory[0].MarkDirty();
        Assert.True(be.Alive);
        Assert.True(be.Inventory[0].Empty);
        Assert.True(be.GetCurrentCharge() > 0);
    }

    [Fact]
    public void Unloading_detaches_the_signals_listener()
    {
        var f = new Fixture(); var be = f.Create(1000);
        var listeners = (List<Action>)Field(f.Signals, "ToDoOnSignalTick");
        Assert.Single(listeners);
        be.OnBlockUnloaded();
        Assert.Empty(listeners);
    }

    [Fact]
    public void Server_settings_and_sleep_deadline_roundtrip_to_the_client()
    {
        var f = new Fixture(); var be = f.Create(1000);
        f.Manager.Sleep(f.Pos, be.Columns, 42, true);
        var tree = new TreeAttribute(); be.ToTreeAttributes(tree);
        tree.SetFloat("anchorReference", 123);
        tree.SetFloat("anchorExponent", 1.8f);
        tree.SetInt("anchorMax", 17);
        var client = new Probe { Block = be.Block };
        var world = Proxy.Make<IWorldAccessor>((m, a) => m.Name == "get_Side" ? EnumAppSide.Client : Proxy.Unhandled);
        client.FromTreeAttributes(tree, world);
        client.Api = Proxy.Make<Vintagestory.API.Client.ICoreClientAPI>((m, a) => m.Name == "get_Side" ? EnumAppSide.Client : Proxy.Unhandled);
        Assert.Equal(123, client.Settings.AnchorReferenceLoad);
        Assert.Equal(17, client.Settings.AnchorMaxColumns);
        Assert.Equal(42d, Field(client, "syncedWakeAt"));
    }

    [Fact]
    public void Sleeping_selection_and_schedule_survive_save_restore()
    {
        var f = new Fixture(); f.Manager.Sleep(f.Pos, new[] { 0L, 1L }, 42, true);
        Call(f.Manager, "Store");
        var restored = new Fixture { Saved = f.Saved };
        Call(restored.Manager, "Restore");
        Assert.True(restored.Manager.IsAsleep(f.Pos));
        Assert.Equal(42, restored.Manager.WakesAt(f.Pos));
        Assert.Empty(restored.Manager.ColumnsOf(f.Pos));
        restored.Manager.WakeNow(f.Pos);
        Call(restored.Manager, "ProcessWork", .1f);
        Assert.Equal(2, restored.Manager.ColumnsOf(f.Pos).Count);
    }

    [Fact]
    public void Never_sleep_selection_wakes_a_current_sleeper()
    {
        var f = new Fixture(); var be = f.Create(1000);
        f.Manager.Sleep(f.Pos, be.Columns, 42, true);
        be.SetWakeCycle(0, .5, false);
        Assert.False(f.Manager.IsAsleep(f.Pos));
        Assert.True(be.Alive);
    }

    [Fact]
    public void Uncharged_anchor_cannot_be_started_by_the_scheduler()
    {
        var f = new Fixture(); var be = f.Create(0);
        f.Manager.Sleep(f.Pos, be.Columns, 1, true);
        f.Manager.WakeNow(f.Pos);
        Call(f.Manager, "ProcessWork", .1f);
        Assert.False(be.Alive);
        Assert.Empty(f.Manager.ColumnsOf(f.Pos));
    }

    [Theory]
    [InlineData(double.NaN, .5)]
    [InlineData(double.PositiveInfinity, .5)]
    [InlineData(2, .5)]
    [InlineData(1, -.5)]
    public void Invalid_wake_settings_are_rejected(double interval, double window)
    {
        var f = new Fixture(); var be = f.Create(1000);
        be.SetWakeCycle(interval, window, true);
        Assert.Equal(0, be.WakeIntervalHours);
        Assert.Equal(.5, be.WakeWindowHours);
    }

    [Fact]
    public void Quarter_hour_cycles_spend_charge_before_sleep_and_off_time_is_free()
    {
        var f = new Fixture(); f.Player.Entity.Pos.SetPos(10000, 100, 10000);
        var be = f.Create(100000);
        be.SetWakeCycle(4, .25, false);
        f.Tick(be); // establish census and finish warmup
        double before = be.GetCurrentCharge();
        f.Now = .25; f.Tick(be);
        Assert.True(f.Manager.IsAsleep(f.Pos));
        Assert.True(be.GetCurrentCharge() < before);
        double after = be.GetCurrentCharge();
        f.Now = 2; f.Tick(be);
        Assert.Equal(after, be.GetCurrentCharge());
        be.SetSwitchedOn(false);
        f.Now = 10; f.Tick(be);
        Assert.Equal(after, be.GetCurrentCharge());
        be.SetSwitchedOn(true); f.Tick(be);
        Assert.Equal(after, be.GetCurrentCharge());
    }

    [Fact]
    public void Signal_keeps_the_anchor_awake_past_the_window()
    {
        var f = new Fixture(); var be = f.Create(1000);
        be.SetWakeCycle(1, .25, false); f.Tick(be);
        be.OnValueChanged(new NodePos(f.Pos, 0), 15);
        f.Now = 2; f.Tick(be);
        Assert.False(f.Manager.IsAsleep(f.Pos));
        be.OnValueChanged(new NodePos(f.Pos, 0), 0); f.Tick(be);
        Assert.True(f.Manager.IsAsleep(f.Pos));
    }

    [Fact]
    public void Rescheduling_invalidates_an_old_queued_wake()
    {
        var f = new Fixture(); var be = f.Create(1000);
        f.Manager.Sleep(f.Pos, be.Columns, 1, true);
        f.Manager.WakeNow(f.Pos);
        be.SetWakeCycle(24, .5, false);
        Call(f.Manager, "ProcessWork", .1f);
        Assert.True(f.Manager.IsAsleep(f.Pos));
        Assert.Equal(24, f.Manager.WakesAt(f.Pos));
    }

    [Fact]
    public void An_unloaded_anchor_wakes_from_a_live_wire_end_without_loading_to_poll()
    {
        var f = new Fixture();
        var remotePos = new BlockPos(64, 1, 0);
        var remote = new WireDevice { Pos = remotePos };
        remote.Node = new BaseNode { Pos = new NodePos(remotePos, 0), isSource = true, output = 0 };
        f.Devices[remotePos] = remote;
        f.Wires.data.connections.Add(new WireConnection(new NodePos(f.Pos, 0), remote.Node.Pos));
        f.Manager.Sleep(f.Pos, new[] { 0L }, 24, false);
        Call(f.Manager, "ProcessWork", .1f);
        Assert.True(f.Manager.IsAsleep(f.Pos));
        Assert.Empty(f.Manager.ColumnsOf(f.Pos));
        remote.Node.output = 15;
        Call(f.Manager, "ProcessWork", .1f);
        Assert.False(f.Manager.IsAsleep(f.Pos));
        Assert.Single(f.Manager.ColumnsOf(f.Pos));
    }

    [Fact]
    public void Removing_a_wake_wire_prevents_its_old_source_from_waking_the_anchor()
    {
        var f = new Fixture();
        var remotePos = new BlockPos(64, 1, 0);
        var remote = new WireDevice { Pos = remotePos, Node = new BaseNode { Pos = new NodePos(remotePos, 0), isSource = true, output = 15 } };
        f.Devices[remotePos] = remote;
        f.Wires.data.connections.Add(new WireConnection(new NodePos(f.Pos, 0), remote.Node.Pos));
        f.Manager.Sleep(f.Pos, new[] { 0L }, 24, false);
        f.Wires.data.connections.Clear();
        Call(f.Manager, "ProcessWork", .1f);
        Assert.True(f.Manager.IsAsleep(f.Pos));
    }

    [Fact]
    public void Replacing_a_wire_in_one_tick_does_not_leave_a_stale_wake_source()
    {
        var f = new Fixture();
        var oldPos = new BlockPos(64, 1, 0);
        var newPos = new BlockPos(96, 1, 0);
        var oldDevice = new WireDevice { Pos = oldPos, Node = new BaseNode { Pos = new NodePos(oldPos, 0), isSource = true, output = 15 } };
        var newDevice = new WireDevice { Pos = newPos, Node = new BaseNode { Pos = new NodePos(newPos, 0), isSource = true, output = 0 } };
        f.Devices[oldPos] = oldDevice; f.Devices[newPos] = newDevice;
        var oldWire = new WireConnection(new NodePos(f.Pos, 0), oldDevice.Node.Pos);
        var newWire = new WireConnection(new NodePos(f.Pos, 0), newDevice.Node.Pos);
        f.Wires.data.connections.Add(oldWire);
        f.Manager.Sleep(f.Pos, new[] { 0L }, 24, false);
        f.Wires.data.connections.Remove(oldWire); Call(f.Manager, "ChangeWire", oldWire, false);
        f.Wires.data.connections.Add(newWire); Call(f.Manager, "ChangeWire", newWire, true);
        Call(f.Manager, "ProcessWork", .1f);
        Assert.True(f.Manager.IsAsleep(f.Pos));
        newDevice.Node.output = 15;
        Call(f.Manager, "ProcessWork", .1f);
        Assert.False(f.Manager.IsAsleep(f.Pos));
    }

    [Fact]
    public void Float_charge_from_older_saves_is_preserved()
    {
        var f = new Fixture(); var be = f.Create(0);
        var tree = new TreeAttribute(); be.ToTreeAttributes(tree);
        tree.SetFloat("charge", 1234.5f);
        be.FromTreeAttributes(tree, f.Api.World);
        Assert.Equal(1234.5f, be.GetCurrentCharge());
    }

    [Fact]
    public void Old_claim_for_a_missing_anchor_is_released_after_loading_its_column()
    {
        var f = new Fixture();
        f.Manager.SetColumns(f.Pos, new[] { 0L, 1L });
        f.Jobs.Tick(0);
        Call(f.Manager, "ProcessWork", .1f);
        Assert.Empty(f.Manager.ColumnsOf(f.Pos));
        Assert.Equal(2, f.Released.Count);
    }

    public class WireDevice : BlockEntity, ISignalNodeProvider
    {
        public BaseNode Node;
        public ISignalNode GetNodeAt(NodePos pos) => Node.Pos == pos ? Node : null;
        public Dictionary<NodePos, ISignalNode> GetNodes() => new() { [Node.Pos] = Node };
        public Vec3f GetNodePosinBlock(NodePos pos) => new();
        public void OnNodeUpdate(NodePos pos) { }
    }

    public static object Field(object target, string name) => Find(target.GetType(), name).GetValue(target);
    private static void Set(object target, string name, object value) => Find(target.GetType(), name).SetValue(target, value);
    private static FieldInfo Find(Type type, string name)
    {
        while (type != null)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null) return field;
            type = type.BaseType;
        }
        throw new MissingFieldException(name);
    }
    public static object Call(object target, string name, params object[] args)
    {
        var type = target.GetType();
        while (type != null)
        {
            var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (method != null) return method.Invoke(target, args);
            type = type.BaseType;
        }
        throw new MissingMethodException(name);
    }
    public class Probe : BEChunkAnchor
    {
        public override void MarkDirty(bool redrawOnClient = false, IPlayer skipPlayer = null) { }
    }
    public class Fixture
    {
        public double Now;
        public byte[] Saved;
        public BlockPos Pos = new(0, 1, 0);
        public Probe Entity;
        public ChunkAnchors Manager = new();
        public SignalNetworkMod Signals = new();
        public HangingWiresMod Wires = new();
        public Dictionary<BlockPos, BlockEntity> Devices = new();
        public List<long> Released = new();
        public AnchorColumnJobs Jobs;
        public ICoreServerAPI Api;
        public IPlayer Player;
        
        public Fixture()
        {
            var player = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Vintagestory.Server.ServerPlayer));
            var dataType = Find(player.GetType(), "worlddata").FieldType;
            var data = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(dataType);
            dataType.GetProperty("EntityPlayer").SetValue(data, new Vintagestory.API.Common.EntityPlayer());
            Set(player, "worlddata", data);
            Player = (IPlayer)player;
            object Handle(MethodInfo m, object[] a)
            {
                switch (m.Name)
                {
                    case "get_TotalHours": return Now;
                    case "get_ElapsedMilliseconds": return (long)(Now * 1000);
                    case "get_Side": return EnumAppSide.Server;
                    case "get_ChunkSize": return 32;
                    case "get_MapSizeY": return 256;
                    case "get_AllOnlinePlayers": return new IPlayer[] { Player };
                    case "GetBlock": return null;
                    case "GetBlockEntity": return ((BlockPos)a[0]).Equals(Pos) ? Entity : Devices.GetValueOrDefault((BlockPos)a[0]);
                    case "GetModSystem": return m.GetGenericArguments()[0] == typeof(ChunkAnchors) ? Manager : m.GetGenericArguments()[0] == typeof(SignalNetworkMod) ? Signals : null;
                    case "StoreData": Saved = (byte[])a[1]; return null;
                    case "GetData": return Saved;
                    case "ServerRunPhase": ((Action)a[1])(); return null;
                }
                return Proxy.Unhandled;
            }
            Api = Proxy.Make<ICoreServerAPI>(Handle);
            Jobs = new AnchorColumnJobs(_ => true, _ => {}, k => Released.Add(k), _ => Counts().GetEnumerator());
            Signals.Api = Api;
            Set(Manager, "sapi", Api); Set(Manager, "jobs", Jobs);
            Set(Manager, "signals", Signals); Set(Manager, "wires", Wires);
        }
        private static IEnumerable<AnchorCount> Counts() { yield return new AnchorCount(251, 0); }
        public Probe Create(double charge)
        {
            var block = new Block { Code = new AssetLocation("signalslink:chunkanchor-off-north") };
            block.BlockBehaviors = new BlockBehavior[] { new BlockBehaviorTemporalCharge(block) };
            block.CollectibleBehaviors = block.BlockBehaviors;
            Entity = new Probe { Pos = Pos.Copy(), Block = block };
            Set(Entity, "charge", charge);
            Entity.Initialize(Api);
            return Entity;
        }
        public void Tick(Probe be) { Jobs.Tick(Now * 1000); Call(be, "OnTick", 5f); }
    }
    public class Proxy : DispatchProxy
    {
        public static readonly object Unhandled = new();
        public System.Func<MethodInfo, object[], object> Handler;
        private readonly Dictionary<Type, object> children = new();
        public static T Make<T>(System.Func<MethodInfo, object[], object> handler) where T : class
        {
            var value = Create<T, Proxy>(); ((Proxy)(object)value).Handler = handler; return value;
        }
        protected override object Invoke(MethodInfo method, object[] args)
        {
            object result = Handler(method, args);
            if (!ReferenceEquals(result, Unhandled)) return result;
            Type type = method.ReturnType;
            if (type == typeof(void)) return null;
            if (type.IsInterface)
            {
                if (!children.TryGetValue(type, out var value))
                { value = Create(type, typeof(Proxy)); ((Proxy)value).Handler = Handler; children[type] = value; }
                return value;
            }
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }
}


