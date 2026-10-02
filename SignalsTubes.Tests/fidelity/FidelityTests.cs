using SignalsTubes.src.circuit;
using SignalsTubes.src.imprint;
using Vintagestory.API.MathTools;
using static SignalsTubes.Tests.fidelity.SignalsRig;

namespace SignalsTubes.Tests.fidelity;

/// <summary>
/// A circuit built from real Signals devices is read by the real bridge and reader, and the resulting
/// program is stepped next to the real network. The tube must do what the circuit does, step by step.
/// </summary>
public class FidelityTests
{
    private static CircuitProgram Read(SignalsRig rig, BlockPos socket)
    {
        var read = CircuitReader.Read(new SignalsCircuitWorld(rig.Api), socket);
        Assert.True(read.Ok, string.Join(", ", read.Refusals.Select(r => r.Pos + " " + r.Reason)));
        return ProgramCodec.FromJson(ProgramCodec.ToJson(CircuitSimplifier.Simplify(read.Program)));   // as stored
    }

    /// <summary>Stimulus held for a few steps, then changed: levels 0, 15 and anything between.</summary>
    private static byte[][] Stimulus(int seed, int steps)
    {
        var random = new Random(seed);
        var levels = new byte[steps][];
        var current = new byte[CircuitProgram.MaxPins];
        for (int t = 0; t < steps; t++)
        {
            for (int pin = 0; pin < current.Length; pin++)
                if (random.Next(4) == 0)
                    current[pin] = random.Next(3) switch { 0 => (byte)0, 1 => (byte)15, _ => (byte)random.Next(16) };
            levels[t] = (byte[])current.Clone();
        }
        return levels;
    }

    private static string Trace(IEnumerable<byte> levels) => string.Join(" ", levels.Select(l => l.ToString("x")));

    private static void AssertTubeMatchesCircuit(SignalsRig rig, BlockPos socket, int seed = 1, int steps = 80)
    {
        var program = Read(rig, socket);
        var sim = new CircuitSimulator(program, rig.Store.Get);
        var stimulus = Stimulus(seed, steps);
        var outputs = program.Pins.Where(p => p.Role == PinRole.Output).Select(p => p.Index).ToList();
        var real = outputs.ToDictionary(o => o, _ => new List<byte>());
        var ours = outputs.ToDictionary(o => o, _ => new List<byte>());
        for (int t = 0; t < steps; t++)
        {
            foreach (var pin in program.Pins.Where(p => p.Role == PinRole.Input))
            {
                rig.Drive(socket, pin.Index, stimulus[t][pin.Index]);
                sim.SetInput(pin.Index, stimulus[t][pin.Index]);
            }
            rig.Tick();
            sim.Step();
            foreach (int o in outputs) { real[o].Add(rig.Level(socket, o)); ours[o].Add(sim.GetOutput(o)); }
        }
        foreach (int o in outputs)
            Assert.True(real[o].SequenceEqual(ours[o]), $"seed {seed}, pin {o}\nreal {Trace(real[o])}\nours {Trace(ours[o])}\n{ProgramCodec.ToJson(program)}");
        Assert.NotEmpty(outputs);
    }

    /// <summary>
    /// Exact comparison from a cold start, a fresh circuit per stimulus. The real networks are first put
    /// into the order where every valve reacts in the next step, as a tube does.
    /// </summary>
    private static void AssertExact(Action<SignalsRig, BlockPos> build, int seeds = 5, int steps = 80)
    {
        for (int seed = 1; seed <= seeds; seed++)
        {
            var rig = new SignalsRig();
            var socket = rig.Socket(At(0));
            build(rig, socket);
            Assert.Equal(0, rig.ReactNextStepOrder());
            AssertTubeMatchesCircuit(rig, socket, seed, steps);
        }
    }

    private static void Inverter(SignalsRig rig, BlockPos socket)
    {
        var valve = rig.Valve(At(1));
        var source = rig.SourceBlock(At(2));
        rig.Wire(socket, 0, valve, 0);
        rig.Wire(source, 0, valve, 1);
        rig.Wire(valve, 2, socket, 1);
    }

    [Fact]
    public void InverterMatches() => AssertExact(Inverter);

