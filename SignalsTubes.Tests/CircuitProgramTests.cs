using SignalsTubes.src.circuit;

namespace SignalsTubes.Tests;

public class CircuitProgramTests
{
    // Pin 0 → resistor 3 → valve grid; supply → valve → pin 1; a delay hangs on the anode.
    private static CircuitProgram Sample()
    {
        var p = new CircuitProgram { NodeCount = 6 };
        p.Links.Add(new Link(0, 1, 3, 3));
        p.Links.Add(new Link(4, 5));                       // plain wire
        p.Components.Add(new Component(ComponentKind.Source, 15, 2));
        p.Components.Add(new Component(ComponentKind.Valve, 0, 1, 2, 3));
        p.Components.Add(new Component(ComponentKind.Delay, 2, 3, 4) { State = new byte[] { 1, 2, 3, 0, 0, 0 } });
        p.Components.Add(new Component(ComponentKind.Switch, 1, 3, 5));
        p.Pins.Add(Pin.Input(0, 0));
        p.Pins.Add(Pin.Output(1, 3));
        p.Pins.Add(Pin.Output(2, 5));
        p.Pins.Add(Pin.Delay(5, 2));
        p.Pins.Add(Pin.Switch(7, 3));
        return p;
    }

    [Fact]
    public void JsonRoundTripKeepsEverything()
    {
        string json = ProgramCodec.ToJson(Sample());
        var back = ProgramCodec.FromJson(json);
        Assert.Equal(json, ProgramCodec.ToJson(back));
        Assert.Equal(6, back.NodeCount);
        Assert.Equal(new byte[] { 1, 2, 3, 0, 0, 0 }, back.Components[2].State);
        Assert.Equal(PinRole.Switch, back.Pins[4].Role);
        Assert.Equal(3, back.Pins[4].Component);
        Assert.StartsWith("{\"v\":1,", json);
    }

    [Theory]
    [InlineData("not json", "valid JSON")]
    [InlineData("{\"v\":2,\"n\":1,\"links\":[],\"parts\":[],\"pins\":[]}", "format 2")]
    [InlineData("{\"v\":1,\"n\":1,\"links\":[[0,1,0,0]],\"parts\":[],\"pins\":[]}", "Node 1")]
    [InlineData("{\"v\":1,\"n\":1,\"links\":[[0,0,16,0]],\"parts\":[],\"pins\":[]}", "Level 16")]
    [InlineData("{\"v\":1,\"n\":3,\"links\":[],\"parts\":[{\"k\":\"valve\",\"n\":[0,1]}],\"pins\":[]}", "needs 3")]
    [InlineData("{\"v\":1,\"n\":2,\"links\":[],\"parts\":[{\"k\":\"delay\",\"n\":[0,1],\"p\":6}],\"pins\":[]}", "Delay setting")]
    [InlineData("{\"v\":1,\"n\":1,\"links\":[],\"parts\":[{\"k\":\"lamp\",\"n\":[0]}],\"pins\":[]}", "lamp")]
    [InlineData("{\"v\":1,\"n\":1,\"links\":[],\"parts\":[],\"pins\":[{\"i\":8,\"r\":\"in\",\"n\":0}]}", "Pin index 8")]
    [InlineData("{\"v\":1,\"n\":1,\"links\":[],\"parts\":[],\"pins\":[{\"i\":0,\"r\":\"in\",\"n\":0},{\"i\":0,\"r\":\"out\",\"n\":0}]}", "twice")]
    [InlineData("{\"v\":1,\"n\":1,\"links\":[],\"parts\":[{\"k\":\"source\",\"n\":[0]}],\"pins\":[{\"i\":0,\"r\":\"switch\",\"c\":0}]}", "Switch")]
    public void BrokenProgramsAreRejectedWithAReason(string json, string reason)
    {
        var e = Assert.Throws<FormatException>(() => ProgramCodec.FromJson(json));
        Assert.Contains(reason, e.Message);
    }

