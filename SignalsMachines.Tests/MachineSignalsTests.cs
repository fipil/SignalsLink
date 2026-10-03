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
        var block = new BlockCraftingMachine();
        block.VariantStrict["side"] = "north";
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

    // The imprinter reads the circuit hanging on the machine's own pins: what drives the machine comes
    // out as output pins, the state pin the circuit listens to as an input pin.
    [Fact]
    public void ImprintingFromTheMachineSocketGetsTheRolesRight()
    {
        var rig = new SignalsRig();
        Machine(rig, At(0));
        var source = rig.SourceBlock(At(1), 9);
        var valve = rig.Valve(At(2));
        rig.Wire(source, 0, At(0), 1);        // clutch straight from the source
        rig.Wire(At(0), 0, valve, 0);         // the valve is controlled by the machine's state
        rig.Wire(source, 0, valve, 1);
        rig.Wire(valve, 2, At(0), 3);         // strength through the valve
        var read = SignalsTubes.src.imprint.CircuitReader.Read(new SignalsTubes.src.imprint.SignalsCircuitWorld(rig.Api), At(0));
        Assert.True(read.Ok);
        var roles = read.Program.Pins.OrderBy(p => p.Index).Select(p => (p.Index, p.Role)).ToList();
        Assert.Equal(new[] { (0, PinRole.Input), (1, PinRole.Output), (3, PinRole.Output) }, roles);

        // the tube written from it drives a machine the same way the wires did
        var rig2 = new SignalsRig();
        var machine = Machine(rig2, At(0), rig2.Tube(read.Program, "auto"));
        for (int i = 0; i < 4; i++) rig2.Tick();
        Assert.True(machine.TubeControls);
        Assert.Equal(9, machine.Inputs.Clutch);
    }

    [Fact]
    public void AutomationComesInOnlyThroughAnOpenDoor()
    {
        var rig = new SignalsRig();
        var machine = Machine(rig, At(0));
        var west = rig.SourceBlock(At(1));
        rig.Wire(west, 0, At(0), 4);   // pin 4 = west door of a north-facing machine
        rig.ElapsedMilliseconds = 10_000;
        rig.Tick(); rig.Tick();
        var chamber = At(0).UpCopy();
        Assert.False(machine.AllowsAutomation(chamber, BlockFacing.WEST));   // still riding down
        rig.ElapsedMilliseconds += (long)((DoorMotion.PushSeconds + DoorMotion.OpenSlideSeconds) * 1000) + 1;
        Assert.True(machine.AllowsAutomation(chamber, BlockFacing.WEST));
        Assert.False(machine.AllowsAutomation(chamber, BlockFacing.EAST));
        Assert.False(machine.AllowsAutomation(chamber, BlockFacing.NORTH));
        Assert.False(machine.AllowsAutomation(chamber, BlockFacing.UP));
        Assert.False(machine.AllowsAutomation(At(0), BlockFacing.WEST));   // the pedestal has no door
        var clutch = rig.SourceBlock(At(2));
        rig.Wire(clutch, 0, At(0), 1);
        rig.Tick(); rig.Tick();
        Assert.False(machine.AllowsAutomation(chamber, BlockFacing.WEST));   // clutch closed: the plate may turn
        rig.Drive(clutch, 0, 0);
        rig.Tick(); rig.Tick();
        Assert.True(machine.AllowsAutomation(chamber, BlockFacing.WEST));
        rig.Drive(west, 0, 0);
        rig.Tick(); rig.Tick();
        Assert.False(machine.AllowsAutomation(chamber, BlockFacing.WEST));
    }
}
