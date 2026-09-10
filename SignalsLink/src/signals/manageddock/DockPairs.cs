using System.Collections.Generic;
using SignalsLink.src.signals.cargo;

namespace SignalsLink.src.signals.manageddock
{
    /// <summary>
    /// Which part of one holder is tried against which part of the other, and in what order.
    ///
    /// With a holder at both ends there is no single obvious pair to work on any more, and two
    /// things have to be right.
    ///
    /// <b>Source-major order.</b> The nearest part of the source is emptied into the nearest part
    /// of the target before anything further out is touched. That one rule gives "empty the near
    /// wagon first" and "fill the near column to its height before starting another" — the same
    /// rule that made the composed inventory work, now applied to both ends at once.
    ///
    /// <b>A ceiling on the work.</b> Four hundred columns against a ten wagon train is four
    /// thousand attempts, and a tick has to end whether or not anything was found. Sources with
    /// nothing in them are skipped before they cost anything, and what remains is capped.
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
                // Skipping does not count against the ceiling: most of a yard is empty most of the
                // time, and a dock whose near end happens to be empty would otherwise spend every
                // tick walking past it and never reach the goods behind.
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
