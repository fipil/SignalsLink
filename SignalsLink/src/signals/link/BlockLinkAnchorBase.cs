using System.Collections.Generic;
using signals.src.signalNetwork;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.link
{
    /// <summary>
    /// Base for blocks that only have link anchors (Coupling, Intake). Mirror of the Signals
    /// <c>BlockConnection</c>, but for the link network — anchors are read from
    /// <c>attributes.linkNodes</c>, placement is handled by <c>PlacingLinksMod</c>, and block
    /// removal is cleaned up by <c>LinkNetworkMod.RemoveAllAt</c>. (The Valve has its own
    /// <c>BlockHoseValve</c> because it derives from the Signals <c>BlockConnection</c>.)
    /// </summary>
    public abstract class BlockLinkAnchorBase : Block, ILinkAnchor
    {
        protected LinkAnchor[] linkAnchors = System.Array.Empty<LinkAnchor>();

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            linkAnchors = LinkAnchorUtil.Parse(Attributes, api, Code);
        }

        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor world, BlockPos pos)
        {
            List<Cuboidf> boxes = new List<Cuboidf>();
            foreach (LinkAnchor a in linkAnchors) boxes.Add(a.RotatedCopy());
            boxes.AddRange(base.GetSelectionBoxes(world, pos));
            return boxes.ToArray();
        }

        public override bool DoPartialSelection(IWorldAccessor world, BlockPos pos) => true;

        public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
        {
            int? selectionBoxIndex = forPlayer?.CurrentBlockSelection?.SelectionBoxIndex;
            if (selectionBoxIndex != null)
            {
                foreach (LinkAnchor anchor in linkAnchors)
                {
                    if (anchor.Index == selectionBoxIndex)
                    {
                        // Kept hose-worded until the sleeve gets its own key; the lang files are
                        // translated into ten languages and this step changes no wording.
                        return Lang.Get("signalslink:con-hose");
                    }
                }
            }

            return base.GetPlacedBlockInfo(world, pos, forPlayer);
        }

        public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
        {
            base.OnBlockRemoved(world, pos);
            api.ModLoader.GetModSystem<LinkNetworkMod>()?.RemoveAllAt(pos);
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            PlacingLinksMod mod = api.ModLoader.GetModSystem<PlacingLinksMod>();
            if (mod != null)
            {
                NodePos pos = GetNodePosForLink(world, blockSel, mod.GetPendingNode());
                if (pos != null && CanAttachLink(world, pos, mod.GetPendingNode()) && mod.ConnectLink(pos, byPlayer, this))
                {
                    return false;
                }
            }
            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        #region ILinkAnchor
        public Vec3f GetLinkAnchorPosInBlock(NodePos pos) => LinkAnchorUtil.GetAnchorPosInBlock(linkAnchors, pos.index);

        public NodePos GetNodePosForLink(IWorldAccessor world, BlockSelection blockSel, NodePos posInit = null)
        {
            foreach (LinkAnchor box in linkAnchors)
            {
                if (box.Index == blockSel.SelectionBoxIndex) return new NodePos(blockSel.Position, blockSel.SelectionBoxIndex);
            }
            return null;
        }

        public bool CanAttachLink(IWorldAccessor world, NodePos pos, NodePos posInit = null) => true;

        public virtual bool AllowsMultipleLinks(NodePos pos) => false;

        public NodePos[] GetLinkAnchors(IWorldAccessor world, BlockPos pos) => LinkAnchorUtil.GetLinkAnchors(linkAnchors, pos);

        public virtual byte AcceptedLinkKind => LinkKind.Hose;
        #endregion
    }
}
