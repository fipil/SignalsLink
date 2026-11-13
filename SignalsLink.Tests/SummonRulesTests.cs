using System.Collections.Generic;
using System.Linq;
using SignalsLink.YTT.src.taxi;

namespace SignalsLink.Tests
{
    /// <summary>Summoning a train: who qualifies, who comes first, how far off the guess is, which columns to hold.</summary>
    public class SummonRulesTests
    {
        private static TrainCandidate Train(long head, double x, double z, bool ok = true, byte gauge = 1, int dimension = 0)
        {
            return new TrainCandidate
            {
                Head = head, X = x, Z = z, Dimension = dimension, Gauge = gauge,
                HasPassengerCar = ok, HasConductor = ok, HasEngine = ok, HasFuelAndWater = ok,
                HasPlayers = false, OnTaxiRide = false,
            };
        }

        [Theory]
        [InlineData("sgwagon_passenger", true)]
        [InlineData("sgwagon_early_passenger", true)]
        [InlineData("sgwagon_boxcar", false)]
        [InlineData("sglocomotive", false)]
        [InlineData(null, false)]
        public void A_passenger_car_is_known_by_its_code(string code, bool expected)
        {
            Assert.Equal(expected, SummonRules.IsPassengerCar(code));
        }

        [Fact]
        public void The_nearest_fit_train_comes_first()
        {
            List<TrainCandidate> picked = SummonRules.Pick(new[] { Train(1, 500, 0), Train(2, 100, 0), Train(3, 300, 0) }, 0, 0, 0, 1, 0);

            Assert.Equal(new long[] { 2, 3, 1 }, picked.Select(t => t.Head).ToArray());
        }

        [Fact]
        public void A_train_that_does_not_fit_is_left_out()
        {
            TrainCandidate noCar = Train(1, 10, 0); noCar = new TrainCandidate { Head = 1, X = 10, HasPassengerCar = false, HasConductor = true, HasEngine = true, HasFuelAndWater = true, Gauge = 1 };
            TrainCandidate busy = new TrainCandidate { Head = 2, X = 20, HasPassengerCar = true, HasConductor = true, HasEngine = true, HasFuelAndWater = true, HasPlayers = true, Gauge = 1 };
            TrainCandidate riding = new TrainCandidate { Head = 3, X = 30, HasPassengerCar = true, HasConductor = true, HasEngine = true, HasFuelAndWater = true, OnTaxiRide = true, Gauge = 1 };
            TrainCandidate dry = new TrainCandidate { Head = 4, X = 40, HasPassengerCar = true, HasConductor = true, HasEngine = true, HasFuelAndWater = false, Gauge = 1 };
            TrainCandidate narrow = Train(5, 50, 0, gauge: 0);
            TrainCandidate elsewhere = Train(6, 60, 0, dimension: 1);
            TrainCandidate fine = Train(7, 70, 0);

            List<TrainCandidate> picked = SummonRules.Pick(new[] { noCar, busy, riding, dry, narrow, elsewhere, fine }, 0, 0, 0, 1, 0);

            Assert.Single(picked);
            Assert.Equal(7, picked[0].Head);
        }

        [Fact]
        public void The_limit_keeps_far_trains_out_and_zero_means_none()
        {
            TrainCandidate[] trains = { Train(1, 100, 0), Train(2, 3000, 0) };

            Assert.Single(SummonRules.Pick(trains, 0, 0, 0, 1, 2000));
            Assert.Equal(2, SummonRules.Pick(trains, 0, 0, 0, 1, 0).Count);
        }

        [Theory]
        [InlineData(0, 6, 10)]       // never less than ten seconds
        [InlineData(300, 6, 50)]
        [InlineData(301, 6, 60)]     // rounded up to tens
        [InlineData(300, 0, 50)]     // unknown speed: the default
        public void The_guess_is_distance_over_speed_in_tens(double distance, double speed, int expected)
        {
            Assert.Equal(expected, SummonRules.EtaSeconds(distance, speed));
        }

        [Fact]
        public void Columns_are_the_ones_under_the_train_and_a_ring_around()
        {
            HashSet<(int x, int z)> columns = SummonRules.ColumnsAround(new[] { (40.0, 40.0) });

            Assert.Equal(9, columns.Count);
            Assert.Contains((1, 1), columns);
            Assert.Contains((0, 0), columns);
            Assert.Contains((2, 2), columns);
            Assert.DoesNotContain((3, 1), columns);
        }

        [Fact]
        public void Negative_coordinates_land_in_the_right_column()
        {
            Assert.Equal(-1, SummonRules.ColumnOf(-0.5));
            Assert.Equal(-2, SummonRules.ColumnOf(-33));
            Assert.Equal(0, SummonRules.ColumnOf(31.9));
        }
    }
}
