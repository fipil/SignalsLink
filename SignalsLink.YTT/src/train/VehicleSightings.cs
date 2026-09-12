using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace SignalsLink.YTT.src.train
{
    /// <summary>
    /// Which vehicles were last seen near a device, so the search over entities does not run on
    /// every tick.
    ///
    /// Only ids are kept. An id is worth nothing until it is exchanged for a live entity, which is
    /// what makes this different from remembering a wagon: a vehicle that has gone resolves to
    /// null and cannot be written into.
    /// </summary>
    public sealed class VehicleSightings
    {
        /// <summary>How long a sighting is trusted before the world is searched again.</summary>
        public const long TrustedMs = 500;

        /// <summary>A device nobody has asked about for this long is dropped.</summary>
        private const long StaleMs = 60000;

        /// <summary>Below this many devices, pruning is not worth the walk.</summary>
        private const int PruneAbove = 32;

        private readonly Dictionary<BlockPos, Sighting> seen = new Dictionary<BlockPos, Sighting>();

        public int Count => seen.Count;

        /// <summary>
        /// The ids last seen near this device, or null when the world has to be searched again.
        ///
        /// One vehicle no longer there discards the whole sighting, not just that one: something
        /// left, so something else may have arrived, and only a search can tell.
        /// </summary>
        public long[] Recall(BlockPos pos, long now, Func<long, bool> stillThere)
        {
            if (pos == null || stillThere == null) return null;
            if (!seen.TryGetValue(pos, out Sighting sighting)) return null;

            if (now - sighting.At >= TrustedMs)
            {
                seen.Remove(pos);
                return null;
            }

            foreach (long id in sighting.Ids)
            {
                if (stillThere(id)) continue;

                seen.Remove(pos);
                return null;
            }

            return sighting.Ids;
        }

        public void Remember(BlockPos pos, long now, long[] ids)
        {
            if (pos == null || ids == null) return;

            seen[pos.Copy()] = new Sighting(ids, now);

            Prune(now);
        }

        private void Prune(long now)
        {
            if (seen.Count <= PruneAbove) return;

            List<BlockPos> gone = null;

            foreach (KeyValuePair<BlockPos, Sighting> pair in seen)
            {
                if (now - pair.Value.At < StaleMs) continue;

                (gone ??= new List<BlockPos>()).Add(pair.Key);
            }

            if (gone == null) return;

            foreach (BlockPos pos in gone) seen.Remove(pos);
        }

        private readonly struct Sighting
        {
            public Sighting(long[] ids, long at)
            {
                Ids = ids;
                At = at;
            }

            public long[] Ids { get; }
            public long At { get; }
        }
    }
}
