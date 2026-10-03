using System.Reflection;
using Newtonsoft.Json.Linq;
using signals.src;
using signals.src.hangingwires;
using signals.src.signalNetwork;
using SignalsTubes.src.programtube;
using SignalsTubes.src.socket;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SignalsTubes.Tests.fidelity;

/// <summary>An interface answered by one lambda; everything it does not handle returns the default.</summary>
public class Fake : DispatchProxy
{
    public static readonly object Unhandled = new();
    private System.Func<MethodInfo, object[], object> handler;

    public static T Of<T>(System.Func<MethodInfo, object[], object> handler = null) where T : class
    {
        var proxy = Create<T, Fake>();
        ((Fake)(object)proxy).handler = handler;
        return proxy;
    }

    protected override object Invoke(MethodInfo method, object[] args)
    {
        object result = handler == null ? Unhandled : handler(method, args);
        if (!ReferenceEquals(result, Unhandled)) return result;
        var type = method.ReturnType;
        if (type == typeof(void)) return null;
        if (type.IsInterface && type.IsInstanceOfType(this)) return this;   // fluent registration calls
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}

/// <summary>
/// The real Signals mod on a fake game: its own mod systems, network manager, block entities and
/// behaviors run unchanged; only the game API under them is faked. Devices are placed like a player
/// would, wired through HangingWiresMod, and ticked through the tick Signals registered itself.
/// Internal Signals classes are created by name, so a rename in Signals fails these tests loudly.
/// </summary>
public sealed class SignalsRig
{
    public readonly ICoreServerAPI Api;
    public readonly SignalNetworkMod Signals = new();
    public readonly HangingWiresMod Wires = new();
    public readonly ProgramStore Store = new();
    public readonly ItemProgramTube TubeItem = new() { Code = new AssetLocation("signalstubes:programtube-fire"), MaxStackSize = 8, ItemId = 1 };

    private static readonly Assembly SignalsAssembly = typeof(SignalNetworkMod).Assembly;
    private readonly Dictionary<BlockPos, BlockEntity> entities = new();
    private readonly Dictionary<BlockPos, Block> blocks = new();
    private readonly List<Action<float>> gameTicks = new();
    private readonly List<(NodePos control, NodePos switched)> controls = new();   // valve grids, actuators
    private readonly IServerWorldAccessor world;
    public IWorldAccessor World => world;
    /// <summary>Grid recipes the fake world knows (for machines that craft).</summary>
    public readonly List<GridRecipe> Recipes = new();
    /// <summary>Items the fake world hands out by code; anything else is the tube item.</summary>
    public readonly Dictionary<string, Item> Items = new();

    public SignalsRig()
    {
        var logger = Fake.Of<ILogger>();
        var accessor = Fake.Of<IBlockAccessor>((m, a) => m.Name switch
        {
            "GetBlockEntity" => a[0] is BlockPos p && entities.TryGetValue(p, out var be) ? be : null,
            "GetBlock" when a[0] is BlockPos p => blocks.GetValueOrDefault(p),
            _ => Fake.Unhandled
        });
        world = Fake.Of<IServerWorldAccessor>((m, a) => m.Name switch
        {
            "get_BlockAccessor" => accessor,
            "get_Side" => EnumAppSide.Server,
            "get_Logger" => logger,
            "get_Api" => Api,
            "RegisterGameTickListener" => Capture((Action<float>)a[0]),
            "GetItem" => a[0] is AssetLocation loc && Items.TryGetValue(loc.ToString(), out var known) ? known : TubeItem,
            "get_GridRecipes" => Recipes,
            _ => Fake.Unhandled
        });
        var modLoader = Fake.Of<IModLoader>((m, a) =>
        {
            if (m.Name != "GetModSystem" || !m.IsGenericMethod) return Fake.Unhandled;
            var wanted = m.GetGenericArguments()[0];
            return new ModSystem[] { Signals, Wires, Store }.FirstOrDefault(wanted.IsInstanceOfType);
        });
        var network = Fake.Of<IServerNetworkAPI>((m, a) => m.Name == "RegisterChannel" ? Fake.Of<IServerNetworkChannel>() : Fake.Unhandled);
        var events = Fake.Of<IServerEventAPI>();
        var classes = Fake.Of<IClassRegistryAPI>();   // containers ask it for an inventory network util; null is fine without a GUI
        Api = Fake.Of<ICoreServerAPI>((m, a) => m.Name switch
        {
            "get_ClassRegistry" => classes,
            "get_Side" => EnumAppSide.Server,
            "get_World" => world,
            "get_ModLoader" => modLoader,
            "get_Logger" => logger,
            "get_Network" => network,
            "get_Event" => events,
            _ => Fake.Unhandled
        });
        Wires.Start(Api);
        Signals.Start(Api);
        Signals.StartServerSide(Api);
    }

