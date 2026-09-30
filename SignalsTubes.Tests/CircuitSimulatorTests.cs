using SignalsTubes.src.circuit;

namespace SignalsTubes.Tests;

public class CircuitSimulatorTests
{
    private static CircuitProgram Program(int nodes, IEnumerable<Link> links, IEnumerable<Component> parts, params Pin[] pins)
    {
        var p = new CircuitProgram { NodeCount = nodes };
        p.Links.AddRange(links);
        p.Components.AddRange(parts);
        p.Pins.AddRange(pins);
        return p;
    }

    private static byte[] Run(CircuitSimulator sim, int steps, int outputPin, Action<int> beforeStep = null)
    {
        var trace = new byte[steps];
        for (int i = 0; i < steps; i++)
        {
            beforeStep?.Invoke(i);
            sim.Step();
            trace[i] = sim.GetOutput(outputPin);
        }
        return trace;
    }

    [Fact]
    public void SourceThroughResistorsLosesAttenuationAndParallelPathsTakeTheMaximum()
    {
        // 0 -3-> 1 -4-> 2, plus 0 -5-> 2 directly.
        var p = Program(3,
            new[] { new Link(0, 1, 3, 3), new Link(1, 2, 4, 4), new Link(0, 2, 5, 5) },
            new[] { new Component(ComponentKind.Source, 15, 0) },
            Pin.Output(0, 1), Pin.Output(1, 2));
        var sim = new CircuitSimulator(p);
        sim.Step();
        Assert.Equal(12, sim.GetOutput(0));
        Assert.Equal(10, sim.GetOutput(1));
    }

    [Fact]
    public void AttenuationIsDirectionalAndLevelsNeverGoBelowZero()
    {
        var p = Program(3,
            new[] { new Link(0, 1, 15, 0), new Link(1, 2, 2, 2) },
            new[] { new Component(ComponentKind.Source, 5, 1) },
            Pin.Output(0, 0), Pin.Output(1, 2));
        var sim = new CircuitSimulator(p);
        sim.Step();
        Assert.Equal(5, sim.GetOutput(0));   // 1→0 is free in reverse
        Assert.Equal(3, sim.GetOutput(1));
    }

    [Fact]
    public void InputPinDrivesItsNodeAndUnwiredNodesReadZero()
    {
        var p = Program(3, new[] { new Link(0, 1) }, Array.Empty<Component>(),
            Pin.Input(0, 0), Pin.Output(1, 1), Pin.Output(2, 2));
        var sim = new CircuitSimulator(p);
        sim.SetInput(0, 9);
        sim.Step();
        Assert.Equal(9, sim.GetOutput(1));
        Assert.Equal(0, sim.GetOutput(2));
        sim.SetInput(0, 40);
        sim.Step();
        Assert.Equal(15, sim.GetOutput(1));
    }

    [Fact]
    public void ValveInvertsWithOneStepLatency()
    {
        // grid = pin 0, cathode fed by a source, anode = pin 1.
        var p = Program(3, Array.Empty<Link>(),
            new[] { new Component(ComponentKind.Source, 15, 1), new Component(ComponentKind.Valve, 0, 0, 1, 2) },
            Pin.Input(0, 0), Pin.Output(1, 2));
        var sim = new CircuitSimulator(p);
        var trace = Run(sim, 4, 1, i => { if (i == 1) sim.SetInput(0, 15); });
        // Step 0: grid 0 → conducts. Step 1: grid rises, but the valve reacted to the old grid.
        Assert.Equal(new byte[] { 15, 15, 0, 0 }, trace);
        sim.SetInput(0, 5);
        sim.Step(); sim.Step();
        Assert.Equal(10, sim.GetOutput(1));
    }

    [Fact]
    public void ValveBlocksReverseDirection()
    {
        var p = Program(3, Array.Empty<Link>(),
            new[] { new Component(ComponentKind.Source, 15, 2), new Component(ComponentKind.Valve, 0, 0, 1, 2) },
            Pin.Output(0, 1));
        var sim = new CircuitSimulator(p);
        sim.Step(); sim.Step();
        Assert.Equal(0, sim.GetOutput(0));
    }

