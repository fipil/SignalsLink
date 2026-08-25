using System.Collections.Generic;
using signals.src.signalNetwork;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.hose
{
    /// <summary>
    /// Base for blocks that only have hose anchors (Coupling, Intake). Mirror of the Signals
    /// <c>BlockConnection</c>, but for the hose network — anchors are read from
    /// <c>attributes.hoseNodes</c>, placement is handled by <c>PlacingHosesMod</c>, and block
    /// removal is cleaned up by <c>HoseNetworkMod.RemoveAllAt</c>. (The Valve has its own
    /// <c>BlockHoseValve</c> because it derives from the Signals <c>BlockConnection</c>.)
    /// </summary>
    public abstract class BlockHoseAnchorBase : Block, IHoseAnchor
    {
        protected HoseAnchor[] hoseAnchors = System.Array.Empty<HoseAnchor>();

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            hoseAnchors = HoseAnchorUtil.Parse(Attributes, api, Code);
        }

        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor world, BlockPos pos)
        {
            List<Cuboidf> boxes = new List<Cuboidf>();
            foreach (HoseAnchor a in hoseAnchors) boxes.Add(a.RotatedCopy());
            boxes.AddRange(base.GetSelectionBoxes(world, pos));
            return boxes.ToArray();
        }

        public override bool DoPartialSelection(IWorldAccessor world, BlockPos pos) => true;

        public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
        {
            int? selectionBoxIndex = forPlayer?.CurrentBlockSelection?.SelectionBoxIndex;
            if (selectionBoxIndex != null)
            {
                foreach (HoseAnchor anchor in hoseAnchors)
                {
                    if (anchor.Index == selectionBoxIndex)
                    {
                        return Lang.Get("signalslink:con-hose");
                    }
                }
            }

            return base.GetPlacedBlockInfo(world, pos, forPlayer);
        }

        public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
        {
            base.OnBlockRemoved(world, pos);
            api.ModLoader.GetModSystem<HoseNetworkMod>()?.RemoveAllAt(pos);
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            PlacingHosesMod mod = api.ModLoader.GetModSystem<PlacingHosesMod>();
            if (mod != null)
            {
                NodePos pos = GetNodePosForHose(world, blockSel, mod.GetPendingNode());
                if (pos != null && CanAttachHose(world, pos, mod.GetPendingNode()) && mod.ConnectHose(pos, byPlayer, this))
                {
                    return false;
                }
            }
            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        #region IHoseAnchor
        public Vec3f GetHoseAnchorPosInBlock(NodePos pos) => HoseAnchorUtil.GetAnchorPosInBlock(hoseAnchors, pos.index);

        public NodePos GetNodePosForHose(IWorldAccessor world, BlockSelection blockSel, NodePos posInit = null)
        {
            foreach (HoseAnchor box in hoseAnchors)
            {
                if (box.Index == blockSel.SelectionBoxIndex) return new NodePos(blockSel.Position, blockSel.SelectionBoxIndex);
            }
            return null;
        }

        public bool CanAttachHose(IWorldAccessor world, NodePos pos, NodePos posInit = null) => true;

        public virtual bool AllowsMultipleHoses(NodePos pos) => false;

        public NodePos[] GetHoseAnchors(IWorldAccessor world, BlockPos pos) => HoseAnchorUtil.GetHoseAnchors(hoseAnchors, pos);
        #endregion
    }
}
