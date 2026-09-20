using SignalsLink.src;
using SignalsLink.src.signals.chunkanchor;

namespace SignalsLink.Tests;

public class AnchorWorkTests
{
    [Fact]
    public void Short_windows_are_charged_but_sleep_offline_and_restart_gaps_are_not()
    {
        var bill = new AnchorAccounting();
        double hours = 0;
        for (int cycle = 0; cycle < 10; cycle++)
        {
            bill.Reset(cycle * 4);
            hours += bill.Advance(cycle * 4 + .25, true);
            Assert.Equal(0, bill.Advance(cycle * 4 + 3, false));
        }
        Assert.Equal(2.5, hours);
        Assert.Equal(0, bill.Advance(10000, true));
        Assert.Equal(.1, bill.Advance(10000.1, true), 6);
        Assert.Equal(0, bill.Advance(-100, true));
    }

    [Fact]
    public void Loads_and_entity_scans_are_bounded_and_shared()
    {
        int loads = 0, steps = 0;
        IEnumerable<AnchorCount> Scan()
        {
            for (int i = 0; i < 10000; i++) { steps++; yield return new AnchorCount(1, i); }
        }
        var jobs = new AnchorColumnJobs(_ => true, _ => loads++, _ => {}, _ => Scan().GetEnumerator());
        for (int i = 0; i < 100; i++) { jobs.Request(i); jobs.Request(i); }
        Assert.Equal(100, jobs.Pending);
        jobs.Tick(0);
        Assert.InRange(loads, 1, AnchorColumnJobs.StartsPerTick);
        Assert.InRange(steps, 1, AnchorColumnJobs.StepsPerTick);
        jobs.Tick(.1); jobs.Tick(.2); jobs.Tick(.3);
        Assert.Equal(AnchorColumnJobs.MaxInFlight, loads);
    }

    [Fact]
    public void A_tick_that_ran_out_of_time_before_it_began_still_does_its_minimum()
    {
        // Found by the packaging script: right after a build, with everything cold, the three
        // milliseconds were gone before the first step and the test above counted none. On a
        // busy server that is every tick, and a census would never finish.
        int steps = 0;
        IEnumerable<AnchorCount> Scan()
        {
            for (int i = 0; i < 10000; i++) { steps++; yield return new AnchorCount(1, i); }
        }
        var jobs = new AnchorColumnJobs(_ => true, _ => System.Threading.Thread.Sleep(20), _ => {}, _ => Scan().GetEnumerator());
        jobs.Request(1);
        jobs.Tick(0);
        Assert.Equal(AnchorColumnJobs.MinStepsPerTick, steps);
    }

    [Fact]
    public void Preview_releases_its_pin_but_does_not_release_a_retained_column()
    {
        var released = new List<long>();
        IEnumerable<AnchorCount> Scan() { yield return new AnchorCount(10, 2); }
        var jobs = new AnchorColumnJobs(_ => true, _ => {}, released.Add, _ => Scan().GetEnumerator());
        jobs.Request(1); jobs.Retain(2);
        for (int i = 0; i < 10; i++) jobs.Tick(i);
        Assert.Equal(new[] { 1L }, released);
        Assert.True(jobs.TryGet(1, 10, out var result));
        Assert.Equal(new AnchorCount(10, 2), result);
        jobs.LetGo(2);
        Assert.Equal(new[] { 1L, 2L }, released);
    }

    [Fact]
    public void Incomplete_columns_are_not_reported_as_empty_and_timeout_releases_previews()
    {
        int released = 0;
        var jobs = new AnchorColumnJobs(_ => false, _ => {}, _ => released++, _ => throw new Exception("not loaded"));
        Assert.False(jobs.TryGet(1, 0, out _)); jobs.Tick(0);
        Assert.False(jobs.TryGet(1, 10, out _)); jobs.Tick(61);
        Assert.Equal(1, released);
    }

    [Fact]
    public void Release_cancels_in_flight_work_and_a_queued_column_cannot_resurrect_a_pin()
    {
        int loads = 0, releases = 0;
        var jobs = new AnchorColumnJobs(_ => false, _ => loads++, _ => releases++, _ => throw new Exception());
        jobs.Retain(1); jobs.Retain(2); jobs.Tick(0);
        jobs.Retain(3); jobs.LetGo(1); jobs.LetGo(3);
        jobs.Tick(.1);
        Assert.Equal(2, loads);
        Assert.Equal(1, releases); // the queued column never acquired a pin
        Assert.Equal(1, jobs.Pending);
    }

    [Fact]
    public void Long_preview_keeps_early_results_until_the_whole_selection_is_ready()
    {
        IEnumerable<AnchorCount> Scan() { yield return new AnchorCount(10, 2); }
        var jobs = new AnchorColumnJobs(_ => true, _ => {}, _ => {}, _ => Scan().GetEnumerator());
        jobs.Request(1); jobs.Tick(0);
        Assert.True(jobs.TryGet(1, 120, out _, requestStarted: 0));
        Assert.False(jobs.TryGet(1, 120, out _));
    }

    [Fact]
    public void Config_validation_keeps_valid_values_and_repairs_invalid_values()
    {
        var config = new SignalsLinkConfig { AnchorReferenceLoad = float.NaN, AnchorPriceExponent = 1,
            AnchorCreatureWeight = -1, AnchorColumnWeight = -1, AnchorMaxColumns = -1 };
        Assert.False(config.Validate());
        Assert.Equal(250, config.AnchorReferenceLoad); Assert.True(config.AnchorPriceExponent > 1);
        Assert.Equal(10, config.AnchorCreatureWeight); Assert.Equal(5, config.AnchorColumnWeight);
        Assert.Equal(64, config.AnchorMaxColumns);
        config.AnchorMaxColumns = 0; config.AnchorColumnWeight = 0;
        Assert.True(config.Validate());
        Assert.Equal(0, config.AnchorMaxColumns);
    }

    [Fact]
    public void Bare_anchor_is_free_but_extra_empty_columns_are_not()
    {
        Assert.Equal(0, AnchorCensus.AnchorUnits(0, 0, 1));
        Assert.Equal(10, AnchorCensus.AnchorUnits(0, 0, 2));
        Assert.Equal(6, AnchorCensus.AnchorUnits(1, 0, 1));
        Assert.Equal(15, AnchorCensus.AnchorUnits(0, 1, 1));
    }
}
