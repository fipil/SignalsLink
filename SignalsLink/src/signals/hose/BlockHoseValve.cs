using SignalsLink.src.signals.link;
using Vintagestory.API.Common;

namespace SignalsLink.src.signals.hose
{
    /// <summary>
    /// Valve — the ManagedHose endpoint. All the anchor plumbing (Signals wire anchors 0/1 plus
    /// the hose anchor 2, selection box ordering, placement, removal) lives in
    /// <see cref="BlockLinkEndpointBase"/>; only what makes it a hose is here.
    /// </summary>
    public class BlockHoseValve : BlockLinkEndpointBase
    {
        protected override AssetLocation CanonicalCode => new AssetLocation("signalslink", "hosevalve-north-down");

        protected override string AnchorLangKey => "signalslink:con-hose";

        public override byte AcceptedLinkKind => LinkKind.Hose;

        // A valve on the floor is the drain: it pours sideways into the block it faces, and the
        // hose follows that. A damper on the floor sits on its host instead, so its sleeve leaves
        // straight up.
        public override bool FloorMountExitsSideways => true;
    }
}
