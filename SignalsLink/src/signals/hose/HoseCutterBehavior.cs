using signals.src.signalNetwork;
using Vintagestory.API.Common;

namespace SignalsLink.src.signals.hose
{
    /// <summary>
    /// Lets shears cut a hose segment, mirroring the Signals <c>WireCutterBehavior</c> for wires.
    /// Click one hose anchor, then the other end of the same segment — the connection is removed
    /// and the hose item is returned. Attached to shears via a JSON patch.
    ///
    /// On a valve (which is both a wire anchor and a hose anchor) the two cutter behaviors don't
    /// clash: each resolves its node only from its own selection boxes, so <see cref="IHoseAnchor.GetNodePosForHose"/>
    /// returns null for wire boxes and the wire cutter's lookup returns null for hose boxes.
    /// </summary>
    public class HoseCutterBehavior : CollectibleBehavior
    {
        private NodePos pendingNode;
        private HoseNetworkMod hoseMod;
        private ICoreAPI api;

        public HoseCutterBehavior(CollectibleObject collObj) : base(collObj) { }

        public override void OnLoaded(ICoreAPI api)
        {
            this.api = api;
            hoseMod = api.ModLoader.GetModSystem<HoseNetworkMod>();
        }

        public override void OnHeldInteractStart(ItemSlot itemslot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling, ref EnumHandling handling)
        {
            base.OnHeldInteractStart(itemslot, byEntity, blockSel, entitySel, firstEvent, ref handHandling, ref handling);

            if (blockSel == null) return; // e.g. clicking an entity

            IHoseAnchor anchor = byEntity.World.BlockAccessor.GetBlock(blockSel.Position) as IHoseAnchor;
            NodePos pos = anchor?.GetNodePosForHose(byEntity.World, blockSel, pendingNode);
            if (pos == null) return; // not a hose anchor box → let the wire cutter / default handle it

            if (pendingNode == null)
            {
                pendingNode = pos;
            }
            else
            {
                if (api.Side == EnumAppSide.Server)
                {
                    hoseMod.CutHose(byEntity, pos, pendingNode);
                }
                pendingNode = null;
            }

            handHandling = EnumHandHandling.PreventDefault;
        }
    }
}
