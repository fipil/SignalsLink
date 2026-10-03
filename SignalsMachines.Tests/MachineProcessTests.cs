using SignalsMachines.src.craftingmachine;

namespace SignalsMachines.Tests;

/// <summary>The crafting sequence, stepped with scripted senses.</summary>
public class MachineProcessTests
{
    private static Sense S(byte clutch = 0, byte crystal = 0, byte strength = 0, bool doors = true, int cells = 4, bool recipe = true,
        bool productEmpty = true, bool network = true, bool plate = true) =>
        new(new MachineInputs(clutch, crystal, strength, (byte)(doors ? 0 : 15), 0), doors, cells, recipe, productEmpty, network, plate);

    private static MachineProcess.Event Run(MachineProcess p, Sense s, float seconds)
    {
        var last = MachineProcess.Event.None;
        for (float t = 0; t < seconds - 1e-4f; t += 0.1f) last = p.Step(s, 0.1f);
        return last;
    }

    [Fact]
    public void TheFullSequenceCraftsAfterThreePlusOnePerCellSeconds()
    {
        var p = new MachineProcess();
        Assert.Equal(MachineProcess.Event.None, Run(p, S(), 0.1f));
        Assert.Equal(MachineProcess.Ready, p.State);
        Run(p, S(clutch: 15), 0.1f);
        Assert.Equal(MachineProcess.Preparing, p.State);
        // strength raised gently to 4 with the crystal down
        Run(p, S(15, 15, 2), 0.1f); Run(p, S(15, 15, 4), 0.1f);
        Assert.Equal(MachineProcess.Crafting, p.State);
        Assert.Equal(MachineProcess.Event.None, Run(p, S(15, 15, 4), 6.8f));   // 7 s = 3 + 4 cells
        Assert.Equal(MachineProcess.Event.Craft, Run(p, S(15, 15, 4), 0.2f));
        Assert.Equal(MachineProcess.Done, p.State);
        // the batch is consumed, strength is still 4 over an emptied plate: no overload while the product waits
        Run(p, S(15, 15, 4, cells: 0, recipe: false, productEmpty: false), 1f);
        Assert.Equal(MachineProcess.Done, p.State);
        Run(p, S(15, 15, 4, productEmpty: false), 0.1f);
        Assert.Equal(MachineProcess.OutputFull, p.State);   // the product waits and the plate already shows a new recipe
        Run(p, S(15, 15, 4, cells: 0, recipe: false), 1f);   // product taken, plate empty: still nothing to burn
        Assert.Equal(MachineProcess.Waiting, p.State);
        Assert.Equal(MachineProcess.Event.Overload, p.Step(S(15, 15, 4, cells: 2), 0.1f));   // a smaller batch under too much strength
    }

    [Fact]
    public void ASteepRiseOrTooMuchStrengthOverloads()
    {
        var p = new MachineProcess();
        Run(p, S(15, 15, 1), 0.1f);
        Assert.Equal(MachineProcess.Event.Overload, p.Step(S(15, 15, 4), 0.1f));   // +3 in one step
        Assert.Equal(MachineProcess.Overloaded, p.State);

        p = new MachineProcess();
        Run(p, S(15, 15, 4), 0.5f); Run(p, S(15, 15, 5), 0.1f);
        Assert.Equal(MachineProcess.Event.Overload, p.Step(S(15, 15, 6), 0.1f));   // 6 > cells + 1 with the crystal down
        Assert.Equal(MachineProcess.Overloaded, p.State);
        // held until strength 0 and crystal up
        Run(p, S(15, 15, 0), 1f);
        Assert.Equal(MachineProcess.Overloaded, p.State);
        Run(p, S(15, 0, 0), 0.1f);
        Assert.Equal(MachineProcess.Overloaded, p.State);   // clutch still closed
        Run(p, S(0, 0, 0), 0.1f);
        Assert.Equal(MachineProcess.Waiting, p.State);
    }

    [Fact]
    public void OpeningADoorWhileCraftingCancelsTheRun()
    {
        var p = new MachineProcess();
        Run(p, S(15, 15, 4), 2f);
        Assert.Equal(MachineProcess.Crafting, p.State);
        Run(p, S(15, 15, 4, doors: false), 0.1f);
        Assert.Equal(MachineProcess.DoorOpened, p.State);
        Run(p, S(15, 15, 4), 1f);
        Assert.Equal(MachineProcess.DoorOpened, p.State);   // latched
        Run(p, S(0, 0, 0), 0.1f);
        Assert.Equal(MachineProcess.Waiting, p.State);
        Assert.Equal(0, p.Progress);
    }

    [Fact]
    public void AShortInterruptionPausesALongOneResets()
    {
        var p = new MachineProcess();
        Run(p, S(15, 15, 4), 3f);
        float progress = p.Progress;
        Run(p, S(0, 15, 4, plate: false), 1.2f);   // clutch dropped and the plate stopped under a lowered crystal
        Assert.Equal(MachineProcess.PlateStill, p.State);
        p = new MachineProcess();
        Run(p, S(15, 15, 4), 3f);
        progress = p.Progress;
        Run(p, S(15, 15, 4, plate: false), 0.5f);   // plate briefly too slow: paused
        Assert.Equal(progress, p.Progress, 3);
        Run(p, S(15, 15, 4), 0.1f);
        Assert.Equal(MachineProcess.Crafting, p.State);
        p = new MachineProcess();
        Run(p, S(15, 15, 4), 3f);
        Run(p, S(15, 0, 4), 5.2f);   // crystal lifted for more than 5 s: the run is forgotten
        Assert.Equal(0, p.Progress);
        Assert.Equal(MachineProcess.Preparing, p.State);
    }

    [Fact]
    public void WrongStrengthAndMissingDriveAreErrorsAfterTheirGrace()
    {
        var p = new MachineProcess();
        Run(p, S(15, 15, 3), 10f);   // below the target for as long as it takes: the operator is still ramping up
        Assert.Equal(MachineProcess.Preparing, p.State);
        Run(p, S(15, 15, 5), 2.2f);   // one above the target for over 2 s
        Assert.Equal(MachineProcess.WrongStrength, p.State);
        p = new MachineProcess();
        Run(p, S(clutch: 15, network: false, plate: false), 3.2f);
        Assert.Equal(MachineProcess.NoDrive, p.State);
        Run(p, S(clutch: 15, network: true), 1f);
        Assert.Equal(MachineProcess.NoDrive, p.State);   // the wind came back, the clutch must still be opened once
        Run(p, S(), 0.2f);   // one step clears the error, the next reads the plate again
        Assert.Equal(MachineProcess.Ready, p.State);
    }

    [Fact]
    public void WithoutARecipeOrWithDoorsOpenItJustWaits()
    {
        var p = new MachineProcess();
        Run(p, S(recipe: false), 0.1f);
        Assert.Equal(MachineProcess.Waiting, p.State);
        Run(p, S(doors: false), 0.1f);
        Assert.Equal(MachineProcess.Waiting, p.State);
    }
}
