using System;
using System.Collections.Generic;
using signals.src.signalNetwork;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using SignalsLink.src.signals.link;

namespace SignalsLink.src.signals.hose
{
    /// <summary>
    /// Valve — derives from the Signals <c>BlockConnection</c> for its Signals anchors
    /// (index 0 = Input, index 1 = Output, wire), and additionally implements <c>ILinkAnchor</c>
    /// for the hose anchor (index 2). The Signals selection boxes + anchors are handled by
    /// <c>BlockConnection</c>; the hose anchor boxes are appended after them.
    /// </summary>
    public class BlockHoseValve : BlockConnection, ILinkAnchor
    {
        protected LinkAnchor[] linkAnchors = Array.Empty<LinkAnchor>();

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            linkAnchors = LinkAnchorUtil.Parse(Attributes, api, Code);
        }

        // Always pick/drop the canonical valve item, regardless of the current mount state
        // (hung/stand/drain are three separate block codes).
        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            Block canonical = world.GetBlock(new AssetLocation("signalslink", "hosevalve-north-down"));
            return new ItemStack(canonical ?? this);
        }

        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);
            (world.BlockAccessor.GetBlockEntity(pos) as BlockEntityHoseValve)?.OnNeighbourChanged(neibpos);
        }

        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor world, BlockPos pos)
        {
            // base (BlockConnection) = [Signals anchors..., block body...].
            Cuboidf[] baseBoxes = base.GetSelectionBoxes(world, pos);
            if (linkAnchors.Length == 0) return baseBoxes;

            // Order the boxes so ALL connectors come first and the block body is LAST:
            // [Signals anchors..., hose anchors..., body...]. BlockBehaviorPaperConditions shows
            // its tooltip only on box indices >= SignalInputsCount, i.e. on the body — so the
            // connectors must precede the body (same layout as ManagedChute).
            int wireCount = wireAnchors?.Length ?? 0;
            List<Cuboidf> boxes = new List<Cuboidf>(baseBoxes.Length + linkAnchors.Length);
            for (int i = 0; i < wireCount && i < baseBoxes.Length; i++) boxes.Add(baseBoxes[i]); // Signals anchors
            foreach (LinkAnchor a in linkAnchors) boxes.Add(a.RotatedCopy());                    // hose anchors
            for (int i = wireCount; i < baseBoxes.Length; i++) boxes.Add(baseBoxes[i]);          // body last
            return boxes.ToArray();
        }

        public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
        {
            if (forPlayer?.CurrentBlockSelection?.SelectionBoxIndex == BlockEntityHoseValve.HOSE)
            {
                return Lang.Get("signalslink:con-hose");
            }

            return base.GetPlacedBlockInfo(world, pos, forPlayer);
        }

        public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
        {
            base.OnBlockRemoved(world, pos);                               // Signals wires
            api.ModLoader.GetModSystem<LinkNetworkMod>()?.RemoveAllAt(pos); // hoses
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            // Handle the hose anchor before the Signals wire.
            PlacingLinksMod linkMod = api.ModLoader.GetModSystem<PlacingLinksMod>();
            if (linkMod != null)
            {
                NodePos hpos = GetNodePosForLink(world, blockSel, linkMod.GetPendingNode());
                if (hpos != null && CanAttachLink(world, hpos, linkMod.GetPendingNode()) && linkMod.ConnectLink(hpos, byPlayer, this))
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
            // Hose boxes sit right after the Signals anchor boxes (see GetSelectionBoxes order).
            int wireCount = wireAnchors?.Length ?? 0;
            int idx = blockSel.SelectionBoxIndex;
            if (idx >= wireCount && idx < wireCount + linkAnchors.Length)
            {
                LinkAnchor a = linkAnchors[idx - wireCount];
                return new NodePos(blockSel.Position, a.Index);
            }
            return null;
        }

        public bool CanAttachLink(IWorldAccessor world, NodePos pos, NodePos posInit = null) => true;

        // The valve accepts several hoses on its single anchor: it pumps from all connected
        // sources in turn (see BlockEntityHoseValve, round-robin cursor).
        public bool AllowsMultipleLinks(NodePos pos) => true;

        public NodePos[] GetLinkAnchors(IWorldAccessor world, BlockPos pos) => LinkAnchorUtil.GetLinkAnchors(linkAnchors, pos);

        public byte AcceptedLinkKind => LinkKind.Hose;
        #endregion
    }
}
