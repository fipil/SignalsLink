using System.Collections.Generic;

namespace SignalsLink.YTT.src.probe
{
    /// <summary>
    /// Tells a vehicle that has stopped from one that is merely slow, by sampling positions - the
    /// other mod publishes no speed.
    ///
    /// Two quiet samples, not one: a train changing direction passes through a standstill.
    ///
    /// Standing is a property of the VEHICLE, not of whoever is asking. One finder serves every
    /// dock in the world, so nothing here may be keyed on, or cleared by, a single device's view.
    /// </summary>
    public class StandingWatch
    {
        /// <summary>Physics never puts a body down at exactly the same coordinates twice.</summary>
        public const double StillnessThreshold = 0.01;

        /// <summary>A vehicle nobody has looked at for this long is forgotten.</summary>
        public const long MemoryMs = 10000;

        private readonly Dictionary<long, Sample> samples = new Dictionary<long, Sample>();

        public int Count => samples.Count;

        /// <summary>Call once per tick per vehicle; <paramref name="now"/> is the world clock.</summary>
        public bool IsStanding(long entityId, double x, double y, double z, long now)
        {
            if (!samples.TryGetValue(entityId, out Sample last))
            {
                samples[entityId] = new Sample(x, y, z, 0, now);
                return false;
            }

            // Two docks may watch the same train within one tick. Comparing a position with
            // itself would find it quiet, and a moving train would be called stopped.
            if (now == last.At) return last.QuietRuns >= 2;

            bool quiet = Distance(last, x, y, z) <= StillnessThreshold;

            // Moving takes readiness away at once: that inventory is about to leave.
            int quietRuns = quiet ? last.QuietRuns + 1 : 0;

            samples[entityId] = new Sample(x, y, z, quietRuns, now);

            return quietRuns >= 2;
        }

        /// <summary>
        /// Drops what has not been looked at for a while, or every train that ever passed stays
        /// for the life of the world.
        ///
        /// By age, never by who asked. Clearing whatever one device could not see wiped the other
        /// device's train on every tick, so neither ever gathered two quiet samples and no dock in
        /// a two-track station ever loaded anything.
        /// </summary>
        public void Forget(long now)
        {
            List<long> gone = null;

            foreach (KeyValuePair<long, Sample> pair in samples)
            {
                if (now - pair.Value.At < MemoryMs) continue;

                (gone ??= new List<long>()).Add(pair.Key);
            }

            if (gone == null) return;

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
            public Sample(double x, double y, double z, int quietRuns, long at)
            {
                X = x;
                Y = y;
                Z = z;
                QuietRuns = quietRuns;
                At = at;
            }

            public double X { get; }
            public double Y { get; }
            public double Z { get; }
            public int QuietRuns { get; }
            public long At { get; }
        }
    }
}
