using signals.src.signalNetwork;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.link
{
    /// <summary>
    /// An anchor a hose or a sleeve can attach to (Valve / Coupling / Intake / Damper). Mirror of
    /// the Signals <c>IHangingWireAnchor</c>, but for the link network — which is independent of
    /// the signal network. Uses <c>NodePos</c> (blockPos + anchor index) from Signals so the
    /// selection-box / anchor machinery can be shared; a link, however, carries no signal.
    /// </summary>
    public interface ILinkAnchor
    {
        /// <summary>Center of the anchor within the block (0..1), used for link rendering.</summary>
        Vec3f GetLinkAnchorPosInBlock(NodePos pos);

        /// <summary>The NodePos of the link anchor under the given selection box, or null.</summary>
        NodePos GetNodePosForLink(IWorldAccessor world, BlockSelection blockSel, NodePos posInit = null);

        /// <summary>May a link attach to this anchor? (Typically true; occupancy is enforced by the network.)</summary>
        bool CanAttachLink(IWorldAccessor world, NodePos pos, NodePos posInit = null);

        /// <summary>
        /// If true, this anchor accepts any number of links (the "max 1 link per anchor" rule is
        /// waived for it). Used by the Intake (one source feeding many targets) and by the Valve
        /// (one target pumping from many sources in turn). Couplings stay strictly pass-through.
        /// </summary>
        bool AllowsMultipleLinks(NodePos pos);

        /// <summary>All link anchors of the block at the given position.</summary>
        NodePos[] GetLinkAnchors(IWorldAccessor world, BlockPos pos);

        /// <summary>
        /// Which kind of line may attach here (see <see cref="LinkKind"/>). Anchors of different
        /// kinds cannot be joined, and a player holding the wrong line is refused.
        /// </summary>
        byte AcceptedLinkKind { get; }
    }
}
