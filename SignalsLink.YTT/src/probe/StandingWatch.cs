using System.Collections.Generic;

namespace SignalsLink.YTT.src.probe
{
    /// <summary>
    /// Tells a vehicle that has stopped from one that is merely slow.
    ///
    /// The other mod keeps no speed anywhere a bystander can read, so the only honest way to ask is
    /// to watch where a vehicle IS from one moment to the next.
    ///
    /// <b>Two quiet samples, not one.</b> A train changing direction passes through a standstill,
    /// and a dock that believed the first sample would start unloading a wagon that is about to
    /// pull away - with the goods half moved.
    ///
    /// It is a plain state machine over positions on purpose: no world, no entities, no mod. What
    /// counts as stopped is the sort of thing that is wrong in a way nobody notices for weeks, so
    /// it is worth being able to check it on a table.
    /// </summary>
    public class StandingWatch
    {
        /// <summary>
        /// How far a vehicle may drift between two looks and still count as standing. Physics never
        /// puts a body down at exactly the same coordinates twice.
        /// </summary>
        public const double StillnessThreshold = 0.01;

        private readonly Dictionary<long, Sample> samples = new Dictionary<long, Sample>();

        /// <summary>
        /// Has this vehicle been in the same place for two looks running? Call once per tick per
        /// vehicle; the answer is about the vehicle, not about this call.
        /// </summary>
        public bool IsStanding(long entityId, double x, double y, double z)
        {
            if (!samples.TryGetValue(entityId, out Sample last))
            {
                samples[entityId] = new Sample(x, y, z, 0);
                return false;
            }

            bool quiet = Distance(last, x, y, z) <= StillnessThreshold;

            // Moving takes readiness away at once, and it has to: the goods are being written into
            // an inventory that is about to leave.
            int quietRuns = quiet ? last.QuietRuns + 1 : 0;

            samples[entityId] = new Sample(x, y, z, quietRuns);

            return quietRuns >= 2;
        }

        /// <summary>
        /// Drops everything not in the list. Without it every train that ever passed would stay in
        /// the dock's memory for the life of the world.
        /// </summary>
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
