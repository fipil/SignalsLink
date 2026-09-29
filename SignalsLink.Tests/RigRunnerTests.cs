using SignalsLink.Testing;
using Vintagestory.API.MathTools;

namespace SignalsLink.Tests;

public class RigRunnerTests
{
    [Fact]
    public void Setup_never_asserts_before_a_later_tick_and_each_step_enters_once()
    {
        int enters = 0, checks = 0;
        var runner = New(new RigStep("step", () => enters++, () => { checks++; return true; }, () => "ok"));
        runner.Tick(0); Assert.Equal(1, enters); Assert.Equal(0, checks); Assert.Equal(RigStatus.Running, runner.Status);
        runner.Tick(50); Assert.Equal(RigStatus.Passed, runner.Status); Assert.Equal(1, checks);
        runner.Tick(100); Assert.Equal(1, enters); Assert.Equal(1, checks);
    }
    [Fact]
    public void Eventually_waits_for_the_actual_state()
    {
        bool ready = false;
        var runner = New(new RigStep("state", null, () => ready, () => "not ready"));
        runner.Tick(0); runner.Tick(100); Assert.Equal(RigStatus.Running, runner.Status);
        ready = true; runner.Tick(200); Assert.Equal(RigStatus.Passed, runner.Status);
    }
    [Fact]
    public void Deadline_fails_even_if_the_first_late_observation_is_true()
    {
        var runner = New(new RigStep("deadline", null, () => true, () => "actual=9", 100));
        runner.Tick(0); runner.Tick(101);
        Assert.Equal(RigStatus.Failed, runner.Status); Assert.Contains("actual=9", runner.Detail);
    }
    [Fact]
    public void Stable_window_does_not_pass_on_a_brief_success()
    {
        bool state = true;
        var runner = New(new RigStep("hold", null, () => state, () => "changed", 2000, StepMode.Stable, 1000));
        runner.Tick(0); runner.Tick(100); Assert.Equal(RigStatus.Running, runner.Status);
        state = false; runner.Tick(200); Assert.Equal(RigStatus.Failed, runner.Status);
        state = true; runner.Tick(1500); Assert.Equal(RigStatus.Failed, runner.Status);
    }
    [Fact]
    public void Stable_window_requires_the_whole_duration()
    {
        var runner = New(new RigStep("hold", null, () => true, () => "ok", 2000, StepMode.Stable, 1000));
        runner.Tick(0); runner.Tick(999); Assert.Equal(RigStatus.Running, runner.Status);
        runner.Tick(1000); Assert.Equal(RigStatus.Passed, runner.Status);
    }
    [Theory]
    [InlineData(RigStatus.Cancelled)] [InlineData(RigStatus.InvalidEnvironment)]
    public void Aborting_prevents_later_mutations(RigStatus status)
    {
        int edits = 0;
        var runner = New(new RigStep("setup", () => edits++, () => true, () => "ok"));
        runner.Stop(status, "stop"); runner.Tick(0);
        Assert.Equal(0, edits); Assert.Equal(status, runner.Status);
    }
    [Fact]
    public void Exceptions_are_errors_and_do_not_run_the_next_step()
    {
        int edits = 0;
        var runner = New(new RigStep("bad setup", () => throw new InvalidOperationException("fixture missing"), () => true, () => ""),
            new RigStep("next", () => edits++, () => true, () => ""));
        runner.Tick(0); runner.Tick(100);
        Assert.Equal(RigStatus.Error, runner.Status); Assert.Contains("fixture missing", runner.Detail); Assert.Equal(0, edits);
    }
    [Fact]
    public void Report_lines_keep_multiline_paper_on_one_physical_line()
        => Assert.Equal("a\\r\\nb", RigReport.OneLine("a\r\nb"));
    [Fact]
    public void Invalid_deadlines_are_rejected_before_setup()
        => Assert.Throws<ArgumentException>(() => New(new RigStep("bad", null, () => true, () => "", 100, StepMode.Stable, 100)));
    [Fact]
    public void Arena_reservation_is_bounded_and_dimension_specific()
    {
        var layout = new ArenaLayout(new ArenaLocation(100, 30, 200, 1));
        Assert.Equal(ArenaLayout.Width * ArenaLayout.Depth * ArenaLayout.Height, layout.Cells().Distinct().Count());
        Assert.All(layout.Cells(), p => Assert.True(layout.Contains(p)));
        Assert.False(layout.Contains(new BlockPos(100,30,200,0)));
        Assert.False(layout.Contains(layout.At(9,0,0)));
        Assert.False(layout.Contains(layout.At(0,-1,0)));
        Assert.False(layout.IsOwnedBlock(layout.Chute, "game:chest-east"));
        Assert.True(layout.IsOwnedBlock(layout.Switch, "signals:knifeswitch-north-down-on"));
        Assert.Null(layout.ExpectedCode(layout.At(8,4,4)));
    }
    static RigRunner New(params RigStep[] steps) => new(steps, (_, _) => { });
}
