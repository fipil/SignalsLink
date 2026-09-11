using System.Collections.Generic;
using SignalsLink.src.signals.cargo;

namespace SignalsLink.src.signals.manageddock
{
    /// <summary>
    /// Which part of one holder is tried against which part of the other. Source-major, so the
    /// nearest source is emptied into the nearest target first. Capped, and empty sources are
    /// skipped free.
    /// </summary>
    public static class DockPairs
    {
        public static IEnumerable<(ICargoHold Source, ICargoHold Target)> Order(
            IReadOnlyList<ICargoHold> sources, IReadOnlyList<ICargoHold> targets, int maxAttempts)
        {
            if (sources == null || targets == null || targets.Count == 0) yield break;

            int attempts = 0;

            foreach (ICargoHold source in sources)
            {
                // Not counted against the ceiling, or a yard with an empty near end would
                // never reach the goods behind it.
                if (source == null || source.IsEmpty) continue;

                foreach (ICargoHold target in targets)
                {
                    if (target == null) continue;
                    if (attempts >= maxAttempts) yield break;

                    attempts++;
                    yield return (source, target);
                }
            }
        }
    }
}
