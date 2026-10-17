using SignalsTubes.src.circuit;
using SignalsTubes.src.imprint;
using SignalsTubes.src.programtube;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SignalsTubes.Tests;

public class CircuitReaderTests
{
    /// <summary>A little world: blocks by position, their node indices, wires, saved state.</summary>
    private sealed class World : ICircuitWorld
    {
        private readonly Dictionary<BlockPos, (Block block, int[] nodes)> blocks = new();
        private readonly List<(NodeRef a, NodeRef b)> wires = new();
        private readonly Dictionary<BlockPos, ITreeAttribute> attributes = new();
        private readonly Dictionary<NodeRef, byte> outputs = new();
        private readonly Dictionary<BlockPos, NestedTube> tubes = new();

        public BlockPos Put(string code, int x, int nodeCount, params (string, string)[] variants)
        {
            var pos = new BlockPos(x, 0, 0);
            var block = new Block { Code = new AssetLocation(code) };
            foreach (var (k, v) in variants) block.VariantStrict[k] = v;
            blocks[pos] = (block, Enumerable.Range(0, nodeCount).ToArray());
            return pos;
        }
        public World Wire(BlockPos a, int ia, BlockPos b, int ib) { wires.Add((new NodeRef(a, ia), new NodeRef(b, ib))); return this; }
        public void Unwire(BlockPos pos) => wires.RemoveAll(w => w.a.Pos.Equals(pos) || w.b.Pos.Equals(pos));
        public World Output(BlockPos pos, byte level) { outputs[new NodeRef(pos, 0)] = level; return this; }
        public World State(BlockPos pos, ITreeAttribute tree) { attributes[pos] = tree; return this; }
        public World Tube(BlockPos pos, NestedTube tube) { tubes[pos] = tube; return this; }

        public Block BlockAt(BlockPos pos) => blocks.TryGetValue(pos, out var b) ? b.block : null;
        public IReadOnlyList<int> NodesAt(BlockPos pos) => blocks.TryGetValue(pos, out var b) ? b.nodes : Array.Empty<int>();
        public byte SourceOutput(NodeRef node) => outputs.TryGetValue(node, out byte v) ? v : (byte)15;
        public IEnumerable<NodeRef> WiresFrom(NodeRef node) =>
            wires.Where(w => w.a == node).Select(w => w.b).Concat(wires.Where(w => w.b == node).Select(w => w.a));
        public ITreeAttribute EntityAttributes(BlockPos pos) => attributes.TryGetValue(pos, out var t) ? t : null;
        public NestedTube TubeAt(BlockPos pos, string readerUid) => tubes.TryGetValue(pos, out var t) ? t : null;
    }

    private static NestedTube Inverter(bool solder)
    {
        var p = ProgramCodec.FromJson("{\"v\":1,\"n\":3,\"links\":[],\"parts\":[{\"k\":\"source\",\"n\":[1],\"p\":15},{\"k\":\"valve\",\"n\":[0,1,2]}],\"pins\":[{\"i\":0,\"r\":\"in\",\"n\":0,\"name\":\"A\"},{\"i\":1,\"r\":\"out\",\"n\":2}]}");
        return new NestedTube { Name = "Inverter", Description = "NOT", ProgramId = "prog-inv", Solder = solder, Program = solder ? null : p,
            Pins = new() { new PublicPin(0, PinRole.Input, "A"), new PublicPin(1, PinRole.Output, null) } };
    }

    private static (World world, BlockPos socket) Socket()
    {
        var w = new World();
        return (w, w.Put("signalstubes:tubesocket-north-down", 0, 8));
    }

