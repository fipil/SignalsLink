using signals.src.signalNetwork;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.hose
{
    /// <summary>
    /// A hose anchor on a block (Valve / Coupling / Intake). Mirror of the Signals
    /// <c>IHangingWireAnchor</c>, but for the ManagedHose network — which is independent of
    /// the signal network. Uses <c>NodePos</c> (blockPos + anchor index) from Signals so the
    /// selection-box / anchor machinery can be shared; a hose, however, carries no signal.
    /// </summary>
    public interface IHoseAnchor
    {
        /// <summary>Center of the anchor within the block (0..1), used for hose rendering.</summary>
        Vec3f GetHoseAnchorPosInBlock(NodePos pos);

        /// <summary>The NodePos of the hose anchor under the given selection box, or null.</summary>
        NodePos GetNodePosForHose(IWorldAccessor world, BlockSelection blockSel, NodePos posInit = null);

        /// <summary>May a hose attach to this anchor? (Typically true; occupancy is enforced by the network.)</summary>
        bool CanAttachHose(IWorldAccessor world, NodePos pos, NodePos posInit = null);

        /// <summary>
        /// If true, this anchor accepts any number of hoses (the "max 1 hose per anchor" rule is
        /// waived for it). Used by the Intake, which is a source feeding many targets.
        /// </summary>
        bool AllowsMultipleHoses(NodePos pos);

        /// <summary>All hose anchors of the block at the given position.</summary>
        NodePos[] GetHoseAnchors(IWorldAccessor world, BlockPos pos);
    }
}
