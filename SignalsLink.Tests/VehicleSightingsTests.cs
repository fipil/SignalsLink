using System.Collections.Generic;
using SignalsLink.YTT.src.train;
using Vintagestory.API.MathTools;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>
    /// Remembering which vehicles stand near a device, so the search over entities does not run on
    /// every tick.
    ///
    /// The rule that matters is not the cache, it is when the cache must be thrown away. Handing
    /// back a vehicle that has driven off is how goods get written into a wagon that is no longer
    /// there - the one fault this whole bridge is built to avoid.
    /// </summary>
    public class VehicleSightingsTests
    {
        private static readonly BlockPos Dock = new BlockPos(10, 5, 10);

        [Fact]
        public void What_was_seen_is_handed_back_within_the_moment()
        {
            VehicleSightings sightings = new VehicleSightings();
            sightings.Remember(Dock, 1000, new long[] { 7, 8 });

            Assert.Equal(new long[] { 7, 8 }, sightings.Recall(Dock, 1100, All));
        }

        [Fact]
        public void An_old_sighting_is_not_trusted()
        {
            VehicleSightings sightings = new VehicleSightings();
            sightings.Remember(Dock, 1000, new long[] { 7 });

            Assert.Null(sightings.Recall(Dock, 1000 + VehicleSightings.TrustedMs, All));
        }

        [Fact]
        public void One_vehicle_gone_discards_the_whole_sighting()
        {
            // Not just that one: something left, so something else may have arrived, and only a
            // search can tell.
            VehicleSightings sightings = new VehicleSightings();
            sightings.Remember(Dock, 1000, new long[] { 7, 8 });

            Assert.Null(sightings.Recall(Dock, 1100, id => id != 8));
        }

        [Fact]
        public void A_discarded_sighting_is_not_offered_again()
        {
            // Even to a caller who would now accept it: the answer was thrown away, not merely
            // withheld, or a train that drove off and came back would be recalled from memory.
            VehicleSightings sightings = new VehicleSightings();
            sightings.Remember(Dock, 1000, new long[] { 7, 8 });

            sightings.Recall(Dock, 1100, id => id != 8);

            Assert.Null(sightings.Recall(Dock, 1100, All));
        }

        [Fact]
        public void A_device_never_seen_has_nothing_to_recall()
        {
            VehicleSightings sightings = new VehicleSightings();

            Assert.Null(sightings.Recall(Dock, 1000, All));
            Assert.Null(sightings.Recall(null, 1000, All));
        }

        [Fact]
        public void Two_devices_remember_separately()
        {
            // One shared finder serves every dock in the world; a station with two of them must
            // not answer for the wrong one.
            VehicleSightings sightings = new VehicleSightings();
            BlockPos other = new BlockPos(90, 5, 90);

            sightings.Remember(Dock, 1000, new long[] { 7 });
            sightings.Remember(other, 1000, new long[] { 8 });

            Assert.Equal(new long[] { 7 }, sightings.Recall(Dock, 1100, All));
            Assert.Equal(new long[] { 8 }, sightings.Recall(other, 1100, All));
        }

        [Fact]
        public void An_empty_platform_is_remembered_too()
        {
            // The case that would otherwise search every tick forever: a dock waiting for a train
            // that has not come.
            VehicleSightings sightings = new VehicleSightings();
            sightings.Remember(Dock, 1000, new long[0]);

            Assert.NotNull(sightings.Recall(Dock, 1100, All));
        }

        [Fact]
        public void Devices_long_unused_are_dropped()
        {
            // A dock that was broken would otherwise sit in memory for the life of the world.
            VehicleSightings sightings = new VehicleSightings();

            for (int i = 0; i < 40; i++) sightings.Remember(new BlockPos(i, 5, 0), 1000, new long[] { i });

            Assert.Equal(40, sightings.Count);

            sightings.Remember(new BlockPos(100, 5, 0), 1000 + 120000, new long[] { 99 });

            Assert.Equal(1, sightings.Count);
        }

        private static bool All(long id) => true;
    }
}