    // A damper's output wired to a pin: the damper stays outside, the pin is an input of the tube; the
    // tube may also drive such a device, then the pin is an output. Deeper inside the circuit it is refused.
    [Fact]
    public void DevicesOfOtherModsStayOutsideWhenTheyHangOnAPin()
    {
        var (w, socket) = Socket();
        var damper = w.Put("signalslink:sleevedamper-north-down", 1, 4);
        var chute = w.Put("signalslink:managedchute-north-down", 2, 4);
        var source = w.Put("signals:blocksource", 3, 1);
        w.Wire(damper, 3, socket, 6).Wire(source, 0, socket, 2).Wire(socket, 2, chute, 0);
        var r = CircuitReader.Read(w, socket);
        Assert.True(r.Ok);
        Assert.Equal(PinRole.Input, r.Program.Pins.Single(p => p.Index == 6).Role);
        Assert.Equal(PinRole.Output, r.Program.Pins.Single(p => p.Index == 2).Role);
        Assert.Equal(new[] { ComponentKind.Source }, r.Program.Components.Select(c => c.Kind));

        var (w2, socket2) = Socket();
        var valve = w2.Put("signals:blockvalve-off-north-down", 1, 3);
        var sensor = w2.Put("signalslink:blocksensor-off-fwd-north-down", 2, 2);
        w2.Wire(socket2, 0, valve, 1).Wire(sensor, 0, valve, 0).Wire(valve, 2, socket2, 1);
        var r2 = CircuitReader.Read(w2, socket2);
        Assert.True(r2.Ok);
        Assert.Equal("external", r2.Ignored.Single().Reason);
    }

    [Fact]
    public void InverterOnASocketBecomesValveWithInputAndOutputPins()
    {
        var (w, socket) = Socket();
        var valve = w.Put("signals:blockvalve-off-north-down", 1, 3);
        var source = w.Put("signals:blocksource", 2, 1);
        var connector = w.Put("signals:connection-down", 3, 1);
        w.Wire(socket, 0, connector, 0).Wire(connector, 0, valve, 0).Wire(source, 0, valve, 1).Wire(valve, 2, socket, 3);

        var r = CircuitReader.Read(w, socket);
        Assert.True(r.Ok);
        var p = r.Program;
        Assert.Equal(new[] { ComponentKind.Valve, ComponentKind.Source }, p.Components.Select(c => c.Kind));
        Assert.Equal(15, p.Components[1].Param);
        Assert.Equal(2, p.Pins.Count);
        Assert.Equal(PinRole.Input, p.Pins.Single(x => x.Index == 0).Role);
        Assert.Equal(PinRole.Output, p.Pins.Single(x => x.Index == 3).Role);
        Assert.Empty(r.Adjustables);

        var sim = new CircuitSimulator(CircuitSimplifier.Simplify(p));
        sim.SetInput(0, 15); sim.Step(); sim.Step();
        Assert.Equal(0, sim.GetOutput(3));
        sim.SetInput(0, 0); sim.Step(); sim.Step();
        Assert.Equal(15, sim.GetOutput(3));
    }

