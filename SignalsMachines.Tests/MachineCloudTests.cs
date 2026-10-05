using SignalsMachines.src.craftingmachine;
using Vintagestory.API.MathTools;

namespace SignalsMachines.Tests;

/// <summary>The leak cloud behaves like the game's liquids: falls first, seeks a drain, else spreads evenly; walls stop it; gone a minute after the source.</summary>
public class MachineCloudTests
{
    private static BlockPos P(int x, int y = 0, int z = 0) => new(x, y, z, 0);
    private static readonly (BlockPos, float)[] Source = { (P(0), 1f) };
    private static readonly (BlockPos, float)[] None = Array.Empty<(BlockPos, float)>();
    private static bool Ground(BlockPos p) => p.Y >= 0;   // solid floor right under the source level

    private static MachineCloud Run(float seconds, System.Func<BlockPos, bool> passable = null, (BlockPos, float)[] sources = null)
    {
        var cloud = new MachineCloud();
        for (float t = 0; t < seconds - 1e-4f; t += 0.5f) cloud.Step(0.5f, sources ?? Source, passable ?? Ground);
        return cloud;
    }

    [Fact]
    public void OverFlatGroundItSpreadsEvenlyABlockPerDelayAndWeakensPerStep()
    {
        var c = Run(1f);
        Assert.Equal(1f, c.At(0.5, 0.5, 0.5));
        Assert.Equal(0, c.At(1.5, 0.5, 0.5));        // a sideways step takes 1.5 s
        c = Run(1.5f);
        foreach (var (x, z) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            Assert.Equal(1 - 1f / MachineCloud.MaxSteps, c.At(x + .5, 0.5, z + .5), 3);   // all four sides alike
        Assert.Equal(0, c.At(2.5, 0.5, 0.5));
        c = Run(3f);
        Assert.Equal(1 - 2f / MachineCloud.MaxSteps, c.At(2.5, 0.5, 0.5), 3);
        c = Run(30f);
        Assert.True(c.At(7.5, 0.5, 0.5) > 0);        // seven steps out
        Assert.Equal(0, c.At(8.5, 0.5, 0.5));
        Assert.Equal(0, c.At(0.5, 1.5, 0.5));        // never upwards
    }

    [Fact]
    public void FallsFirstThenSpreadsOnTheFloorAndWallsStopIt()
    {
        bool Passable(BlockPos p) => p.X < 2 && p.Y >= -2;    // a wall at x = 2, the floor two below the door
        var c = Run(0.5f, Passable);
        Assert.Equal(1f, c.At(0.5, 0.5, 0.5));
        Assert.True(c.At(0.5, -0.5, 0.5) > 0);             // drops at once
        Assert.Equal(0, c.At(1.5, 0.5, 0.5));              // nothing sideways while falling
        c = Run(1f, Passable);
        Assert.True(c.At(0.5, -1.5, 0.5) > 0);             // column down to the floor
        Assert.Equal(0, c.At(1.5, 0.5, 0.5));
        Assert.Equal(0, c.At(1.5, -1.5, 0.5));
        c = Run(3f, Passable);
        Assert.True(c.At(1.5, -1.5, 0.5) > 0);             // spreads along the floor
        Assert.Equal(1f, c.At(0.5, -1.5, 0.5));            // falling cost nothing
        Assert.Equal(0, c.At(1.5, 0.5, 0.5));              // the upper blocks only fall, they do not spread
        c = Run(30f, Passable);
        Assert.Equal(0, c.At(2.5, -1.5, 0.5));             // the wall holds
        Assert.Equal(0, c.At(0.5, -2.5, 0.5));             // the floor holds
    }

    [Fact]
    public void SeeksADrainAndFlowsOnlyTowardsIt()
    {
        // flat floor, but a hole three blocks east of the source: the cloud goes east only
        bool Passable(BlockPos p) => p.Y >= 0 || (p.X == 3 && p.Z == 0 && p.Y >= -3);
        var c = Run(6f, Passable);
        Assert.True(c.At(1.5, 0.5, 0.5) > 0);
        Assert.True(c.At(2.5, 0.5, 0.5) > 0);
        Assert.Equal(0, c.At(-0.5, 0.5, 0.5));
        Assert.Equal(0, c.At(0.5, 0.5, 1.5));
        Assert.True(c.At(3.5, -2.5, 0.5) > 0);             // and pours down the hole
        Assert.Equal(new Vec3i(1, 0, 0), c.Cells[P(1)].Flow);   // the slope points east
        Assert.Equal(-1, c.Cells[P(3)].Flow.Y);                  // and falls over the hole
    }

    [Fact]
    public void FadesOutAMinuteAfterTheSourceStops()
    {
        var cloud = Run(4f);
        float before = cloud.At(1.5, 0.5, 0.5);
        Assert.True(before > 0);
        for (int i = 0; i < 60; i++) cloud.Step(0.5f, None, Ground);   // 30 s
        Assert.Equal(before / 2, cloud.At(1.5, 0.5, 0.5), 2);
        for (int i = 0; i < 62; i++) cloud.Step(0.5f, None, Ground);   // another 31 s
        Assert.True(cloud.Empty);
    }

    // Reopening the door into the remains of the last leak: the front has to flow out again, the fading cells
    // are not lit up at once.
    [Fact]
    public void AFadingCloudIsRefilledOnlyAsTheFrontReachesItAgain()
    {
        var cloud = Run(6f);
        for (int i = 0; i < 20; i++) cloud.Step(0.5f, None, Ground);   // 10 s of fading
        float fadedFar = cloud.At(3.5, 0.5, 0.5);
        Assert.True(fadedFar > 0 && fadedFar < 1 - 3f / MachineCloud.MaxSteps);
        cloud.Step(0.5f, Source, Ground);
        Assert.Equal(1f, cloud.At(0.5, 0.5, 0.5));                      // the source is back
        Assert.True(cloud.At(3.5, 0.5, 0.5) < fadedFar);                // three blocks out it is still fading
        for (int i = 0; i < 12; i++) cloud.Step(0.5f, Source, Ground);  // 6 s: the front gets there again
        Assert.Equal(1 - 3f / MachineCloud.MaxSteps, cloud.At(3.5, 0.5, 0.5), 3);
    }

    [Fact]
    public void SyncRoundTripKeepsPositionsIntensitiesAndFlow()
    {
        var cloud = Run(3f);
        var origin = P(10, 5, -3);
        var copy = new MachineCloud();
        copy.Deserialize(cloud.Serialize(origin), origin);
        Assert.Equal(cloud.Cells.Count, copy.Cells.Count);
        Assert.Equal(cloud.At(1.5, 0.5, 0.5), copy.At(1.5, 0.5, 0.5), 1);   // a byte per intensity
        Assert.Equal(cloud.Cells[P(1)].Flow, copy.Cells[P(1)].Flow);
    }
}
