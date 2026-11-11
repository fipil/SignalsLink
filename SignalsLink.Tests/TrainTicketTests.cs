using SignalsLink.YTT.src.taxi;
using Vintagestory.API.Datastructures;

namespace SignalsLink.Tests
{
    /// <summary>
    /// The ticket's bookkeeping, on plain attribute trees: the conductor's timetable set aside,
    /// replaced by one stop, cleared, and put back; and a ride's state round-tripped.
    /// </summary>
    public class TrainTicketTests
    {
        private static StationInfo Station(string name, ulong hash) => new StationInfo
        {
            Name = name, NameHash = hash, X = 10, Y = 64, Z = -20, Dimension = 0, Gauge = 1,
        };

        private static ITreeAttribute ConductorTimetable(int stops)
        {
            TreeAttribute attributes = new TreeAttribute();
            ITreeAttribute root = attributes.GetOrAddTreeAttribute(Timetable.Key);

            root.SetInt("count", stops);
            root.SetInt("currentStationIndex", 1);
            root.SetInt("changeSerial", 7);

            for (int i = 0; i < stops; i++)
            {
                ITreeAttribute entry = root.GetOrAddTreeAttribute("entry" + i);
                entry.SetString("stationName", "Stop " + i);
                entry.SetLong("nameHash", 100 + i);
            }

            return attributes;
        }

        // ---------------------------------------------------------------- one stop, and stay

        [Fact]
        public void A_single_stop_replaces_the_timetable_and_bumps_the_serial()
        {
            ITreeAttribute attributes = ConductorTimetable(3);

            Timetable.WriteSingle(attributes, Station("Depot", 42));

            ITreeAttribute root = attributes.GetTreeAttribute(Timetable.Key);
            Assert.Equal(1, root.GetInt("count"));
            Assert.Equal(0, root.GetInt("currentStationIndex"));
            Assert.Equal(8, root.GetInt("changeSerial"));
            Assert.Equal("Depot", root.GetTreeAttribute("entry0").GetString("stationName"));
            Assert.Equal(Timetable.HoldSeconds, root.GetTreeAttribute("entry0").GetInt("timeElapsedSeconds"));
            Assert.Null(root.GetTreeAttribute("entry1"));
            Assert.Null(root.GetTreeAttribute("entry2"));
            Assert.Equal(42UL, Timetable.CurrentDestinationHash(attributes));
        }

        [Fact]
        public void Clearing_leaves_no_stops_and_bumps_the_serial()
        {
            ITreeAttribute attributes = ConductorTimetable(2);

            Timetable.Clear(attributes);

            ITreeAttribute root = attributes.GetTreeAttribute(Timetable.Key);
            Assert.Equal(0, root.GetInt("count"));
            Assert.Equal(8, root.GetInt("changeSerial"));
            Assert.Null(root.GetTreeAttribute("entry0"));
            Assert.Equal(0, Timetable.Count(attributes));
        }

        // ---------------------------------------------------------------- set aside, put back

        [Fact]
        public void A_snapshot_survives_the_ride_and_comes_back_with_a_higher_serial()
        {
            ITreeAttribute attributes = ConductorTimetable(3);
            ITreeAttribute saved = Timetable.Snapshot(attributes);

            Timetable.WriteSingle(attributes, Station("Depot", 42));
            Timetable.Clear(attributes);
            Timetable.Restore(attributes, saved);

            ITreeAttribute root = attributes.GetTreeAttribute(Timetable.Key);
            Assert.Equal(3, root.GetInt("count"));
            Assert.Equal(1, root.GetInt("currentStationIndex"));
            Assert.Equal("Stop 2", root.GetTreeAttribute("entry2").GetString("stationName"));
            Assert.True(root.GetInt("changeSerial") > 9);
        }

        [Fact]
        public void The_snapshot_is_a_copy_not_the_tree_itself()
        {
            ITreeAttribute attributes = ConductorTimetable(2);
            ITreeAttribute saved = Timetable.Snapshot(attributes);

            Timetable.Clear(attributes);

            Assert.Equal(2, saved.GetInt("count"));
        }

        [Fact]
        public void No_timetable_before_means_none_after()
        {
            ITreeAttribute attributes = new TreeAttribute();
            ITreeAttribute saved = Timetable.Snapshot(attributes);

            Assert.Null(saved);

            Timetable.WriteSingle(attributes, Station("Depot", 42));
            Timetable.Restore(attributes, saved);

            Assert.Equal(0, Timetable.Count(attributes));
        }

        // ---------------------------------------------------------------- the ride itself

        [Fact]
        public void A_ride_round_trips_through_its_tree()
        {
            TaxiJob job = new TaxiJob
            {
                Phase = TaxiPhase.Alighting,
                Wait = true,
                Stopping = true,
                ConvoyHead = 1234,
                HolderUid = "abc",
                DestinationName = "Depot",
                DestinationHash = ulong.MaxValue - 5,
                DestinationDimension = 0,
                SavedTimetable = ConductorTimetable(2).GetTreeAttribute(Timetable.Key).Clone() as ITreeAttribute,
                SavedBurning = true,
                OriginX = 1.5, OriginY = 64, OriginZ = -3.25,
                ReturnName = "Yard", ReturnHash = 99, ReturnX = 4, ReturnY = 65, ReturnZ = 6, ReturnGauge = 1,
                HolderLeftAtMs = 5000,
                StuckSinceMs = 7,
                StuckReported = true,
            };

            TreeAttribute tree = new TreeAttribute();
            job.ToTree(tree);
            TaxiJob back = TaxiJob.FromTree(tree);

            Assert.Equal(TaxiPhase.Alighting, back.Phase);
            Assert.True(back.Wait);
            Assert.True(back.Stopping);
            Assert.Equal(1234, back.ConvoyHead);
            Assert.Equal("abc", back.HolderUid);
            Assert.Equal(ulong.MaxValue - 5, back.DestinationHash);
            Assert.Equal(2, back.SavedTimetable.GetInt("count"));
            Assert.True(back.SavedBurning);
            Assert.Equal(-3.25, back.OriginZ);
            Assert.Equal("Yard", back.ReturnStation().Name);
            Assert.Equal(5000, back.HolderLeftAtMs);
            Assert.True(back.StuckReported);
        }

        [Fact]
        public void A_ride_with_no_timetable_before_has_none_to_restore()
        {
            TaxiJob job = new TaxiJob { SavedTimetable = null };

            TreeAttribute tree = new TreeAttribute();
            job.ToTree(tree);

            Assert.Null(TaxiJob.FromTree(tree).SavedTimetable);
            Assert.False(TaxiJob.FromTree(tree).HasReturnStation);
        }
    }
}