    // Known difference: built grid-first, the real valve reacts within the same step; the tube is one step behind.
    [Fact]
    public void RealValveReactsAtOnceWhenItsGridNetworkIsOlder()
    {
        var rig = new SignalsRig();
        var socket = rig.Socket(At(0));
        Inverter(rig, socket);
        var sim = new CircuitSimulator(Read(rig, socket));
        var stimulus = Stimulus(3, 60);
        var real = new List<byte>();
        var ours = new List<byte>();
        for (int t = 0; t < stimulus.Length; t++)
        {
            rig.Drive(socket, 0, stimulus[t][0]);
            sim.SetInput(0, stimulus[t][0]);
            rig.Tick();
            sim.Step();
            real.Add(rig.Level(socket, 1));
            ours.Add(sim.GetOutput(1));
        }
        Assert.Equal(real.Take(real.Count - 1), ours.Skip(1));
    }

    [Fact]
    public void TwoValvesInSeriesWithResistorsKeepLevels() => AssertExact((rig, socket) =>
    {
        var source = rig.SourceBlock(At(1));
        var a = rig.Valve(At(2));
        var b = rig.Valve(At(3));
        var r1 = rig.Resistor(At(4), 3);
        var r2 = rig.Resistor(At(5), 2);
        var joint = rig.Connection(At(6));
        var pass = rig.PassThrough(At(7));
        rig.Wire(source, 0, r1, 0);
        rig.Wire(r1, 1, a, 1);
        rig.Wire(a, 2, pass, 0);
        rig.Wire(pass, 1, b, 1);
        rig.Wire(b, 2, joint, 0);
        rig.Wire(joint, 0, r2, 0);
        rig.Wire(r2, 1, socket, 2);
        rig.Wire(joint, 0, socket, 3);
        rig.Wire(socket, 0, a, 0);
        rig.Wire(socket, 1, b, 0);
    });

    [Fact]
    public void TetrodeMatches() => AssertExact((rig, socket) =>
    {
        var source = rig.SourceBlock(At(1));
        var tetrode = rig.Tetrode(At(2));
        rig.Wire(source, 0, tetrode, 1);
        rig.Wire(tetrode, 2, socket, 2);
        rig.Wire(socket, 0, tetrode, 0);
        rig.Wire(socket, 1, tetrode, 3);
    });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void DelayLine(int setting) => AssertExact((rig, socket) =>
    {
        var delay = rig.Delay(At(1), setting);
        rig.Wire(socket, 0, delay, 0);
        rig.Wire(delay, 1, socket, 1);
    });

    [Fact]
    public void ClockRunsAtTheSamePace() => AssertExact((rig, socket) =>
    {
        var source = rig.SourceBlock(At(1));
        var valve = rig.Valve(At(2));
        var delay = rig.Delay(At(3), 2);
        rig.Wire(source, 0, valve, 1);
        rig.Wire(valve, 2, delay, 0);
        rig.Wire(valve, 2, socket, 0);
        rig.Wire(delay, 1, valve, 0);
    }, seeds: 1);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActuatorOnASwitchIsTheSameMemory(bool startsOn) => AssertExact((rig, socket) =>
    {
        var source = rig.SourceBlock(At(1));
        var knife = rig.KnifeSwitch(At(6), startsOn);
        var actuator = rig.Actuator(At(5), BlockFacing.EAST);
        rig.Wire(source, 0, knife, 0);
        rig.Wire(knife, 1, socket, 1);
        rig.Wire(socket, 0, actuator, 0);
    });

