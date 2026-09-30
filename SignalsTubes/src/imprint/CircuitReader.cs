using SignalsTubes.src.circuit;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsTubes.src.imprint;

/// <summary>
/// Turns the Signals network hanging on a socket into a program. The socket's pins are the boundary:
/// everything reachable from them by wire is read, block by block. Blocks the tube cannot contain are
/// refused with a reason and their position, so the imprinter can point at them.
/// </summary>
public sealed class CircuitReader
{
    public sealed record Refusal(BlockPos Pos, string Reason);
    /// <summary>A switch or delay the author may pull out to a pin.</summary>
    public sealed record Adjustable(BlockPos Pos, ComponentKind Kind, int Component);

    public sealed class Result
    {
        public CircuitProgram Program = new();
        public List<Refusal> Refusals = new();
        public List<Adjustable> Adjustables = new();
        public bool Ok => Refusals.Count == 0;
    }

    public const string SocketPath = "tubesocket";
    private const string Signals = "signals";

    private readonly ICircuitWorld world;
    private readonly BlockPos socket;
    private readonly ISet<BlockPos> exposed;
    private readonly Result result = new();
    private readonly Dictionary<NodeRef, int> ids = new();
    private readonly HashSet<BlockPos> processed = new();
    private readonly HashSet<BlockPos> toggleActuators = new();
    private readonly HashSet<(int, int)> wires = new();
    private readonly Queue<NodeRef> queue = new();

    private CircuitReader(ICircuitWorld world, BlockPos socket, ISet<BlockPos> exposed)
    {
        this.world = world; this.socket = socket; this.exposed = exposed;
    }

    /// <param name="exposed">Positions of switches and delays the author wants on pins.</param>
    public static Result Read(ICircuitWorld world, BlockPos socket, ISet<BlockPos> exposed = null) =>
        new CircuitReader(world, socket, exposed ?? new HashSet<BlockPos>()).Run();

    private Result Run()
    {
        var p = result.Program;
        processed.Add(socket);
        var pinNodes = new int[CircuitProgram.MaxPins];
        for (int i = 0; i < CircuitProgram.MaxPins; i++)
        {
            var pin = new NodeRef(socket, i);
            pinNodes[i] = world.WiresFrom(pin).Any() ? Id(pin) : -1;
        }
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            foreach (var other in world.WiresFrom(node))
            {
                int a = Id(node), b = Id(other);
                if (wires.Add((Math.Min(a, b), Math.Max(a, b)))) p.Links.Add(new Link(a, b));
            }
            Visit(node.Pos);
        }

