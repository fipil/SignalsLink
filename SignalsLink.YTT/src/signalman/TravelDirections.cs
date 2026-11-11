using System.Collections.Generic;

namespace SignalsLink.YTT.src.signalman
{
    /// <summary>
    /// Which way each train on the watched block is going, worked out from how its vehicles
    /// move between looks and remembered for as long as the train stays - a train that came in
    /// from the right and stopped is still a train from the right.
    ///
    /// Plain arithmetic over positions, so it can be checked on a table. Forgotten by age, never
    /// by who asked.
    /// </summary>
    public sealed class TravelDirections
    {
        /// <summary>A train not seen for this long is forgotten, direction and all.</summary>
        public const long MemoryMs = 10000;

        private readonly Dictionary<long, (double along, long at)> lastSeen = new Dictionary<long, (double, long)>();
        private readonly Dictionary<long, (TravelDirection direction, long at)> convoys = new Dictionary<long, (TravelDirection, long)>();

        /// <summary>
        /// One look at a train: every vehicle on the block, as its position along the plate's
        /// "right" axis. Returns the train's direction, remembered or just found.
        /// </summary>
        public TravelDirection Observe(long convoyHead, IEnumerable<(long entityId, double alongRight)> vehicles, long now)
        {
            double moved = 0;

            foreach ((long id, double along) in vehicles)
            {
                if (lastSeen.TryGetValue(id, out (double along, long at) last) && last.at != now) moved += along - last.along;

                lastSeen[id] = (along, now);
            }

            TravelDirection seen = SignalmanRules.DirectionOf(moved);

            convoys.TryGetValue(convoyHead, out (TravelDirection direction, long at) known);

            TravelDirection direction = seen != TravelDirection.Unknown ? seen : known.direction;
            convoys[convoyHead] = (direction, now);

            return direction;
        }

        /// <summary>Drops what has not been seen for a while.</summary>
        public void Forget(long now)
        {
            Prune(lastSeen, pair => pair.Value.at, now);
            Prune(convoys, pair => pair.Value.at, now);
        }

        private static void Prune<TValue>(Dictionary<long, TValue> map, System.Func<KeyValuePair<long, TValue>, long> at, long now)
        {
            List<long> gone = null;

            foreach (KeyValuePair<long, TValue> pair in map)
            {
                if (now - at(pair) < MemoryMs) continue;

                (gone ??= new List<long>()).Add(pair.Key);
            }

            if (gone == null) return;

            foreach (long id in gone) map.Remove(id);
        }
    }
}
