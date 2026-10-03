using signals.src.signalNetwork;
using SignalsMachines.src.craftingmachine;
using SignalsTubes.src.circuit;
using SignalsTubes.Tests.fidelity;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using static SignalsTubes.Tests.fidelity.SignalsRig;

namespace SignalsMachines.Tests;

/// <summary>The machine on the real Signals network: pins as inputs, a tube taking over.</summary>
public class MachineSignalsTests
{
    private static BECraftingMachine Machine(SignalsRig rig, BlockPos pos, ItemStack tube = null)
    {
        var block = new Block();
        var be = new BECraftingMachine { Block = block };
        if (tube != null)
        {
            var tree = new TreeAttribute();
            tree.SetInt("posx", pos.X); tree.SetInt("posy", pos.Y); tree.SetInt("posz", pos.Z);
            tree.SetItemstack("programTube", tube);
            be.FromTreeAttributes(tree, rig.World);
        }
        rig.Place(pos, block, "signalsmachines:craftingmachine-north", be, e => new BEBehaviorSignalConnector(e), SourceNodes(BECraftingMachine.PinCount));
        return be;
    }

    // strength = NOT clutch: pin 1 in, pin 3 out
    private static CircuitProgram Inverter() => ProgramCodec.FromJson(
        "{\"v\":1,\"n\":3,\"links\":[],\"parts\":[{\"k\":\"source\",\"n\":[1],\"p\":15},{\"k\":\"valve\",\"n\":[0,1,2]}],\"pins\":[{\"i\":1,\"r\":\"in\",\"n\":0},{\"i\":3,\"r\":\"out\",\"n\":2}]}");

    [Fact]
    public void WiresDriveTheInputs()
    {
        var rig = new SignalsRig();
        var machine = Machine(rig, At(0));
        var clutch = rig.SourceBlock(At(1));
        var strength = rig.SourceBlock(At(2), 4);
        rig.Wire(clutch, 0, At(0), 1);
        rig.Wire(strength, 0, At(0), 3);
        rig.Tick(); rig.Tick();   // the machine reads the levels the previous step produced, like a socket
        Assert.Equal(new MachineInputs(15, 0, 4, 0, 0), machine.Inputs);
        Assert.False(machine.TubeControls);
        rig.Drive(clutch, 0, 0);
        rig.Tick(); rig.Tick();
        Assert.Equal(new MachineInputs(0, 0, 4, 0, 0), machine.Inputs);
    }

    [Fact]
    public void TubeTakesOverAndWiresOnInputsAreIgnored()
    {
        var rig = new SignalsRig();
        var machine = Machine(rig, At(0), rig.Tube(Inverter(), "NOT"));
        var clutch = rig.SourceBlock(At(1));
        var crystal = rig.SourceBlock(At(2));
        rig.Wire(clutch, 0, At(0), 1);
        rig.Wire(crystal, 0, At(0), 2);
        Assert.True(machine.TubeControls);
        for (int i = 0; i < 3; i++) rig.Tick();
        // wires on machine inputs are cut off entirely: the tube sees clutch 0, so strength = NOT 0 = 15
        Assert.Equal(new MachineInputs(0, 0, 15, 0, 0), machine.Inputs);
        rig.Drive(clutch, 0, 0);
        for (int i = 0; i < 3; i++) rig.Tick();
        Assert.Equal(new MachineInputs(0, 0, 15, 0, 0), machine.Inputs);
    }

    [Fact]
    public void InputsSurviveSaveAndLoadForTheClient()
    {
        var rig = new SignalsRig();
        var machine = Machine(rig, At(0));
        rig.Wire(rig.SourceBlock(At(1), 9), 0, At(0), 5);
        rig.Tick(); rig.Tick();
        var tree = new TreeAttribute();
        machine.ToTreeAttributes(tree);
        var copy = new BECraftingMachine { Block = new Block() };
        copy.FromTreeAttributes(tree, rig.World);
        Assert.Equal(new MachineInputs(0, 0, 0, 0, 9), copy.Inputs);
    }

    // Fipil's first in-game tube: source -> cathode, grid = pin 2, anode -> resistor 14 -> pin 4 (level 1)
    [Fact]
    public void WeakInverterTubeDrivesStrengthOne()
    {
        var program = ProgramCodec.FromJson("{\"v\":1,\"n\":4,\"links\":[[2,3,14,14]],\"parts\":[{\"k\":\"source\",\"n\":[1],\"p\":15},{\"k\":\"valve\",\"n\":[0,1,2]}],\"pins\":[{\"i\":1,\"r\":\"in\",\"n\":0},{\"i\":3,\"r\":\"out\",\"n\":3}]}");
        var rig = new SignalsRig();
        var machine = Machine(rig, At(0), rig.Tube(program, "NOT"));
        for (int i = 0; i < 3; i++) rig.Tick();
        Assert.Equal(new MachineInputs(0, 0, 1, 0, 0), machine.Inputs);
        var clutch = rig.SourceBlock(At(1));
        rig.Wire(clutch, 0, At(0), 1);   // a wire on a machine input changes nothing while the tube rules
        for (int i = 0; i < 3; i++) rig.Tick();
        Assert.Equal(new MachineInputs(0, 0, 1, 0, 0), machine.Inputs);
    }

    // a tube may take its own signals through the reserve pins: pin 7 in -> pin 3 (crystal) out
    [Fact]
    public void ReservePinsStillReachTheTube()
    {
        var program = ProgramCodec.FromJson("{\"v\":1,\"n\":2,\"links\":[[0,1,0,0]],\"parts\":[],\"pins\":[{\"i\":6,\"r\":\"in\",\"n\":0},{\"i\":2,\"r\":\"out\",\"n\":1}]}");
        var rig = new SignalsRig();
        var machine = Machine(rig, At(0), rig.Tube(program, "pass"));
        rig.Wire(rig.SourceBlock(At(1), 9), 0, At(0), 6);
        for (int i = 0; i < 3; i++) rig.Tick();
        Assert.Equal(new MachineInputs(0, 9, 0, 0, 0), machine.Inputs);
    }

    [Theory]
    [InlineData("north", "west", "east")]
    [InlineData("south", "east", "west")]
    [InlineData("east", "north", "south")]
    [InlineData("west", "south", "north")]
    public void DoorPinsAreNamedByTheWorldSideTheyFace(string variant, string pin4, string pin5)
    {
        var block = new BlockCraftingMachine();
        block.VariantStrict["side"] = variant;
        Assert.Equal(pin4, block.DoorSide(4).Code);
        Assert.Equal(pin5, block.DoorSide(5).Code);
    }
}