    [Fact]
    public void ResistorsPassThroughsAndSavedDelayStateAreRead()
    {
        var (w, socket) = Socket();
        var resistor = w.Put("signals:blockresistor-4-north-down", 1, 2, ("value", "4"));
        var pass = w.Put("signals:pass_through_connector-down", 2, 2);
        var delay = w.Put("signals:blockdelay-north-down-3", 3, 2, ("value", "3"));
        var tree = new TreeAttribute(); tree.SetBytes("values", new byte[] { 1, 2, 3, 4, 5, 6 });
        w.State(delay, tree);
        w.Wire(socket, 0, resistor, 0).Wire(resistor, 1, pass, 0).Wire(pass, 1, delay, 0).Wire(delay, 1, socket, 1);

        var r = CircuitReader.Read(w, socket);
        Assert.True(r.Ok);
        var p = r.Program;
        Assert.Contains(p.Links, l => l.Att == 4 && l.RevAtt == 4);
        var d = Assert.Single(p.Components);
        Assert.Equal(ComponentKind.Delay, d.Kind);
        Assert.Equal(3, d.Param);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, d.State);
        Assert.Equal(PinRole.Input, p.Pins.Single(x => x.Index == 0).Role);
        Assert.Equal(PinRole.Output, p.Pins.Single(x => x.Index == 1).Role);
        var adj = Assert.Single(r.Adjustables);
        Assert.Equal(ComponentKind.Delay, adj.Kind);
    }

    [Fact]
    public void SwitchIsAdjustableAndExposedOnesGetFreePins()
    {
        var (w, socket) = Socket();
        var sw = w.Put("signals:knifeswitch-north-down-on", 1, 2, ("state", "on"));
        var source = w.Put("signals:blocksource", 2, 1);
        w.Wire(source, 0, sw, 0).Wire(sw, 1, socket, 0);

        var r = CircuitReader.Read(w, socket);
        Assert.True(r.Ok);
        var s = Assert.Single(r.Program.Components, c => c.Kind == ComponentKind.Switch);
        Assert.Equal(1, s.Param);
        Assert.Equal(PinRole.Output, Assert.Single(r.Program.Pins).Role);

        r = CircuitReader.Read(w, socket, new HashSet<BlockPos> { sw });
        Assert.True(r.Ok);
        var pin = Assert.Single(r.Program.Pins, x => x.Role == PinRole.Switch);
        Assert.Equal(1, pin.Index);   // first free index after the wired pin 0
        Assert.Equal(0, pin.Component);

        // a machine's socket: its own pins 0-5 are never handed out, the switch lands on the first reserve pin
        r = CircuitReader.Read(w, socket, new HashSet<BlockPos> { sw }, null, new HashSet<int> { 0, 1, 2, 3, 4, 5 });
        Assert.Equal(6, Assert.Single(r.Program.Pins, x => x.Role == PinRole.Switch).Index);
        r = CircuitReader.Read(w, socket, new HashSet<BlockPos> { sw }, null, new HashSet<int> { 0, 1, 2, 3, 4, 5, 6, 7 });
        Assert.False(r.Ok);
        Assert.Equal("no-free-pin", Assert.Single(r.Refusals).Reason);
    }

    [Fact]
    public void ActuatorFacingASwitchBecomesAToggleAndStrayActuatorIsRefused()
    {
        var (w, socket) = Socket();
        var actuator = w.Put("signals:blockactuator-off-east-down", 1, 1, ("orientation", "east"));
        var sw = w.Put("signals:knifeswitch-north-down-off", 2, 2, ("state", "off"));
        var source = w.Put("signals:blocksource", 3, 1);
        w.Wire(socket, 0, actuator, 0).Wire(source, 0, sw, 0).Wire(sw, 1, socket, 1);

        var r = CircuitReader.Read(w, socket);
        Assert.True(r.Ok);
        var t = Assert.Single(r.Program.Components, c => c.Kind == ComponentKind.Toggle);
        Assert.Equal(0, t.Param);
        Assert.Empty(r.Adjustables);
        Assert.Equal(PinRole.Input, r.Program.Pins.Single(x => x.Index == 0).Role);
        Assert.Equal(PinRole.Output, r.Program.Pins.Single(x => x.Index == 1).Role);

        var sim = new CircuitSimulator(CircuitSimplifier.Simplify(r.Program));
        sim.Step();
        Assert.Equal(0, sim.GetOutput(1));
        sim.SetInput(0, 15); sim.Step(); sim.Step();
        Assert.Equal(15, sim.GetOutput(1));

        var (w2, socket2) = Socket();
        var stray = w2.Put("signals:blockactuator-off-east-down", 1, 1, ("orientation", "east"));
        w2.Put("signals:blocklightbulb-off-north-down", 2, 1);
        w2.Wire(socket2, 0, stray, 0);
        r = CircuitReader.Read(w2, socket2);
        Assert.True(r.Ok);
        Assert.Equal("actuator-target", Assert.Single(r.Ignored).Reason);
        Assert.Empty(r.Program.Pins);
    }

    [Fact]
    public void WorldDrivenPartsAndUnknownBlocksAreRefusedWithPositions()
    {
        var (w, socket) = Socket();
        var button = w.Put("signals:buttonswitch-north-down-off", 1, 2);
        var strange = w.Put("signals:blockgizmo-north", 2, 1);   // a Signals part the reader does not know
        var lamp = w.Put("signals:blocklightbulb-off-north-down", 4, 1);
        w.Wire(socket, 0, button, 0).Wire(socket, 1, strange, 0).Wire(socket, 3, lamp, 0);

        var r = CircuitReader.Read(w, socket);
        Assert.Equal(new[] { ("world-driven", 1), ("unknown", 2) },
            r.Ignored.Select(x => (x.Reason, x.Pos.X)).OrderBy(x => x.Item2));
        Assert.True(r.Ok);
        Assert.Equal(3, Assert.Single(r.Program.Pins).Index); // only the harmless lamp remains
    }

    [Fact]
    public void UnwiredPinsAreLeftOutAndWireLoopsDoNotDuplicateLinks()
    {
        var (w, socket) = Socket();
        var a = w.Put("signals:connection-down", 1, 1);
        var b = w.Put("signals:connection-down", 2, 1);
        w.Wire(socket, 7, a, 0).Wire(a, 0, b, 0).Wire(b, 0, socket, 7);

        var r = CircuitReader.Read(w, socket);
        Assert.True(r.Ok);
        Assert.Equal(7, Assert.Single(r.Program.Pins).Index);
        Assert.Equal(3, r.Program.Links.Count);
        Assert.Equal(1, CircuitSimplifier.Simplify(r.Program).NodeCount);
    }

    [Fact]
    public void OwnNestedTubeIsInlinedAsAGroupWithInputBuffers()
    {
        var (w, socket) = Socket();
        var inner = w.Put("signalstubes:tubesocket-north-down", 1, 8);
        w.Tube(inner, Inverter(solder: false));
        w.Wire(socket, 0, inner, 0).Wire(inner, 1, socket, 1);

        var r = CircuitReader.Read(w, socket, null, "me");
        Assert.True(r.Ok);
        Assert.Empty(r.Soldered);
        var p = r.Program;
        var group = Assert.Single(p.Groups);
        Assert.Equal("Inverter", group.Name);
        Assert.Null(group.Ref);
        Assert.Equal(new[] { "A", null }, group.Pins.Select(gp => gp.Name));
        Assert.All(p.Components, c => Assert.Equal(0, c.Group));
        Assert.Contains(p.Components, c => c.Kind == ComponentKind.Buffer);
        Assert.Equal(PinRole.Input, p.Pins.Single(x => x.Index == 0).Role);
        Assert.Equal(PinRole.Output, p.Pins.Single(x => x.Index == 1).Role);

        // Behaves like the placed tube did: inverter plus one step on the input.
        var sim = new CircuitSimulator(CircuitSimplifier.Simplify(p));
        sim.SetInput(0, 15);
        var trace = new List<byte>();
        for (int i = 0; i < 4; i++) { sim.Step(); trace.Add(sim.GetOutput(1)); }
        Assert.Equal(new byte[] { 15, 15, 0, 0 }, trace);
    }

    [Fact]
    public void ForeignLockedTubeIsSolderedByReferenceAndListedForConsumption()
    {
        var (w, socket) = Socket();
        var inner = w.Put("signalstubes:tubesocket-north-down", 1, 8);
        w.Tube(inner, Inverter(solder: true));
        w.Wire(socket, 0, inner, 0).Wire(inner, 1, socket, 1);

        var r = CircuitReader.Read(w, socket, null, "someone-else");
        Assert.True(r.Ok);
        var soldered = Assert.Single(r.Soldered);
        Assert.Equal("Inverter", soldered.Name);
        var tube = Assert.Single(r.Program.Components);
        Assert.Equal(ComponentKind.Tube, tube.Kind);
        Assert.Equal("prog-inv", tube.Ref);
        Assert.Equal(2, tube.Nodes.Length);
        Assert.Equal("prog-inv", Assert.Single(r.Program.Groups).Ref);
        Assert.Equal(PinRole.Output, r.Program.Pins.Single(x => x.Index == 1).Role);

        var program = CircuitSimplifier.Simplify(r.Program);
        Assert.True(program.HasReferences);
        var sim = new CircuitSimulator(program, id => id == "prog-inv" ? Inverter(false).Program : null);
        sim.SetInput(0, 15);
        var trace = new List<byte>();
        for (int i = 0; i < 4; i++) { sim.Step(); trace.Add(sim.GetOutput(1)); }
        Assert.Equal(new byte[] { 15, 15, 0, 0 }, trace);   // same timing as the inlined one

        var back = ProgramCodec.FromJson(ProgramCodec.ToJson(program));
        Assert.Equal("prog-inv", back.Components[0].Ref);
        Assert.Equal("Inverter", back.Groups[0].Name);
    }

    [Fact]
    public void InlinedParameterPinReadsTheOuterNode()
    {
        var delay = ProgramCodec.FromJson("{\"v\":1,\"n\":2,\"links\":[],\"parts\":[{\"k\":\"delay\",\"n\":[0,1],\"p\":1}],\"pins\":[{\"i\":0,\"r\":\"in\",\"n\":0},{\"i\":1,\"r\":\"out\",\"n\":1},{\"i\":2,\"r\":\"delay\",\"c\":0}]}");
        var (w, socket) = Socket();
        var inner = w.Put("signalstubes:tubesocket-north-down", 1, 8);
        w.Tube(inner, new NestedTube { Name = "D", Program = delay, Pins = new() { new(0, PinRole.Input, null), new(1, PinRole.Output, null), new(2, PinRole.Delay, null) } });
        w.Wire(socket, 0, inner, 0).Wire(inner, 1, socket, 1).Wire(socket, 2, inner, 2);

        var r = CircuitReader.Read(w, socket, null, "me");
        Assert.True(r.Ok);
        var d = Assert.Single(r.Program.Components, c => c.Kind == ComponentKind.Delay);
        Assert.True(d.ParamNode >= 0);
        var p = CircuitSimplifier.Simplify(r.Program);
        Assert.Contains("\"pn\"", ProgramCodec.ToJson(p));

        byte[] Run(byte setting)
        {
            var sim = new CircuitSimulator(p);
            sim.SetInput(2, setting);
            var trace = new byte[10];
            for (int i = 0; i < trace.Length; i++) { sim.SetInput(0, (byte)(i == 0 ? 9 : 0)); sim.Step(); trace[i] = sim.GetOutput(1); }
            return trace;
        }
        // Setting 0 keeps the imprinted delay (1); 4 makes it 4. Each is one step later than the bare delay: the input buffer.
        Assert.Equal(3, Array.IndexOf(Run(0), (byte)9));
        Assert.Equal(6, Array.IndexOf(Run(4), (byte)9));
    }

    [Fact]
    public void ThresholdNotWithResistorAfterTheAnodeHasAnOutputPin()
    {
        var (w, socket) = Socket();
        var valve = w.Put("signals:blockvalve-off-north-down", 1, 3);
        var source = w.Put("signals:blocksource", 2, 1);
        var resistor = w.Put("signals:blockresistor-14-north-down", 3, 2, ("value", "14"));
        w.Wire(socket, 0, valve, 0).Wire(source, 0, valve, 1).Wire(valve, 2, resistor, 0).Wire(resistor, 1, socket, 1);
        var r = CircuitReader.Read(w, socket);
        Assert.True(r.Ok);
        Assert.Equal(PinRole.Input, r.Program.Pins.Single(x => x.Index == 0).Role);
        Assert.Equal(PinRole.Output, r.Program.Pins.Single(x => x.Index == 1).Role);
    }

    // No source inside: the anode alone makes pin 6 an output, fed from the input pins.
    [Fact]
    public void MinFromTwoTriodesWithoutSourceHasAnOutputPin()
    {
        var (w, socket) = Socket();
        var t1 = w.Put("signals:blockvalve-off-north-down", 1, 3);
        var t2 = w.Put("signals:blockvalve-off-north-down", 2, 3);
        w.Wire(socket, 0, t1, 1).Wire(socket, 0, t2, 1).Wire(socket, 2, t1, 0).Wire(t1, 2, t2, 0).Wire(t2, 2, socket, 5);
        var r = CircuitReader.Read(w, socket);
        Assert.True(r.Ok);
        Assert.Equal(PinRole.Input, r.Program.Pins.Single(x => x.Index == 0).Role);
        Assert.Equal(PinRole.Input, r.Program.Pins.Single(x => x.Index == 2).Role);
        Assert.Equal(PinRole.Output, r.Program.Pins.Single(x => x.Index == 5).Role);

        var sim = new CircuitSimulator(CircuitSimplifier.Simplify(r.Program));
        foreach (var (a, b) in new[] { (9, 4), (4, 9), (15, 15), (0, 7), (7, 0) })
        {
            sim.SetInput(0, (byte)a); sim.SetInput(2, (byte)b);
            for (int i = 0; i < 4; i++) sim.Step();
            Assert.Equal(Math.Min(a, b), sim.GetOutput(5));
        }
    }

    // Delay + triode loop holds a value. SET reaches the loop through a diode triode, so it stays an input.
    [Fact]
    public void MemoryLoopSetThroughDiodeStaysAnInput()
    {
        var (w, socket) = Socket();
        var diode = w.Put("signals:blockvalve-off-north-down", 1, 3);
        var delay = w.Put("signals:blockdelay-north-down-1", 2, 2, ("value", "1"));
        var loop = w.Put("signals:blockvalve-off-north-down", 3, 3);
        w.Wire(socket, 0, diode, 1).Wire(diode, 2, delay, 0).Wire(delay, 1, loop, 1).Wire(loop, 2, delay, 0)
            .Wire(socket, 1, loop, 0).Wire(delay, 1, socket, 2);
        var r = CircuitReader.Read(w, socket);
        Assert.True(r.Ok);
        Assert.Equal(PinRole.Input, r.Program.Pins.Single(x => x.Index == 0).Role);
        Assert.Equal(PinRole.Input, r.Program.Pins.Single(x => x.Index == 1).Role);
        Assert.Equal(PinRole.Output, r.Program.Pins.Single(x => x.Index == 2).Role);

        var sim = new CircuitSimulator(CircuitSimplifier.Simplify(r.Program));
        void Run(byte set, byte reset) { sim.SetInput(0, set); sim.SetInput(1, reset); for (int i = 0; i < 8; i++) sim.Step(); }
        Run(9, 0); Run(0, 0);
        Assert.Equal(9, sim.GetOutput(2));
        Run(0, 15); Run(0, 0);
        Assert.Equal(0, sim.GetOutput(2));
    }

    [Fact]
    public void UnsupportedBlockCutsItsBranchButDoesNotHideOtherPaths()
    {
        var (w, socket) = Socket();
        var button = w.Put("signals:buttonswitch-off", 1, 2);
        var hiddenSource = w.Put("signals:blocksource", 2, 1);
        var keptSource = w.Put("signals:blocksource", 3, 1);
        w.Wire(socket, 0, button, 0).Wire(button, 1, hiddenSource, 0).Wire(socket, 1, keptSource, 0);
        var read = CircuitReader.Read(w, socket);
        Assert.True(read.Ok);
        Assert.Single(read.Ignored);
        Assert.Single(read.Program.Components);
        Assert.Equal(1, Assert.Single(read.Program.Pins).Index);
        // The same source becomes reachable by another path; the button itself is still absent.
        w.Wire(socket, 2, hiddenSource, 0);
        read = CircuitReader.Read(w, socket);
        Assert.Equal(2, read.Program.Components.Count);
        Assert.Equal(new[] { 1, 2 }, read.Program.Pins.Select(p => p.Index));
    }

    [Fact]
    public void ExcludedDebugSourceCannotTurnAnInputIntoAnOutput()
    {
        var (w, socket) = Socket();
        var valve = w.Put("signals:blockvalve", 1, 3);
        var supply = w.Put("signals:blocksource", 2, 1);
        var debug = w.Put("signals:blocksource", 3, 1);
        w.Wire(socket, 0, valve, 0).Wire(debug, 0, valve, 0).Wire(supply, 0, valve, 1).Wire(valve, 2, socket, 1);
        Assert.Equal(PinRole.Output, CircuitReader.Read(w, socket).Program.Pins[0].Role);
        var read = CircuitReader.Read(w, socket, excluded: new HashSet<BlockPos> { debug });
        Assert.True(read.Ok);
        Assert.Empty(read.Ignored);
        Assert.Equal(PinRole.Input, read.Program.Pins[0].Role);
        var p = CircuitSimplifier.Simplify(read.Program);
        Assert.Equal(2, p.Components.Count);
        var sim = new CircuitSimulator(p);
        sim.SetInput(0, 4); sim.Step(); sim.Step();
        Assert.Equal(11, sim.GetOutput(1));
    }

    [Fact]
    public void ExcludedSwitchCutsTheBranchAndCannotBeExposed()
    {
        var (w, socket) = Socket();
        var sw = w.Put("signals:knifeswitch-on", 1, 2, ("state", "on"));
        var supply = w.Put("signals:blocksource", 2, 1);
        w.Wire(socket, 0, sw, 0).Wire(sw, 1, supply, 0);
        var read = CircuitReader.Read(w, socket, new HashSet<BlockPos> { sw }, excluded: new HashSet<BlockPos> { sw });
        Assert.True(read.Ok);
        Assert.Empty(read.Program.Components);
        Assert.Empty(read.Program.Pins);
        Assert.Empty(read.Adjustables);
    }

    [Fact]
    public void ExcludedNestedTubeIsNeitherInlinedNorConsumed()
    {
        var (w, socket) = Socket();
        var inner = w.Put("signalstubes:tubesocket-north-down", 1, 8);
        w.Tube(inner, Inverter(true));
        w.Wire(socket, 0, inner, 0).Wire(inner, 1, socket, 1);
        var read = CircuitReader.Read(w, socket, excluded: new HashSet<BlockPos> { inner });
        Assert.True(read.Ok);
        Assert.Empty(read.Soldered);
        Assert.Empty(read.Program.Groups);
        Assert.Empty(read.Program.Components);
    }

    [Fact]
    public void ExcludingAnActuatorLeavesAManualSwitchAndExcludingItsSwitchCutsTheActuator()
    {
        var (w, socket) = Socket();
        var actuator = w.Put("signals:blockactuator-off-east-down", 1, 1, ("orientation", "east"));
        var sw = w.Put("signals:knifeswitch-on", 2, 2, ("state", "on"));
        var supply = w.Put("signals:blocksource", 3, 1);
        w.Wire(socket, 0, actuator, 0).Wire(socket, 1, sw, 1).Wire(sw, 0, supply, 0);
        var read = CircuitReader.Read(w, socket, excluded: new HashSet<BlockPos> { actuator });
        Assert.True(read.Ok);
        Assert.Contains(read.Program.Components, c => c.Kind == ComponentKind.Switch);
        Assert.DoesNotContain(read.Program.Components, c => c.Kind == ComponentKind.Toggle);
        read = CircuitReader.Read(w, socket, excluded: new HashSet<BlockPos> { sw });
        Assert.True(read.Ok);
        Assert.Empty(read.Program.Components);
        Assert.Equal("actuator-target", Assert.Single(read.Ignored).Reason);
    }

    [Fact]
    public void ExclusionsSurviveSignalChangesAndChunkUnloadingButNotStructuralChanges()
    {
        var (w, socket) = Socket();
        var imprinter = new BlockPos(30, 0, 0);
        var sw = w.Put("signals:knifeswitch-off", 1, 2, ("state", "off"), ("orientation", "north"));
        w.Wire(socket, 0, sw, 0);
        var memory = new ImprintExclusions();
        Assert.True(memory.Toggle(imprinter, sw, w));
        w.Put("signals:knifeswitch-on", 1, 2, ("state", "on"), ("orientation", "north"));
        Assert.Contains(sw, memory.Current(imprinter, w, _ => true));
        w.Put("signals:knifeswitch-on", 1, 0, ("state", "on"), ("orientation", "north"));
        Assert.Contains(sw, memory.Current(imprinter, w, _ => true)); // device still initializing
        w.Put("signals:knifeswitch-on", 1, 2, ("state", "on"), ("orientation", "north"));
        w.Unwire(sw);
        Assert.Contains(sw, memory.Current(imprinter, w, _ => false)); // unloaded chunks cannot invalidate a selection
        Assert.Empty(memory.Current(imprinter, w, _ => true));
        memory.Toggle(imprinter, sw, w);
        w.Put("signals:knifeswitch-on", 1, 2, ("state", "on"), ("orientation", "south"));
        Assert.Empty(memory.Current(imprinter, w, _ => true));
        memory.Toggle(imprinter, sw, w);
        memory.Changed(sw); // remove and replace with an identical block between reads
        Assert.Empty(memory.Current(imprinter, w, _ => true));
        memory.Toggle(imprinter, sw, w);
        Assert.Empty(new ImprintExclusions().Current(imprinter, w, _ => true)); // new server session
        Assert.Empty(memory.Current(socket, w, _ => true)); // another imprinter
        Assert.False(memory.Toggle(imprinter, sw, w));
    }
}