        var driven = Driven();
        for (int i = 0; i < CircuitProgram.MaxPins; i++)
            if (pinNodes[i] >= 0)
                p.Pins.Add(driven.Contains(pinNodes[i]) ? Pin.Output(i, pinNodes[i]) : Pin.Input(i, pinNodes[i]));
        foreach (var a in result.Adjustables.Where(a => exposed.Contains(a.Pos)))
        {
            int free = Enumerable.Range(0, CircuitProgram.MaxPins).FirstOrDefault(i => p.Pins.All(pin => pin.Index != i), -1);
            if (free < 0) { Refuse(a.Pos, "no-free-pin"); continue; }
            p.Pins.Add(a.Kind == ComponentKind.Switch ? Pin.Switch(free, a.Component) : Pin.Delay(free, a.Component));
        }
        p.NodeCount = ids.Count;
        return result;
    }

    private int Id(NodeRef node)
    {
        if (!ids.TryGetValue(node, out int id))
        {
            ids[node] = id = ids.Count;
            queue.Enqueue(node);
        }
        return id;
    }

    private void Refuse(BlockPos pos, string reason)
    {
        if (!result.Refusals.Any(r => r.Pos.Equals(pos) && r.Reason == reason)) result.Refusals.Add(new Refusal(pos.Copy(), reason));
    }

    // Node of the block's port; the port is wired into the program even if no wire touches it.
    private int Port(BlockPos pos, int index) => Id(new NodeRef(pos, index));

    private void Visit(BlockPos pos)
    {
        if (!processed.Add(pos)) return;
        var block = world.BlockAt(pos);
        var nodes = world.NodesAt(pos);
        if (block == null || nodes.Count == 0) { Refuse(pos, "no-device"); return; }
        string path = block.Code.Path;
        string kind = block.Code.Domain == Signals ? path.Split('-')[0] : block.Code.Domain + ":" + path.Split('-')[0];
        var p = result.Program;
        switch (kind)
        {
            case "connection" or "connection2" or "connection2v" or "blocklightbulb" or "blockbuzzer" or "blockmeter" or "blockscreen":
                break;
            case "pass_through_connector":
                p.Links.Add(new Link(Port(pos, 0), Port(pos, 1)));
                break;
            case "blockresistor":
            {
                byte att = (byte)Math.Clamp(Variant(block, "value", 0), 0, CircuitProgram.MaxLevel);
                p.Links.Add(new Link(Port(pos, 0), Port(pos, 1), att, att));
                break;
            }
            case "blocksource":
                p.Components.Add(new Component(ComponentKind.Source, world.SourceOutput(new NodeRef(pos, 0)), Port(pos, 0)));
                break;
            case "blockvalve":
                p.Components.Add(new Component(ComponentKind.Valve, 0, Port(pos, 0), Port(pos, 1), Port(pos, 2)));
                break;
            case "blocktetrode":
                p.Components.Add(new Component(ComponentKind.Tetrode, 0, Port(pos, 0), Port(pos, 1), Port(pos, 2), Port(pos, 3)));
                break;
            case "blockdelay":
            {
                var delay = new Component(ComponentKind.Delay, (byte)Math.Clamp(Variant(block, "value", 0), 0, CircuitProgram.DelaySlots - 1), Port(pos, 0), Port(pos, 1));
                var saved = world.EntityAttributes(pos)?.GetBytes("values");
                if (saved != null) delay.State = saved.Select(v => (byte)Math.Min(v, CircuitProgram.MaxLevel)).ToArray();
                p.Components.Add(delay);
                result.Adjustables.Add(new Adjustable(pos.Copy(), ComponentKind.Delay, p.Components.Count - 1));
                break;
            }
            case "knifeswitch":
            {
                byte on = (byte)(Variant(block, "state") == "on" ? 1 : 0);
                var actuators = BlockFacing.ALLFACES.Select(f => pos.AddCopy(f.Opposite)).Where(n => ActuatorTarget(n)?.Equals(pos) == true).ToList();
                if (actuators.Count > 1) { Refuse(pos, "switch-many-actuators"); break; }
                if (actuators.Count == 1)
                {
                    // An actuator flipping a switch is the player's memory cell: it stays inside the tube.
                    toggleActuators.Add(actuators[0]);
                    p.Components.Add(new Component(ComponentKind.Toggle, on, Port(actuators[0], 0), Port(pos, 0), Port(pos, 1)));
                }
                else
                {
                    p.Components.Add(new Component(ComponentKind.Switch, on, Port(pos, 0), Port(pos, 1)));
                    result.Adjustables.Add(new Adjustable(pos.Copy(), ComponentKind.Switch, p.Components.Count - 1));
                }
                break;
            }
            case "blockactuator":
            {
                var target = ActuatorTarget(pos);
                if (target != null && Kind(target) == "knifeswitch") Visit(target);   // the switch builds the toggle
                if (!toggleActuators.Contains(pos)) Refuse(pos, "actuator-target");
                break;
            }
            case "buttonswitch" or "pressureplate" or "blocklightsensor" or "blockanemometer":
                Refuse(pos, "world-driven");
                break;
            case "signalstubes:" + SocketPath:
                if (world.EntityAttributes(pos)?.HasAttribute("programTube") == true) Refuse(pos, "nested-tube");
                break;
            default:
                Refuse(pos, "unknown");
                break;
        }
    }

    private string Kind(BlockPos pos)
    {
        var block = world.BlockAt(pos);
        return block == null ? null : block.Code.Path.Split('-')[0];
    }

    private BlockPos ActuatorTarget(BlockPos pos)
    {
        var block = world.BlockAt(pos);
        if (block == null || block.Code.Domain != Signals || Kind(pos) != "blockactuator") return null;
        var facing = BlockFacing.FromCode(Variant(block, "orientation")) ?? BlockFacing.NORTH;
        return pos.AddCopy(facing);
    }

    private static string Variant(Block block, string code) =>
        block.Variant != null && block.Variant.TryGetValue(code, out string v) ? v : null;

    private static int Variant(Block block, string code, int fallback) =>
        int.TryParse(Variant(block, code), out int v) ? v : fallback;

    // Nodes that something inside the circuit can raise: sources and delay outputs, spread over links
    // and through parts in the direction they conduct. A pin on such a node is an output.
    private HashSet<int> Driven()
    {
        var p = result.Program;
        var driven = new HashSet<int>();
        var stack = new Stack<int>();
        void Add(int n) { if (driven.Add(n)) stack.Push(n); }
        foreach (var c in p.Components)
        {
            if (c.Kind == ComponentKind.Source) Add(c.Nodes[0]);
            if (c.Kind is ComponentKind.Delay or ComponentKind.Buffer) Add(c.Nodes[1]);
        }
        while (stack.Count > 0)
        {
            int n = stack.Pop();
            foreach (var l in p.Links)
            {
                if (l.A == n && l.Att < CircuitProgram.MaxLevel) Add(l.B);
                if (l.B == n && l.RevAtt < CircuitProgram.MaxLevel) Add(l.A);
            }
            foreach (var c in p.Components)
                switch (c.Kind)
                {
                    case ComponentKind.Switch when c.Nodes[0] == n: Add(c.Nodes[1]); break;
                    case ComponentKind.Switch when c.Nodes[1] == n: Add(c.Nodes[0]); break;
                    case ComponentKind.Toggle when c.Nodes[1] == n: Add(c.Nodes[2]); break;
                    case ComponentKind.Toggle when c.Nodes[2] == n: Add(c.Nodes[1]); break;
                    case ComponentKind.Valve or ComponentKind.Tetrode when c.Nodes[1] == n: Add(c.Nodes[2]); break;
                }
        }
        return driven;
    }
}