    [Fact]
    public void TetrodeAttenuationIsGridTimesScreenPlusOne()
    {
        var p = Program(4, Array.Empty<Link>(),
            new[] { new Component(ComponentKind.Source, 15, 1), new Component(ComponentKind.Tetrode, 0, 0, 1, 2, 3) },
            Pin.Input(0, 0), Pin.Input(1, 3), Pin.Output(2, 2));
        var sim = new CircuitSimulator(p);
        sim.SetInput(0, 2); sim.SetInput(1, 3);
        sim.Step(); sim.Step();
        Assert.Equal(15 - 8, sim.GetOutput(2));
        sim.SetInput(0, 4); sim.SetInput(1, 5);
        sim.Step(); sim.Step();
        Assert.Equal(0, sim.GetOutput(2));  // 24 clamps to 15
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    public void DelayOutputsTheInputSeenSettingPlusOneStepsAgo(byte setting)
    {
        var p = Program(2, Array.Empty<Link>(),
            new[] { new Component(ComponentKind.Delay, setting, 0, 1) },
            Pin.Input(0, 0), Pin.Output(1, 1));
        var sim = new CircuitSimulator(p);
        var trace = Run(sim, 10, 1, i => sim.SetInput(0, (byte)(i == 2 ? 7 : 0)));
        // Input 7 is present during step 2; a delay of k shows it at step 3 + k.
        for (int i = 0; i < trace.Length; i++) Assert.Equal(i == 3 + setting ? 7 : 0, trace[i]);
    }

    [Fact]
    public void DelayPinOverridesSettingOnlyWhenNonZero()
    {
        var p = Program(2, Array.Empty<Link>(),
            new[] { new Component(ComponentKind.Delay, 2, 0, 1) },
            Pin.Input(0, 0), Pin.Output(1, 1), Pin.Delay(2, 0));
        var sim = new CircuitSimulator(p);
        var trace = Run(sim, 8, 1, i => sim.SetInput(0, (byte)(i == 0 ? 7 : 0)));
        Assert.Equal(3, Array.IndexOf(trace, (byte)7));
        sim = new CircuitSimulator(p);
        sim.SetInput(2, 15);  // clamps to the longest delay, 5
        trace = Run(sim, 8, 1, i => sim.SetInput(0, (byte)(i == 0 ? 7 : 0)));
        Assert.Equal(6, Array.IndexOf(trace, (byte)7));
    }

    [Fact]
    public void SwitchUsesImprintedStateOrItsPin()
    {
        var p = Program(2, Array.Empty<Link>(),
            new[] { new Component(ComponentKind.Source, 15, 0), new Component(ComponentKind.Switch, 1, 0, 1) },
            Pin.Output(0, 1));
        var sim = new CircuitSimulator(p);
        sim.Step();
        Assert.Equal(15, sim.GetOutput(0));

        p.Pins.Add(Pin.Switch(1, 1));
        sim = new CircuitSimulator(p);
        sim.Step();
        Assert.Equal(0, sim.GetOutput(0));   // pin 1 is 0: off, whatever was imprinted
        sim.SetInput(1, 1);
        sim.Step();
        Assert.Equal(15, sim.GetOutput(0));
    }

    [Fact]
    public void ToggleFlipsOnRisingEdgesOnly()
    {
        var p = Program(3, Array.Empty<Link>(),
            new[] { new Component(ComponentKind.Source, 15, 1), new Component(ComponentKind.Toggle, 0, 0, 1, 2) },
            Pin.Input(0, 0), Pin.Output(1, 2));
        var sim = new CircuitSimulator(p);
        byte[] trigger = { 0, 15, 15, 0, 0, 15, 0, 3, 0, 5, 10, 10 };
        var trace = Run(sim, trigger.Length, 1, i => sim.SetInput(0, trigger[i]));
        // The trigger level of step i is seen by the toggle at step i + 1; 5 -> 10 counts as a new activation.
        Assert.Equal(new byte[] { 0, 0, 15, 15, 15, 15, 0, 0, 15, 15, 0, 15 }, trace);
    }

    [Fact]
    public void BufferAddsExactlyOneStep()
    {
        var p = Program(2, Array.Empty<Link>(),
            new[] { new Component(ComponentKind.Buffer, 0, 0, 1) },
            Pin.Input(0, 0), Pin.Output(1, 1));
        var sim = new CircuitSimulator(p);
        var trace = Run(sim, 4, 1, i => sim.SetInput(0, (byte)(i == 1 ? 6 : 0)));
        Assert.Equal(new byte[] { 0, 0, 6, 0 }, trace);
    }

    [Fact]
    public void InverterFedBackToItselfOscillatesEveryStep()
    {
        // Valve whose grid is its own anode: the one-step latency makes it a clock.
        var p = Program(2, Array.Empty<Link>(),
            new[] { new Component(ComponentKind.Source, 15, 0), new Component(ComponentKind.Valve, 0, 1, 0, 1) },
            Pin.Output(0, 1));
        var sim = new CircuitSimulator(p);
        Assert.Equal(new byte[] { 15, 0, 15, 0, 15, 0 }, Run(sim, 6, 0));
    }
}
