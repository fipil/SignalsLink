using SignalsTubes.src.programtube;
using SignalsTubes.src.circuit;
using Vintagestory.API.Common;

namespace SignalsTubes.Tests;

public class PinCacheTests
{
    private static ItemStack Stack(string name, int index = 0)
    {
        var stack = new ItemStack(new ItemProgramTube());
        stack.Attributes.SetString("pins", $"[{{\"i\":{index},\"r\":\"in\",\"name\":\"{name}\"}}]");
        return stack;
    }

    [Fact]
    public void CopiesShareImmutablePinsAndAttributeChangesUseNewEntry()
    {
        var a = Stack("lang:signalstubes:test");
        var b = Stack("lang:signalstubes:test");
        var before = TubeProgram.Pins(a);
        Assert.Same(before, TubeProgram.Pins(b));
        Assert.Throws<NotSupportedException>(() => ((IList<PublicPin>)before).Clear());
        a.Attributes.SetString("pins", Stack("renamed", 2).Attributes.GetString("pins"));
        Assert.Equal("renamed", Assert.Single(TubeProgram.Pins(a)).Name);
        Assert.Equal(4, TubeProgram.PinMask(a));
        Assert.Equal("lang:signalstubes:test", Assert.Single(TubeProgram.Pins(b)).Name);
        TubeProgram.Clear(a);
        Assert.Empty(TubeProgram.Pins(a));
    }

    [Fact]
    public void EvictionAndConcurrentReadsPreserveResults()
    {
        var original = Stack("eviction-original");
        var before = TubeProgram.Pins(original);
        Parallel.For(0, 1024, i => Assert.Equal(i % 8, Assert.Single(TubeProgram.Pins(Stack("parallel-" + i, i % 8))).Index));
        var after = TubeProgram.Pins(original);
        Assert.NotSame(before, after);
        Assert.Equal(before.ToArray(), after.ToArray());
    }

    [Fact]
    public void MalformedPinsCanBeReplacedWithValidData()
    {
        var stack = Stack("healthy");
        stack.Attributes.SetString("pins", "not JSON");
        Assert.Empty(TubeProgram.Pins(stack));
        stack.Attributes.SetString("pins", Stack("fixed").Attributes.GetString("pins"));
        Assert.Equal("fixed", Assert.Single(TubeProgram.Pins(stack)).Name);
    }
}
