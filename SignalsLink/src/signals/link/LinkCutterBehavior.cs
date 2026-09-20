using signals.src.signalNetwork;
using Vintagestory.API.Common;

namespace SignalsLink.src.signals.link
{
    /// <summary>
    /// Lets shears cut a link segment, mirroring the Signals <c>WireCutterBehavior</c> for wires.
    /// Click one link anchor, then the other end of the same segment — the connection is removed
    /// and the line item is returned. One pair of shears handles both hoses and sleeves; the kind
    /// comes from the connection itself. Attached to shears via a JSON patch.
    ///
    /// On a valve (which is both a wire anchor and a link anchor) the two cutter behaviors don't
    /// clash: each resolves its node only from its own selection boxes, so <see cref="ILinkAnchor.GetNodePosForLink"/>
    /// returns null for wire boxes and the wire cutter's lookup returns null for link boxes.
    /// </summary>
    public class LinkCutterBehavior : CollectibleBehavior
    {
        private NodePos pendingNode;
        private LinkNetworkMod linkMod;
        private ICoreAPI api;

        public LinkCutterBehavior(CollectibleObject collObj) : base(collObj) { }

        public override void OnLoaded(ICoreAPI api)
        {
            this.api = api;
            linkMod = api.ModLoader.GetModSystem<LinkNetworkMod>();
        }

        public override void OnHeldInteractStart(ItemSlot itemslot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling, ref EnumHandling handling)
        {
            base.OnHeldInteractStart(itemslot, byEntity, blockSel, entitySel, firstEvent, ref handHandling, ref handling);

            if (blockSel == null) return; // e.g. clicking an entity

            ILinkAnchor anchor = byEntity.World.BlockAccessor.GetBlock(blockSel.Position) as ILinkAnchor;
            NodePos pos = anchor?.GetNodePosForLink(byEntity.World, blockSel, pendingNode);
            if (pos == null) return; // not a link anchor box → let the wire cutter / default handle it

            if (pendingNode == null)
            {
                pendingNode = pos;
            }
            else
            {
                if (api.Side == EnumAppSide.Server)
                {
                    linkMod.CutLink(byEntity, pos, pendingNode);
                }
                pendingNode = null;
            }

            handHandling = EnumHandHandling.PreventDefault;
        }
    }
}
