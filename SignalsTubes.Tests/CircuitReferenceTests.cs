using SignalsTubes.src.programtube;
using SignalsTubes.src.circuit;
using SignalsTubes.src.socket;
using SignalsTubes.Tests.fidelity;

namespace SignalsTubes.Tests;

public class CircuitReferenceTests
{
    private static CircuitProgram Wrapper(string reference) => new()
    {
        NodeCount = 2,
        Pins = new() { Pin.Input(0, 0), Pin.Output(1, 1) },
        Components = new() { new Component(ComponentKind.Tube, 0, 0, 1) { Ref = reference } }
    };

    [Fact]
    public void SelfReferenceIsRejected()
    {
        var a = Wrapper("A");
        Assert.Throws<CircuitProgramException>(() => new CircuitSimulator(a, _ => a));
    }

    [Fact]
    public void MutualReferenceIsRejected()
    {
        var a = Wrapper("B");
        var b = Wrapper("A");
        Assert.Throws<CircuitProgramException>(() => new CircuitSimulator(a, id => id == "A" ? a : b));
    }

    [Fact]
    public void ReferenceCycleIsRejectedEvenWhenResolverReturnsFreshObjects()
    {
        Assert.Throws<CircuitProgramException>(() => new CircuitSimulator(Wrapper("A"), _ => Wrapper("A")));
    }

    [Fact]
    public void SiblingsCanShareAProgramWithIndependentState()
    {
        var leaf = new CircuitProgram { NodeCount = 2,
            Pins = new() { Pin.Input(0, 0), Pin.Output(1, 1) },
            Components = new() { new Component(ComponentKind.Delay, 0, 0, 1) } };
        var root = new CircuitProgram { NodeCount = 4,
            Pins = new() { Pin.Input(0, 0), Pin.Input(1, 2), Pin.Output(2, 1), Pin.Output(3, 3) },
            Components = new() { new Component(ComponentKind.Tube, 0, 0, 1) { Ref = "leaf" },
                new Component(ComponentKind.Tube, 0, 2, 3) { Ref = "leaf" } } };
        var sim = new CircuitSimulator(root, _ => leaf);
        sim.SetInput(0, 15);
        for (int i = 0; i < 5; i++) sim.Step();
        Assert.Equal(15, sim.GetOutput(2));
        Assert.Equal(0, sim.GetOutput(3));
    }

    [Fact]
    public void HostDisablesCyclicProgramAndCanLoadHealthyTubeAfterwards()
    {
        var rig = new SignalsRig();
        var a = Wrapper("pending");
        var tube = rig.Tube(a, "Broken");
        // Simulate a corrupted stored reference; normal imprinting does not mutate stored programs.
        a.Components[0].Ref = tube.Attributes.GetString("programId");
        var host = new TubeHost(rig.Api);
        host.Load(tube);
        Assert.True(host.Controls);
        Assert.Equal(TubeProgramError.Cycle, TubeProgram.Error(tube));
        host.Step(_ => 15, _ => false);
        Assert.Equal(0, host.Output(1));
        host.Load(rig.Tube(new CircuitProgram { NodeCount = 1,
            Pins = new() { Pin.Output(0, 0) },
            Components = new() { new Component(ComponentKind.Source, 15, 0) } }, "Healthy"));
        host.Step(_ => 0, _ => false);
        Assert.True(host.Controls);
        Assert.Equal(15, host.Output(0));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ExpandedSizeCountsNodesLinksAndComponentsAndAcceptsExactLimit(int resource)
    {
        var p = new CircuitProgram();
        void Add()
        {
            if (resource == 0) p.NodeCount++;
            else if (resource == 1) p.Links.Add(new Link(0, 0, 0, 0));
            else p.Components.Add(new Component(ComponentKind.Source, 0, 0));
        }
        if (resource != 0) p.NodeCount = 1;
        for (int i = 1 + p.NodeCount; i < CircuitLimits.MaxExpandedSize; i++) Add();
        _ = new CircuitSimulator(p);
        Add();
        Assert.Equal(TubeProgramError.ExpandedSize,
            Assert.Throws<CircuitProgramException>(() => new CircuitSimulator(p)).Error);
    }

    [Fact]
    public void DepthIncludesRootAndAcceptsExactLimit()
    {
        var leaf = new CircuitProgram { NodeCount = 2, Pins = new() { Pin.Input(0, 0), Pin.Output(1, 1) } };
        var programs = new Dictionary<string, CircuitProgram> { ["0"] = leaf };
        CircuitProgram root = leaf;
        for (int i = 1; i < CircuitLimits.MaxDepth; i++)
            programs[i.ToString()] = root = Wrapper((i - 1).ToString());
        var sim = new CircuitSimulator(root, id => programs[id]);
        sim.Step();
        Assert.True(sim.LoadState(sim.SaveState()));
        root = Wrapper((CircuitLimits.MaxDepth - 1).ToString());
        Assert.Equal(TubeProgramError.Depth,
            Assert.Throws<CircuitProgramException>(() => new CircuitSimulator(root, id => programs[id])).Error);
    }

    [Fact]
    public void ExpandedSizeCountsEveryCopyAndBudgetResetsForNextLoad()
    {
        var leaf = new CircuitProgram { NodeCount = CircuitLimits.MaxExpandedSize / 2,
            Pins = new() { Pin.Input(0, 0), Pin.Output(1, 1) } };
        var root = Wrapper("leaf");
        _ = new CircuitSimulator(root, _ => leaf);
        root.Components.Add(new Component(ComponentKind.Tube, 0, 0, 1) { Ref = "leaf" });
        Assert.Equal(TubeProgramError.ExpandedSize,
            Assert.Throws<CircuitProgramException>(() => new CircuitSimulator(root, _ => leaf)).Error);
        root.Components.RemoveAt(1);
        _ = new CircuitSimulator(root, _ => leaf);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HostShowsLimitReasonAndClearsItAfterProgramIsFixed(bool depth)
    {
        var rig = new SignalsRig();
        var p = new CircuitProgram { NodeCount = 2, Pins = new() { Pin.Input(0, 0), Pin.Output(1, 1) } };
        var tube = rig.Tube(p, "Limit test");
        if (depth)
        {
            for (int i = 1; i <= CircuitLimits.MaxDepth; i++)
                tube = rig.Tube(p = Wrapper(tube.Attributes.GetString("programId")), "Nested");
        }
        else p.NodeCount = CircuitLimits.MaxExpandedSize;
        var host = new TubeHost(rig.Api);
        host.Load(tube);
        Assert.Equal(depth ? TubeProgramError.Depth : TubeProgramError.ExpandedSize, TubeProgram.Error(tube));
        Assert.True(host.Controls);
        host.Step(_ => 15, _ => false);
        Assert.Equal(0, host.Output(1));
        p.Components.Clear();
        p.NodeCount = 2;
        host.Load(tube);
        Assert.False(TubeProgram.HasError(tube));
    }
}
