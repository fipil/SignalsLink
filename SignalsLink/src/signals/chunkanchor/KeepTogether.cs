using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace SignalsLink.src.signals.chunkanchor
{
    /// <summary>
    /// Something that knows of groups of entities which must be loaded together or not at all -
    /// a train. Half a train unloaded is a train that never moves again.
    /// </summary>
    public interface IKeepTogetherSource
    {
        /// <summary>
        /// Every group currently loaded, each as the chunk columns its members stand in. A group
        /// standing in one column cannot be split and need not be reported.
        /// </summary>
        IEnumerable<IReadOnlyCollection<long>> Groups(IWorldAccessor world, System.Func<double, double, long> columnOf);
    }

    /// <summary>
    /// The kinds of group this game knows about. A registry rather than a reference, because the
    /// only one today lives in another mod.
    /// </summary>
    public class KeepTogetherRegistry : ModSystem
    {
        private readonly List<IKeepTogetherSource> sources = new List<IKeepTogetherSource>();

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public IReadOnlyList<IKeepTogetherSource> Sources => sources;

        public void Register(IKeepTogetherSource source)
        {
            if (source != null && !sources.Contains(source)) sources.Add(source);
        }
    }

    /// <summary>
    /// The extra columns an anchor holds so that no group standing on its ground is split. Pure
    /// arithmetic on column sets, so the rule can be checked without a world.
    /// </summary>
    public static class AnchorHalo
    {
        /// <summary>
        /// A group counts while it touches the anchor's own columns or the halo it already has -
        /// so a train driving off is followed to the edge of the window and let go there, where
        /// the train mod's own handling of unloaded ground takes over. It is never followed past
        /// the window: that is what keeps an anchor from driving across the map behind a train.
        /// </summary>
        /// <param name="guests">How many groups are standing on the anchor's ground right now.</param>
        public static HashSet<long> Compute(IReadOnlyCollection<long> baseColumns, IReadOnlyCollection<long> previousHalo,
            IEnumerable<IReadOnlyCollection<long>> groups, int anchorCx, int anchorCz, int windowRadius, out int guests)
        {
            HashSet<long> halo = new HashSet<long>();
            guests = 0;

            if (baseColumns == null || groups == null) return halo;

            foreach (IReadOnlyCollection<long> group in groups)
            {
                if (group == null || group.Count < 2) continue;

                bool touches = false;

                foreach (long key in group)
                {
                    if (baseColumns.Contains(key) || (previousHalo != null && previousHalo.Contains(key)))
                    {
                        touches = true;
                        break;
                    }
                }

                if (!touches) continue;

                guests++;

                foreach (long key in group)
                {
                    if (baseColumns.Contains(key)) continue;

                    (int x, int z) = AnchorArea.Of(key);
                    if (!AnchorArea.InWindow(x, z, anchorCx, anchorCz, windowRadius)) continue;

                    halo.Add(key);
                }
            }

            return halo;
        }
    }
}
