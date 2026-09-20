using SignalsLink.src.signals.link;

namespace SignalsLink.src.signals.sleeve
{
    /// <summary>
    /// Sleeve Coupling — merely extends a sleeve run (2 sleeve anchors, no logic). Deliberately a
    /// separate block from the hose coupling: square and copper, so a run of sleeves is told apart
    /// from a run of hoses at a glance.
    /// </summary>
    public class BlockSleeveCoupling : BlockLinkAnchorBase
    {
        public override byte AcceptedLinkKind => LinkKind.Sleeve;
    }
}