    private long Capture(Action<float> tick) { gameTicks.Add(tick); return gameTicks.Count; }

    /// <summary>One Signals step: tick listeners, then every invalid network is simulated.</summary>
    public void Tick() { foreach (var tick in gameTicks) tick(0.11f); }

    // ---- placing devices

    /// <summary>Behavior properties: n source nodes at output 0, like a socket's pins.</summary>
    public static string SourceNodes(int n) => Nodes(Enumerable.Range(0, n).Select(i => Source(i, 0)).ToArray()) + "}";

    private static string Nodes(params string[] nodes) => "{\"signalNodes\":[" + string.Join(",", nodes) + "]";
    private static string Plain(int index) => "{\"index\":" + index + ",\"isSource\":false}";
    private static string Source(int index, int output) => "{\"index\":" + index + ",\"isSource\":true,\"output\":" + output + "}";

    private static object Internal(string typeName) =>
        Activator.CreateInstance(SignalsAssembly.GetType("signals.src.signalNetwork." + typeName, true), true);

    /// <summary>Any block entity with one Signals behavior, for devices of other mods.</summary>
    public BlockPos Place(BlockPos pos, Block block, string code, BlockEntity be, System.Func<BlockEntity, BlockEntityBehavior> behavior, string properties, params (string, string)[] variants)
    {
        if (entities.ContainsKey(pos)) throw new InvalidOperationException("Position taken: " + pos);
        block.Code = new AssetLocation(code);
        block.BlockId = blocks.Count + 1;   // id 0 is air
        foreach (var (k, v) in variants) block.VariantStrict[k] = v;
        be.Pos = pos;
        be.Block = block;
        var beh = behavior(be);
        beh.properties = new JsonObject(JToken.Parse(properties));
        be.Behaviors.Add(beh);
        blocks[pos] = block;
        entities[pos] = be;
        be.Initialize(Api);
        return pos;
    }

    private sealed class PlainEntity : BlockEntity { }

    public static BlockPos At(int x, int z = 0) => new(x, 0, z);

    public BlockPos Socket(BlockPos pos, ItemStack tube = null)
    {
        var block = new Block();
        var be = new BETubeSocket { Block = block };   // loading a saved entity needs its block
        if (tube != null)
        {
            var tree = new TreeAttribute();
            tree.SetInt("posx", pos.X); tree.SetInt("posy", pos.Y); tree.SetInt("posz", pos.Z);
            tree.SetItemstack("programTube", tube);
            be.FromTreeAttributes(tree, world);
        }
        return Place(pos, block, "signalstubes:tubesocket-north-down", be, e => new BEBehaviorSignalConnector(e), SourceNodes(8));
    }

    public BlockPos SourceBlock(BlockPos pos, int output = 15) =>
        Place(pos, new Block(), "signals:blocksource", new PlainEntity(), e => new BEBehaviorSignalConnector(e), Nodes(Source(0, output)) + "}");

    public BlockPos Connection(BlockPos pos) =>
        Place(pos, new Block(), "signals:connection-down", new PlainEntity(), e => new BEBehaviorSignalConnector(e), Nodes(Plain(0)) + "}");

    public BlockPos PassThrough(BlockPos pos) =>
        Place(pos, new Block(), "signals:pass_through_connector-down", new PlainEntity(), e => new BEBehaviorSignalConnector(e),
            Nodes(Plain(0), Plain(1)) + ",\"connections\":[{\"i1\":0,\"i2\":1,\"att\":0}]}");

    public BlockPos Resistor(BlockPos pos, int value) =>
        Place(pos, new Block(), $"signals:blockresistor-{value}-north-down", new PlainEntity(), e => new BEBehaviorSignalConnector(e),
            Nodes(Plain(0), Plain(1)) + ",\"connections\":[{\"i1\":0,\"i2\":1,\"att\":" + value + "}]}", ("value", value.ToString()));

    public BlockPos Valve(BlockPos pos)
    {
        controls.Add((new NodePos(pos, 0), new NodePos(pos, 1)));
        return Place(pos, new Block(), "signals:blockvalve-north-down", (BlockEntity)Internal("BEValve"), e => new BEBehaviorSignalValve(e), Nodes(Plain(0), Plain(1), Plain(2)) + "}");
    }

