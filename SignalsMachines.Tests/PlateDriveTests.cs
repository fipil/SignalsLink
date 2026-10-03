using SignalsMachines.src.craftingmachine;
using SignalsTubes.Tests.fidelity;
using static SignalsTubes.Tests.fidelity.SignalsRig;

namespace SignalsMachines.Tests;

public class PlateDriveTests
{
    [Fact]
    public void PlateApproachesTargetWithTheTimeConstant()
    {
        float speed = 0;
        for (int i = 0; i < 20; i++) speed = PlateDrive.Step(speed, 1f, 0.1f);   // 2 s = one time constant
        Assert.InRange(speed, 0.62f, 0.64f);
        for (int i = 0; i < 100; i++) speed = PlateDrive.Step(speed, 0f, 0.1f);
        Assert.InRange(speed, 0, 0.01f);
        for (int i = 0; i < 200; i++) speed = PlateDrive.Step(speed, 0f, 0.1f);
        Assert.Equal(0, speed);   // settles to exactly standing
    }

    [Fact]
    public void RunningNeedsAThirdOfTheNetworkSpeed()
    {
        Assert.True(PlateDrive.Running(0.3f, 1f));
        Assert.False(PlateDrive.Running(0.29f, 1f));
        Assert.False(PlateDrive.Running(0.5f, 0f));   // no drive at all
    }

    [Fact]
    public void ClutchSpinsThePlateUpAndLetsItCoastDown()
    {
        var rig = new SignalsRig();
        var block = new Vintagestory.API.Common.Block();
        var machine = new BECraftingMachine { Block = block, SpeedSource = () => 0.5f };
        rig.Place(At(0), block, "signalsmachines:craftingmachine-north", machine, e => new signals.src.signalNetwork.BEBehaviorSignalConnector(e), SourceNodes(BECraftingMachine.PinCount));
        var clutch = rig.SourceBlock(At(1));
        rig.Wire(clutch, 0, At(0), 1);
        rig.Tick(); rig.Tick();
        for (int i = 0; i < 20; i++) machine.OnMechanicsTick(0.1f);
        Assert.InRange(machine.PlateSpeed, 0.31f, 0.32f);
        Assert.True(machine.PlateRunning);
        rig.Drive(clutch, 0, 0);
        rig.Tick(); rig.Tick();
        for (int i = 0; i < 10; i++) machine.OnMechanicsTick(0.1f);   // 1 s open: well below running
        Assert.False(machine.PlateRunning);
        Assert.InRange(machine.PlateSpeed, 0.07f, 0.08f);
        for (int i = 0; i < 30; i++) machine.OnMechanicsTick(0.1f);
        Assert.Equal(0, machine.PlateSpeed);
    }

    [Theory]
    [InlineData(0.3f, 5f)]
    [InlineData(6.0f, 25f)]
    [InlineData(0.1f, 0.4f)]
    public void BrakingEndsExactlyAtHomeAfterAtLeastTheMinimumTime(float angle, float w)
    {
        float d = PlateDrive.BrakingDistance(angle, w);
        float stopTime = 2 * d / w;
        Assert.True(stopTime >= PlateDrive.MinStop);
        float end = PlateDrive.Brake(angle, w, d, stopTime + 1);
        float rem = end % MathF.Tau;
        Assert.True(rem < 1e-3f || MathF.Tau - rem < 1e-3f, "ends at " + end);   // a multiple of a full turn (float rounding either side)
        // speed falls continuously: no jump at the start, monotone progress
        float v0 = (PlateDrive.Brake(angle, w, d, 0.001f) - angle) / 0.001f;
        Assert.InRange(v0, w * 0.98f, w * 1.001f);
        float prev = angle;
        for (float t = 0; t <= stopTime; t += stopTime / 50)
        {
            float a = PlateDrive.Brake(angle, w, d, t);
            Assert.True(a >= prev - 1e-5f);
            prev = a;
        }
    }
}
