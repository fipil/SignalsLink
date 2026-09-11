using System.Collections.Generic;

namespace SignalsLink.YTT.src.probe
{
    /// <summary>
    /// Tells a vehicle that has stopped from one that is merely slow, by sampling positions - the
    /// other mod publishes no speed.
    ///
    /// Two quiet samples, not one: a train changing direction passes through a standstill.
    /// </summary>
    public class StandingWatch
    {
        /// <summary>Physics never puts a body down at exactly the same coordinates twice.</summary>
        public const double StillnessThreshold = 0.01;

        private readonly Dictionary<long, Sample> samples = new Dictionary<long, Sample>();

        /// <summary>Call once per tick per vehicle.</summary>
        public bool IsStanding(long entityId, double x, double y, double z)
        {
            if (!samples.TryGetValue(entityId, out Sample last))
            {
                samples[entityId] = new Sample(x, y, z, 0);
                return false;
            }

            bool quiet = Distance(last, x, y, z) <= StillnessThreshold;

            // Moving takes readiness away at once: that inventory is about to leave.
            int quietRuns = quiet ? last.QuietRuns + 1 : 0;

            samples[entityId] = new Sample(x, y, z, quietRuns);

            return quietRuns >= 2;
        }

        /// <summary>Drops everything not in the list, or every train that passed stays forever.</summary>
        public void Forget(ISet<long> stillHere)
        {
            List<long> gone = new List<long>();

            foreach (long id in samples.Keys)
            {
                if (!stillHere.Contains(id)) gone.Add(id);
            }

            foreach (long id in gone) samples.Remove(id);
        }

        private static double Distance(Sample last, double x, double y, double z)
        {
            double dx = last.X - x;
            double dy = last.Y - y;
            double dz = last.Z - z;

            return System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private readonly struct Sample
        {
            public Sample(double x, double y, double z, int quietRuns)
            {
                X = x;
                Y = y;
                Z = z;
                QuietRuns = quietRuns;
            }

            public double X { get; }
            public double Y { get; }
            public double Z { get; }
            public int QuietRuns { get; }
        }
    }
}