    // A random pile of parts, randomly wired, feedback included. Returns false when the wiring has a
    // control loop, where no network order makes every real valve wait for the next step.
    private static bool RandomCircuit(int seed, out SignalsRig rig, out BlockPos socket)
    {
        var random = new Random(seed * 7919);
        rig = new SignalsRig();
        socket = rig.Socket(At(0));
        var nodes = new List<(BlockPos pos, int index)>();
        for (int pin = 0; pin < 2 + random.Next(4); pin++) nodes.Add((socket, pin));
        int parts = 3 + random.Next(8);
        for (int i = 1; i <= parts; i++)
        {
            BlockPos pos;
            int count;
            switch (random.Next(7))
            {
                case 0: pos = rig.SourceBlock(At(i), random.Next(2) == 0 ? 15 : 6 + random.Next(10)); count = 1; break;
                case 1: pos = rig.Resistor(At(i), 1 + random.Next(6)); count = 2; break;
                case 2 or 3: pos = rig.Valve(At(i)); count = 3; break;
                case 4: pos = rig.Tetrode(At(i)); count = 4; break;
                case 5: pos = rig.Delay(At(i), random.Next(6)); count = 2; break;
                default: pos = rig.Connection(At(i)); count = 1; break;
            }
            for (int n = 0; n < count; n++) nodes.Add((pos, n));
        }
        var wired = new HashSet<(int, int)>();
        for (int w = 0; w < nodes.Count + random.Next(nodes.Count); w++)
        {
            int a = random.Next(nodes.Count), b = random.Next(nodes.Count);
            if (nodes[a].pos.Equals(nodes[b].pos) || !wired.Add((Math.Min(a, b), Math.Max(a, b)))) continue;
            rig.Wire(nodes[a].pos, nodes[a].index, nodes[b].pos, nodes[b].index);
        }
        return rig.ReactNextStepOrder() == 0;
    }

    [Fact]
    public void RandomCircuitsMatch()
    {
        int compared = 0;
        for (int seed = 1; seed <= 300; seed++)
        {
            if (!RandomCircuit(seed, out var rig, out var socket)) continue;
            var read = CircuitReader.Read(new SignalsCircuitWorld(rig.Api), socket);
            if (!read.Ok || read.Program.Pins.All(p => p.Role != PinRole.Output)) continue;
            AssertTubeMatchesCircuit(rig, socket, seed, 60);
            compared++;
        }
        Assert.True(compared >= 200, "Only " + compared + " random circuits were comparable.");
    }

    // A tube sitting in a real socket inside the circuit: the composite read from it must keep its timing.
    [Fact]
    public void NestedTubeKeepsTheTimingOfItsSocket() => AssertExact((rig, socket) =>
    {
        var inverter = ProgramCodec.FromJson("{\"v\":1,\"n\":3,\"links\":[],\"parts\":[{\"k\":\"source\",\"n\":[1],\"p\":15},{\"k\":\"valve\",\"n\":[0,1,2]}],\"pins\":[{\"i\":0,\"r\":\"in\",\"n\":0},{\"i\":1,\"r\":\"out\",\"n\":2}]}");
        var first = rig.Socket(At(1), rig.Tube(inverter, "NOT"));
        var second = rig.Socket(At(2), rig.Tube(inverter, "NOT"));
        var delay = rig.Delay(At(3), 1);
        rig.Wire(socket, 0, first, 0);
        rig.Wire(first, 1, second, 0);
        rig.Wire(second, 1, delay, 0);
        rig.Wire(delay, 1, socket, 1);
        rig.Wire(first, 1, socket, 2);
        Assert.Equal(2, Read(rig, socket).Components.Count(c => c.Kind == ComponentKind.Buffer));   // both tubes were inlined
    });

    // Signals keeps one connection per pair of nodes, so a wire across a part's own two ends does nothing.
    [Theory]
    [InlineData("resistor")]
    [InlineData("switch")]
    [InlineData("valve")]
    [InlineData("tetrode")]
    [InlineData("passthrough")]
    public void WireAcrossAPartIsIgnoredLikeInSignals(string kind) => AssertExact((rig, socket) =>
    {
        var source = rig.SourceBlock(At(1));
        BlockPos part;
        int a = 0, b = 1;
        switch (kind)
        {
            case "resistor": part = rig.Resistor(At(2), 4); break;
            case "switch": part = rig.KnifeSwitch(At(2), false); break;
            case "passthrough": part = rig.PassThrough(At(2)); break;
            case "tetrode": part = rig.Tetrode(At(2)); a = 1; b = 2; rig.Wire(socket, 0, part, 0); rig.Wire(socket, 2, part, 3); break;
            default: part = rig.Valve(At(2)); a = 1; b = 2; rig.Wire(socket, 0, part, 0); break;
        }
        rig.Wire(source, 0, part, a);
        rig.Wire(part, a, part, b);
        rig.Wire(part, b, socket, 1);
    }, seeds: 2);
}
