using System;
using System.Collections.Generic;

namespace SignalsLink.YTT.src.taxi
{
    /// <summary>A train that might be summoned, loaded or virtual, reduced to what the choice needs.</summary>
    public sealed class TrainCandidate
    {
        /// <summary>The convoy's head: the entity id that survives virtualisation.</summary>
        public long Head { get; init; }

        public bool Virtual { get; init; }
        public double X { get; init; }
        public double Z { get; init; }
        public int Dimension { get; init; }
        public byte Gauge { get; init; }
        public bool HasPassengerCar { get; init; }
        public bool HasConductor { get; init; }
        public bool HasEngine { get; init; }
        public bool HasPlayers { get; init; }
        public bool HasFuelAndWater { get; init; }
        public bool OnTaxiRide { get; init; }

        /// <summary>Blocks per second it could do; zero when unknown.</summary>
        public double SpeedBps { get; init; }

        /// <summary>The chunk columns a virtual train needs loaded to materialise; empty when loaded.</summary>
        public IReadOnlyList<(int x, int z)> Columns { get; init; } = Array.Empty<(int, int)>();
    }

    /// <summary>The arithmetic of summoning, with no world behind it.</summary>
    public static class SummonRules
    {
        /// <summary>How far from the player a station may be to be summoned to.</summary>
        public const double StationReach = 30;

        /// <summary>Virtual trains tried before giving up.</summary>
        public const int MaxAttempts = 3;

        /// <summary>How long a virtual train gets to materialise once its columns are held.</summary>
        public const long MaterialisePatienceMs = 10000;

        /// <summary>How long held columns are kept after the train was ordered, at most.</summary>
        public const long HoldColumnsMs = 60000;

        /// <summary>At the pickup, without "wait for me".</summary>
        public const long PickupGraceMs = 15000;

        /// <summary>When nothing better is known.</summary>
        public const double DefaultSpeedBps = 6;

        /// <summary>A passenger car, by the other mod's entity code: the plain one and the early one.</summary>
        public static bool IsPassengerCar(string codePath)
        {
            return codePath != null && codePath.Contains("passenger", StringComparison.Ordinal);
        }

        /// <summary>
        /// Which of the trains to call: the nearest to the station that has a conductor, a
        /// passenger car and an engine, with nobody aboard and not already on a ride, of the
        /// station's gauge and dimension, within the limit (zero means none).
        /// </summary>
        public static List<TrainCandidate> Pick(IEnumerable<TrainCandidate> candidates, double stationX, double stationZ,
            int dimension, byte gauge, int maxBlocks)
        {
            List<(double distance, TrainCandidate train)> fit = new List<(double, TrainCandidate)>();

            foreach (TrainCandidate train in candidates ?? Array.Empty<TrainCandidate>())
            {
                if (train == null || train.Dimension != dimension || train.Gauge != gauge) continue;
                if (!train.HasPassengerCar || !train.HasConductor || !train.HasEngine) continue;
                if (train.HasPlayers || train.OnTaxiRide || !train.HasFuelAndWater) continue;

                double distance = Distance(train.X, train.Z, stationX, stationZ);
                if (maxBlocks > 0 && distance > maxBlocks) continue;

                fit.Add((distance, train));
            }

            fit.Sort((a, b) => a.distance.CompareTo(b.distance));

            List<TrainCandidate> picked = new List<TrainCandidate>(fit.Count);
            foreach ((double _, TrainCandidate train) in fit) picked.Add(train);
            return picked;
        }

        public static double Distance(double x1, double z1, double x2, double z2)
        {
            double dx = x1 - x2;
            double dz = z1 - z2;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// A guess at the arrival, in seconds: the flat distance at the train's speed, rounded up
        /// to tens. The track is longer than the crow flies, so it says "about".
        /// </summary>
        public static int EtaSeconds(double distance, double speedBps)
        {
            if (speedBps <= 0.1) speedBps = DefaultSpeedBps;

            int seconds = (int)Math.Ceiling(distance / speedBps / 10.0) * 10;
            return Math.Max(10, seconds);
        }

        /// <summary>The chunk column a block coordinate lies in.</summary>
        public static int ColumnOf(double coordinate, int chunkSize = 32)
        {
            return (int)Math.Floor(coordinate / chunkSize);
        }

        /// <summary>The columns under these positions and one all round, so a long train fits.</summary>
        public static HashSet<(int x, int z)> ColumnsAround(IEnumerable<(double x, double z)> positions, int chunkSize = 32)
        {
            HashSet<(int, int)> columns = new HashSet<(int, int)>();

            foreach ((double x, double z) in positions ?? Array.Empty<(double, double)>())
            {
                int cx = ColumnOf(x, chunkSize);
                int cz = ColumnOf(z, chunkSize);

                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++) columns.Add((cx + dx, cz + dz));
                }
            }

            return columns;
        }
    }
}