    [Fact]
    public void SimplifyMergesWiresAndParallelLinksWithoutChangingBehaviour()
    {
        var p = new CircuitProgram { NodeCount = 6 };
        p.Links.Add(new Link(0, 1));               // wire
        p.Links.Add(new Link(1, 2));               // wire
        p.Links.Add(new Link(2, 3, 4, 4));
        p.Links.Add(new Link(3, 2, 6, 2));         // parallel, reversed: keeps 2/4
        p.Links.Add(new Link(3, 3, 1, 1));         // self
        p.Links.Add(new Link(4, 5));               // wire to nothing
        p.Components.Add(new Component(ComponentKind.Source, 15, 0));
        p.Pins.Add(Pin.Output(0, 3));
        p.Pins.Add(Pin.Output(1, 2));

        var s = CircuitSimplifier.Simplify(p);
        Assert.Equal(2, s.NodeCount);
        var link = Assert.Single(s.Links);
        int supply = s.Components[0].Nodes[0];
        Assert.Equal((2, 4), link.A == supply ? (link.Att, link.RevAtt) : (link.RevAtt, link.Att));

        var a = new CircuitSimulator(p); var b = new CircuitSimulator(s);
        a.Step(); b.Step();
        Assert.Equal(a.GetOutput(0), b.GetOutput(0));
        Assert.Equal(13, b.GetOutput(0));
        Assert.Equal(15, b.GetOutput(1));
    }

    [Fact]
    public void FingerprintIgnoresNumberingOrderingAndWiring()
    {
        var original = Sample();
        string reference = CircuitFingerprint.Compute(original);

        // Renumber nodes, reorder parts and links, route a wire through extra connectors.
        var p = new CircuitProgram { NodeCount = 9 };
        int[] m = { 7, 2, 5, 0, 8, 3 };
        p.Components.Add(new Component(ComponentKind.Switch, 1, m[3], m[5]));
        p.Components.Add(new Component(ComponentKind.Delay, 2, m[3], m[4]));
        p.Components.Add(new Component(ComponentKind.Valve, 0, m[1], m[2], m[3]));
        p.Components.Add(new Component(ComponentKind.Source, 15, m[2]));
        p.Links.Add(new Link(m[4], 1));
        p.Links.Add(new Link(1, 6));
        p.Links.Add(new Link(6, m[5]));
        p.Links.Add(new Link(m[1], m[0], 3, 3));
        p.Pins.Add(Pin.Switch(7, 0));
        p.Pins.Add(Pin.Delay(5, 1));
        p.Pins.Add(Pin.Output(2, m[5]));
        p.Pins.Add(Pin.Output(1, m[3]));
        p.Pins.Add(Pin.Input(0, m[0]));
        Assert.Equal(reference, CircuitFingerprint.Compute(p));
    }

    [Fact]
    public void FingerprintSeesWiringChangesButNotTransientState()
    {
        string reference = CircuitFingerprint.Compute(Sample());

        var state = Sample();
        state.Components[2].State = new byte[6];
        Assert.Equal(reference, CircuitFingerprint.Compute(state));

        var exposedSwitchFlipped = Sample();
        exposedSwitchFlipped.Components[3].Param = 0;   // pulled out to a pin, so its setting is irrelevant
        Assert.Equal(reference, CircuitFingerprint.Compute(exposedSwitchFlipped));

        var resistor = Sample();
        resistor.Links[0].Att = 4;
        Assert.NotEqual(reference, CircuitFingerprint.Compute(resistor));

        var pins = Sample();
        pins.Pins[0].Index = 3;
        Assert.NotEqual(reference, CircuitFingerprint.Compute(pins));

        var delay = Sample();
        delay.Pins.RemoveAt(3);                          // delay no longer exposed: its setting counts
        string fixedDelay = CircuitFingerprint.Compute(delay);
        Assert.NotEqual(reference, fixedDelay);
        delay.Components[2].Param = 4;
        Assert.NotEqual(fixedDelay, CircuitFingerprint.Compute(delay));
    }

    [Fact]
    public void FingerprintDistinguishesMirroredPorts()
    {
        // Same parts, but the valve's cathode and anode swapped.
        var a = Sample();
        var b = Sample();
        b.Components[1].Nodes = new[] { 1, 3, 2 };
        Assert.NotEqual(CircuitFingerprint.Compute(a), CircuitFingerprint.Compute(b));
    }
}
