using System.Numerics;
using SignalsLink.src.signals.clock;
using Xunit;

namespace SignalsLink.Tests;

/// <summary>The lights glide on a normal step, settle at once after a jump, and keep up with a fast clock.</summary>
public class ClockLightsTests
{
    private static readonly Vector3 Center = new(.5f, .5f, .5f);
    private const float Edge = 1;
    private const double SlowSteps = 700;   // ms per step at the default day length

    private static Vector3 At(char corner) => ClockFigures.Position(corner, Center, Edge);

    [Fact]
    public void The_first_update_settles_without_a_glide()
    {
        var lights = new ClockLights();
        lights.Update(0x37F, 1000, SlowSteps);
        Assert.False(lights.Moving(1000));
        Assert.Equal("GDAFH", lights.Figure(0));
        Assert.Equal("ADFC", lights.Figure(1));
        Assert.Equal("HAF", lights.Figure(2));
        Assert.Equal(At('H'), lights.Point(2, 0, 1000, Center, Edge));
    }

    [Fact]
    public void The_next_step_glides_only_the_groups_whose_digit_changed()
    {
        var lights = new ClockLights();
        lights.Update(0x37F, 0, SlowSteps);
        lights.Update(0x380, 1000, SlowSteps);
        Assert.True(lights.Moving(1000));
        // phase stays, turn ADFC -> FHGA, beat HAF -> FBC
        Assert.Equal(At('G'), lights.Point(0, 0, 1100, Center, Edge));
        Assert.Equal(Vector3.Lerp(At('A'), At('F'), .5f), lights.Point(1, 0, 1100, Center, Edge));
        Assert.Equal(Vector3.Lerp(At('H'), At('F'), .5f), lights.Point(2, 0, 1100, Center, Edge));
        Assert.Equal(At('F'), lights.Point(2, 0, 1200, Center, Edge));
        Assert.False(lights.Moving(1200));
    }

    [Fact]
    public void Midnight_moves_all_twelve_points()
    {
        var lights = new ClockLights();
        lights.Update(0xFFF, 0, SlowSteps);
        lights.Update(0x000, 1000, SlowSteps);
        for (int g = 0; g < 3; g++)
            for (int k = 0; k < ClockFigures.PointCount(g); k++)
            {
                var start = At(ClockFigures.Figure(g, 15)[k]);
                var mid = lights.Point(g, k, 1100, Center, Edge);
                Assert.NotEqual(start, mid);
            }
    }

    [Fact]
    public void A_jump_in_time_settles_the_lights_at_once()
    {
        var lights = new ClockLights();
        lights.Update(0x100, 0, SlowSteps);
        lights.Update(0x105, 1000, SlowSteps);   // five steps at once: a skipped night or a late client
        Assert.False(lights.Moving(1000));
        Assert.Equal(At(ClockFigures.Figure(2, 5)[0]), lights.Point(2, 0, 1000, Center, Edge));
    }

    [Fact]
    public void A_fast_clock_shortens_the_glide_to_the_time_until_the_next_step()
    {
        var lights = new ClockLights();
        lights.Update(0x000, 0, 50);
        lights.Update(0x001, 1000, 50);
        Assert.True(lights.Moving(1040));
        Assert.False(lights.Moving(1050));
        Assert.Equal(At('C'), lights.Point(2, 0, 1050, Center, Edge));
    }

    [Fact]
    public void A_stopped_clock_still_glides_at_the_normal_pace()
    {
        var lights = new ClockLights();
        lights.Update(0, 0, double.PositiveInfinity);
        lights.Update(1, 1000, double.PositiveInfinity);
        Assert.True(lights.Moving(1199));
        Assert.False(lights.Moving(1200));
    }

    [Fact]
    public void Points_are_in_their_corner_before_and_after_a_glide()
    {
        var lights = new ClockLights();
        lights.Update(7, 0, SlowSteps);
        lights.Update(8, 1000, SlowSteps);
        Assert.Equal(At('F'), lights.Point(2, 0, 999, Center, Edge));      // before the start: FCH -> HDA has not begun
        Assert.Equal(At('F'), lights.Point(2, 0, 1000, Center, Edge));
        Assert.Equal(At('H'), lights.Point(2, 0, 5000, Center, Edge));
    }
}
