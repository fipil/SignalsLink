using SignalsLink.src.signals.manageddock;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>
    /// How fast the dock runs next.
    ///
    /// What is being paid for is the SEARCH. A yard is found once and kept for seconds; a train
    /// cannot be kept at all, so every tick that moves goods also pays for a fresh look at every
    /// entity around the dock. The beat is where that is decided, and it is the only knob for it.
    /// </summary>
    public class DockTickRateTests
    {
        [Fact]
        public void Nothing_found_means_only_looking_around()
        {
            Assert.Equal(DockTickRate.IdleMs, DockTickRate.Next(sawHolder: false, actionPerformed: false, searchedLive: false));
        }

        [Fact]
        public void Nothing_found_stays_idle_however_hard_it_was_looked_for()
        {
            // A train that has not arrived is the case this runs into most, and it must not buy
            // itself a fast beat by being expensive to look for.
            Assert.Equal(DockTickRate.IdleMs, DockTickRate.Next(sawHolder: false, actionPerformed: false, searchedLive: true));
        }

        [Fact]
        public void A_holder_found_but_no_work_waits_rather_than_idling()
        {
            // The train is still standing there. Dropping to a one second beat would mean a chute
            // could top the crate up and nothing would happen until it left.
            Assert.Equal(DockTickRate.WaitingMs, DockTickRate.Next(sawHolder: true, actionPerformed: false, searchedLive: false));
        }

        [Fact]
        public void Working_against_something_that_stays_put_runs_fast()
        {
            Assert.Equal(DockTickRate.WorkingMs, DockTickRate.Next(sawHolder: true, actionPerformed: true, searchedLive: false));
        }

        [Fact]
        public void Working_against_something_that_must_be_found_again_does_not()
        {
            // The point of the whole thing: 5 searches a second instead of 20, and the guarantee
            // that pays for it stays intact - nothing about a vehicle is held between ticks.
            Assert.Equal(DockTickRate.WaitingMs, DockTickRate.Next(sawHolder: true, actionPerformed: true, searchedLive: true));
        }

        [Fact]
        public void The_fast_beat_is_the_only_one_held_back()
        {
            // Holding back the waiting beat as well would be the easy mistake: it is already slow
            // enough, and a train arriving would then be noticed a second late.
            Assert.True(DockTickRate.WorkingMs < DockTickRate.WaitingMs);
            Assert.True(DockTickRate.WaitingMs < DockTickRate.IdleMs);
        }
    }
}
