using SignalsLink.YTT.src.train;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>
    /// When a train counts as standing for cargo.
    ///
    /// A timetabled train tells the world what it is doing, on every vehicle, as a plain attribute.
    /// "Waiting at station" is better than two quiet position samples: it means the dwell has
    /// begun. Nothing else it says is taken over the samples - "going" included, because a train
    /// that is going but not moving has run out of steam and is exactly the one to be stoked.
    ///
    /// But only while a conductor is on board. The attribute is not cleared when the conductor
    /// comes off after a restart, and a stale "waiting at station" on a hand-driven train would
    /// have goods loaded into it while it moves.
    /// </summary>
    public class TrainReadinessTests
    {
        [Fact]
        public void Waiting_at_a_station_is_standing_whatever_the_samples_say()
        {
            Assert.True(TrainSignals.IsStanding(TrainSignals.WaitingAtStation, automated: true, () => false));
        }

        [Fact]
        public void A_going_train_that_does_not_move_is_standing_and_can_be_stoked()
        {
            // Seen in a dump: a steam cart with a conductor, "going" for an hour, boiler out. The
            // coal it needs comes off the dock, so "going" must not be read as "not standing".
            Assert.True(TrainSignals.IsStanding(TrainSignals.Going, automated: true, () => true));
            Assert.False(TrainSignals.IsStanding(TrainSignals.Going, automated: true, () => false));
        }

        [Theory]
        [InlineData(0)]   // nothing written yet
        [InlineData(3)]   // stuck
        [InlineData(4)]   // held at a signal
        [InlineData(5)]   // conductor without a timetable
        public void Anything_else_is_judged_by_watching_it(int code)
        {
            Assert.True(TrainSignals.IsStanding(code, automated: true, () => true));
            Assert.False(TrainSignals.IsStanding(code, automated: true, () => false));
        }

        [Theory]
        [InlineData(TrainSignals.WaitingAtStation)]
        [InlineData(TrainSignals.Going)]
        public void Without_a_conductor_the_code_is_stale_and_only_the_watching_counts(int code)
        {
            // Found in a dump: a convoy standing still for an hour and still saying "going".
            Assert.True(TrainSignals.IsStanding(code, automated: false, () => true));
            Assert.False(TrainSignals.IsStanding(code, automated: false, () => false));
        }

        [Fact]
        public void With_nothing_to_watch_it_is_not_standing()
        {
            Assert.False(TrainSignals.IsStanding(0, automated: true, null));
            Assert.False(TrainSignals.IsStanding(0, automated: false, null));
        }

        [Fact]
        public void Nothing_has_a_conductor_without_an_entity()
        {
            Assert.False(TrainSignals.HasConductorLocust(null));
        }

        [Fact]
        public void The_attribute_is_the_one_YTT_writes()
        {
            // Pinned here because it is the whole coupling: rename it upstream and this is what breaks.
            Assert.Equal("yangtransport.locustActionCode", TrainSignals.ActionAttribute);
            Assert.Equal("conductorlocust", TrainSignals.ConductorLocust);
        }
    }
}