    public BlockPos Tetrode(BlockPos pos)
    {
        controls.Add((new NodePos(pos, 0), new NodePos(pos, 1)));
        controls.Add((new NodePos(pos, 3), new NodePos(pos, 1)));
        return Place(pos, new Block(), "signals:blocktetrode-north-down", new PlainEntity(), e => new BEBehaviorSignalTetrode(e), Nodes(Plain(0), Plain(1), Plain(2), Plain(3)) + "}");
    }

    public BlockPos Delay(BlockPos pos, int value) =>
        Place(pos, new Block(), $"signals:blockdelay-north-down-{value}", (BlockEntity)Internal("BEDelay"), e => new BEBehaviorSignalConnector(e),
            Nodes(Plain(0), Source(1, 0)) + "}", ("value", value.ToString()));

    public BlockPos KnifeSwitch(BlockPos pos, bool on) =>
        Place(pos, (Block)Internal("BlockSwitch"), "signals:knifeswitch-north-down-" + (on ? "on" : "off"), (BlockEntity)Internal("BESwitch"),
            e => new BEBehaviorSignalSwitch(e), Nodes(Plain(0), Plain(1)) + "}", ("state", on ? "on" : "off"));

    /// <summary>The actuator acts on the block next to it in the given direction.</summary>
    public BlockPos Actuator(BlockPos pos, BlockFacing towards)
    {
        controls.Add((new NodePos(pos, 0), new NodePos(pos.AddCopy(towards), 0)));
        return Place(pos, new Block(), $"signals:blockactuator-off-{towards.Code}-down", (BlockEntity)Internal("BEActuator"), e => new BEBehaviorSignalConnector(e),
            Nodes(Plain(0)) + "}", ("powered", "off"), ("orientation", towards.Code));
    }

    public void Wire(BlockPos a, int ia, BlockPos b, int ib)
    {
        if (!Wires.TryToAddConnection(new WireConnection(new NodePos(a, ia), new NodePos(b, ib)))) throw new InvalidOperationException("Wire refused.");
    }

    // ---- network order

    /// <summary>
    /// Signals simulates its networks one after another and a valve reacts the moment its grid's network
    /// is done. So a valve whose grid network runs before the network it switches reacts within the same
    /// step, otherwise in the next one; which it is depends on the order the networks were created in.
    /// A tube always reacts in the next step. This puts the networks into the order where the real
    /// circuit does the same, and returns how many controls still react at once (control loops).
    /// </summary>
    public int ReactNextStepOrder()
    {
        var nets = Signals.netManager.networks;
        long? Net(NodePos node) => entities.TryGetValue(node.blockPos, out var be) ? be.GetBehavior<BEBehaviorSignalNodeProvider>()?.GetNodeAt(node)?.netId : null;
        // switched network first, then the network of its control
        var before = controls.Select(c => (first: Net(c.switched), then: Net(c.control)))
            .Where(e => e.first.HasValue && e.then.HasValue && e.first != e.then).Select(e => (first: e.first.Value, then: e.then.Value)).Distinct().ToList();
        var left = nets.Keys.OrderBy(id => id).ToList();
        var order = new List<long>();
        while (left.Count > 0)
        {
            long next = left.FirstOrDefault(id => !before.Any(e => e.then == id && left.Contains(e.first)), left[0]);
            order.Add(next);
            left.Remove(next);
        }
        var byId = nets.ToDictionary(n => n.Key, n => n.Value);
        nets.Clear();
        foreach (long id in order) nets[id] = byId[id];
        return before.Count(e => order.IndexOf(e.then) < order.IndexOf(e.first));
    }

    // ---- driving and reading

    private BEBehaviorSignalNodeProvider Device(BlockPos pos) => entities[pos].GetBehavior<BEBehaviorSignalNodeProvider>();

    /// <summary>Sets the output of a source node, the way a socket drives its pins.</summary>
    public void Drive(BlockPos pos, int index, byte level) => Device(pos).UpdateSource(new NodePos(pos, index), level);

    public byte Level(BlockPos pos, int index) => Device(pos).GetNodeAt(new NodePos(pos, index)).value;

    /// <summary>A tube stack holding the program, as the imprinter would write it.</summary>
    public ItemStack Tube(SignalsTubes.src.circuit.CircuitProgram program, string name = "tube")
    {
        var stack = new ItemStack(TubeItem);
        TubeProgram.Set(stack, program, Store, null, null, false, false);
        stack.Attributes.SetString(TubeProgram.NameKey, name);
        return stack;
    }
}
