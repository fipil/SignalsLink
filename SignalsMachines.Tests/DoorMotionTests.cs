using SignalsMachines.src.craftingmachine;

namespace SignalsMachines.Tests;

public class DoorMotionTests
{
    private static void Run(DoorMotion d, float seconds) { for (float t = 0; t < seconds - 1e-4f; t += 0.05f) d.Advance(0.05f); }

    [Fact]
    public void OpensByPushingOutThenDroppingAndClosesTheOtherWayRound()
    {
        var d = new DoorMotion();
        Assert.False(d.Moving);
        d.Set(true);
        Run(d, DoorMotion.PushSeconds);
        Assert.Equal(1, d.Push, 2);
        Assert.Equal(0, d.Drop, 2);
        Run(d, DoorMotion.OpenSlideSeconds);
        Assert.Equal(1, d.Drop, 2);
        Assert.False(d.Moving);
        Assert.Equal((DoorMotion.PushUnits / 16, DoorMotion.DropUnits / 16), d.Offsets);
        d.Set(false);
        Run(d, DoorMotion.CloseSlideSeconds + 0.1f);   // a step of slack for float rounding
        Assert.Equal(0, d.Drop, 2);
        Assert.True(d.Push > 0.75f);   // still (almost entirely) out of the frame while sliding up; the slack began pulling it in
        Run(d, DoorMotion.PushSeconds + 0.1f);
        Assert.Equal(0, d.Push, 2);
        Assert.False(d.Moving);
    }

    [Fact]
    public void ReversingHalfwayTurnsBackFromWhereItIs()
    {
        var d = new DoorMotion();
        d.Set(true);
        Run(d, DoorMotion.PushSeconds + DoorMotion.OpenSlideSeconds / 2);
        float drop = d.Drop;
        d.Set(false);
        Run(d, 0.3f);
        Assert.True(d.Drop < drop && d.Drop > 0);
        Assert.Equal(1, d.Push, 2);
    }
}
