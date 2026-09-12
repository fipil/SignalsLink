using System.Collections.Generic;
using SignalsLink.YTT.src.probe;

namespace SignalsLink.Tests
{
    /// <summary>
    /// The safety layers of the bridge.
    ///
    /// The bridge reads the private insides of another mod, so the interesting question is never
    /// "does it work" - it is "what happens the day it stops working". These are the parts of that
    /// answer that can be checked without either game or mod: what counts as a surface, what a
    /// change to it does, and how a moving vehicle is told from a standing one.
    /// </summary>
    public class YttSurfaceTests
    {
        // ---------------------------------------------------------------- the fingerprint

        [Fact]
        public void The_same_surface_gives_the_same_fingerprint()
        {
            Assert.Equal(Fingerprint("A:int", "B:string"), Fingerprint("A:int", "B:string"));
        }

        [Fact]
        public void A_renamed_member_changes_it()
        {
            Assert.NotEqual(Fingerprint("StoragePoints:List`1"), Fingerprint("StorageBays:List`1"));
        }

        [Fact]
        public void A_retyped_member_changes_it()
        {
            // The dangerous kind of change: the name still resolves, and the cast blows up later.
            Assert.NotEqual(Fingerprint("Inventory:InventoryGeneric"), Fingerprint("Inventory:InventoryBase"));
        }

        [Fact]
        public void A_member_appearing_or_going_changes_it()
        {
            Assert.NotEqual(Fingerprint("A:int"), Fingerprint("A:int", "B:int"));
        }

        [Fact]
        public void The_order_things_were_found_in_does_not()
        {
            // Reflection does not promise an order, so a fingerprint that depended on one would
            // cry wolf at random.
            Assert.Equal(Fingerprint("A:int", "B:int"), Fingerprint("B:int", "A:int"));
        }

        [Fact]
        public void An_empty_surface_is_not_mistaken_for_a_known_one()
        {
            Assert.NotEqual(Fingerprint(), Fingerprint("A:int"));
        }

        [Fact]
        public void It_is_short_enough_to_paste_into_a_log_line()
        {
            Assert.InRange(Fingerprint("StoragePoints:List`1", "Inventory:InventoryGeneric").Length, 8, 16);
        }

        // ---------------------------------------------------------------- standing still

        [Fact]
        public void A_vehicle_that_has_just_arrived_is_not_ready_yet()
        {
            // One sample proves nothing: a train changing direction passes through zero.
            var watch = new Watch();

            Assert.False(watch.Tick(1, 0, 0, 0));
        }

        [Fact]
        public void Two_samples_in_the_same_place_mean_it_has_stopped()
        {
            var watch = new Watch();

            watch.Tick(1, 10, 0, 10);
            watch.Tick(1, 10, 0, 10);

            Assert.True(watch.Tick(1, 10, 0, 10));
        }

        [Fact]
        public void A_train_reversing_through_a_standstill_is_not_taken_for_stopped()
        {
            // The whole reason two samples are wanted rather than one.
            var watch = new Watch();

            watch.Tick(1, 10.0, 0, 10);
            Assert.False(watch.Tick(1, 10.0, 0, 10));      // one quiet sample
            Assert.False(watch.Tick(1, 10.5, 0, 10));      // and off it goes again
        }

        [Fact]
        public void Moving_again_takes_the_readiness_away_at_once()
        {
            var watch = new Watch();

            watch.Tick(2, 5, 0, 5);
            watch.Tick(2, 5, 0, 5);
            Assert.True(watch.Tick(2, 5, 0, 5));

            Assert.False(watch.Tick(2, 6, 0, 5));
        }

        [Fact]
        public void A_crawling_vehicle_counts_as_moving()
        {
            var watch = new Watch();
            double step = StandingWatch.StillnessThreshold * 2;

            for (int i = 0; i < 5; i++)
            {
                Assert.False(watch.Tick(3, i * step, 0, 0));
            }
        }

        [Fact]
        public void A_shiver_below_the_threshold_still_counts_as_standing()
        {
            // Physics never puts a body down at exactly the same coordinates twice.
            var watch = new Watch();
            double shiver = StandingWatch.StillnessThreshold / 4;

            watch.Tick(4, 0, 0, 0);
            watch.Tick(4, shiver, 0, 0);

            Assert.True(watch.Tick(4, 0, 0, shiver));
        }

        [Fact]
        public void Vehicles_are_watched_one_by_one()
        {
            var watch = new Watch();

            watch.Tick(1, 0, 0, 0);
            watch.Tick(1, 0, 0, 0);
            watch.Tick(2, 0, 0, 0);

            Assert.True(watch.Tick(1, 0, 0, 0));
            Assert.False(watch.Tick(2, 5, 0, 0));
        }

        [Fact]
        public void A_vehicle_nobody_looks_at_any_more_is_forgotten()
        {
            // Otherwise every train that ever passed stays in the dock's memory for the world's life.
            var watch = new Watch();

            watch.Tick(1, 0, 0, 0);
            watch.Watcher.Forget(watch.Now + StandingWatch.MemoryMs);

            Assert.Equal(0, watch.Watcher.Count);
        }

        [Fact]
        public void One_still_being_looked_at_is_kept()
        {
            var watch = new Watch();

            watch.Tick(1, 0, 0, 0);
            watch.Tick(1, 0, 0, 0);
            watch.Watcher.Forget(watch.Now);

            Assert.True(watch.Tick(1, 0, 0, 0));
        }

        [Fact]
        public void One_dock_looking_does_not_erase_what_another_dock_watches()
        {
            // The fault this replaced: Forget used to clear whatever the caller could not see, and
            // one finder serves every dock. Two docks at a two-track station wiped each other's
            // train on every tick, so neither ever gathered two quiet samples and nothing loaded.
            var watch = new Watch();

            watch.Tick(1, 0, 0, 0);      // the first dock's train
            watch.Tick(2, 50, 0, 0);     // the second dock's, elsewhere
            watch.Tick(1, 0, 0, 0);
            watch.Tick(2, 50, 0, 0);

            Assert.True(watch.Tick(1, 0, 0, 0));
            Assert.True(watch.Tick(2, 50, 0, 0));
        }

        [Fact]
        public void Two_docks_asking_within_one_tick_do_not_call_a_moving_train_stopped()
        {
            // The second ask would compare the position with itself.
            var watch = new Watch();

            watch.Tick(1, 0, 0, 0);
            watch.Tick(1, 0, 0, 0);
            Assert.True(watch.Tick(1, 0, 0, 0));

            // It moves, and both docks ask in the same tick.
            Assert.False(watch.Watcher.IsStanding(1, 9, 0, 0, watch.Now + 1));
            Assert.False(watch.Watcher.IsStanding(1, 9, 0, 0, watch.Now + 1));
        }

        // ---------------------------------------------------------------- plumbing

        private static string Fingerprint(params string[] members)
        {
            return YttSurface.Fingerprint(members);
        }

        /// <summary>A StandingWatch with a clock, one sample per tick, the way a device uses it.</summary>
        private sealed class Watch
        {
            public StandingWatch Watcher { get; } = new StandingWatch();

            public long Now { get; private set; } = 1000;

            public bool Tick(long id, double x, double y, double z)
            {
                Now += 200;
                return Watcher.IsStanding(id, x, y, z, Now);
            }
        }
    }
}
