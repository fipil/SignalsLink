using SignalsTubes.src.circuit;
using SignalsTubes.src.copier;
using SignalsTubes.src.programtube;
using Vintagestory.API.Common;

namespace SignalsTubes.Tests;

public class CopierTests
{
    private static readonly ItemProgramTube Tube = new() { Code = new AssetLocation("signalstubes:programtube-fire"), MaxStackSize = 1 };

    private static ItemStack Programmed(string author, bool lockCopy)
    {
        var stack = new ItemStack(Tube);
        var program = new CircuitProgram { NodeCount = 1, Components = { new Component(ComponentKind.Source, 15, 0) }, Pins = { Pin.Output(0, 0) } };
        TubeProgram.Set(stack, program, new ProgramStore(), author, author, lockCopy, false);
        return stack;
    }

    [Fact]
    public void SocketSlotsAcceptOnlyWhatTheyAreFor()
    {
        var be = new BETubeCopier();
        var blank = new DummySlot(new ItemStack(Tube));
        var programmed = new DummySlot(Programmed("a", false));
        var other = new DummySlot(new ItemStack(new Item { Code = new AssetLocation("game:stick") }));
        Assert.True(be.Inventory[BETubeCopier.In].CanHold(programmed));
        Assert.False(be.Inventory[BETubeCopier.In].CanHold(blank));
        Assert.False(be.Inventory[BETubeCopier.In].CanHold(other));
        Assert.True(be.Inventory[BETubeCopier.Out].CanHold(blank));
        Assert.False(be.Inventory[BETubeCopier.Out].CanHold(programmed));
        // automation pulls the original whenever no copy runs; the red socket only once the copy is written
        be.Inventory[BETubeCopier.In].Itemstack = programmed.Itemstack;
        Assert.True(be.Inventory[BETubeCopier.In].CanTake());
        be.Inventory[BETubeCopier.Out].Itemstack = new ItemStack(Tube);
        Assert.False(be.Inventory[BETubeCopier.Out].CanTake());
        be.Inventory[BETubeCopier.Out].Itemstack = Programmed("a", false);
        Assert.True(be.Inventory[BETubeCopier.Out].CanTake());
    }

    [Fact]
    public void CheckReportsWhyACopyCannotStart()
    {
        var be = new BETubeCopier();
        Assert.Equal(BETubeCopier.StateNoInput, be.Check("me"));
        be.Inventory[BETubeCopier.In].Itemstack = Programmed("author", lockCopy: true);
        Assert.Equal(BETubeCopier.StateNoBlank, be.Check("me"));
        be.Inventory[BETubeCopier.Out].Itemstack = new ItemStack(Tube);
        Assert.Equal(BETubeCopier.StateLocked, be.Check("me"));          // locked, I am not the author
        Assert.Equal(BETubeCopier.StateNoCharge, be.Check("author"));    // the author may, but there is no charge
        be.Inventory[BETubeCopier.In].Itemstack = Programmed("author", lockCopy: false);
        Assert.Equal(BETubeCopier.StateNoCharge, be.Check("me"));
    }
}
