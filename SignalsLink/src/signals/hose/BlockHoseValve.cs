using System;
using System.Collections.Generic;
using signals.src.signalNetwork;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.hose
{
    /// <summary>
    /// Valve — derives from the Signals <c>BlockConnection</c> for its Signals anchors
    /// (index 0 = Input, index 1 = Output, wire), and additionally implements <c>IHoseAnchor</c>
    /// for the hose anchor (index 2). The Signals selection boxes + anchors are handled by
    /// <c>BlockConnection</c>; the hose anchor boxes are appended after them.
    /// </summary>
    public class BlockHoseValve : BlockConnection, IHoseAnchor
    {
        protected HoseAnchor[] hoseAnchors = Array.Empty<HoseAnchor>();

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            hoseAnchors = HoseAnchorUtil.Parse(Attributes, api, Code);
        }

        // Ceiling placement is not allowed — the valve mounts on a wall or the floor only.
        public override bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref string failureCode)
        {
            if (Variant["side"] == "up")
            {
                failureCode = "signalslink:hosevalve-noceiling";
                return false;
            }
            return base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode);
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
            if (hoseAnchors.Length == 0) return baseBoxes;

            // Order the boxes so ALL connectors come first and the block body is LAST:
            // [Signals anchors..., hose anchors..., body...]. BlockBehaviorPaperConditions shows
            // its tooltip only on box indices >= SignalInputsCount, i.e. on the body — so the
            // connectors must precede the body (same layout as ManagedChute).
            int wireCount = wireAnchors?.Length ?? 0;
            List<Cuboidf> boxes = new List<Cuboidf>(baseBoxes.Length + hoseAnchors.Length);
            for (int i = 0; i < wireCount && i < baseBoxes.Length; i++) boxes.Add(baseBoxes[i]); // Signals anchors
            foreach (HoseAnchor a in hoseAnchors) boxes.Add(a.RotatedCopy());                    // hose anchors
            for (int i = wireCount; i < baseBoxes.Length; i++) boxes.Add(baseBoxes[i]);          // body last
            return boxes.ToArray();
        }

        public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
        {
            base.OnBlockRemoved(world, pos);                               // Signals wires
            api.ModLoader.GetModSystem<HoseNetworkMod>()?.RemoveAllAt(pos); // hoses
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            // Handle the hose anchor before the Signals wire.
            PlacingHosesMod hoseMod = api.ModLoader.GetModSystem<PlacingHosesMod>();
            if (hoseMod != null)
            {
                NodePos hpos = GetNodePosForHose(world, blockSel, hoseMod.GetPendingNode());
                if (hpos != null && CanAttachHose(world, hpos, hoseMod.GetPendingNode()) && hoseMod.ConnectHose(hpos, byPlayer, this))
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
            // Hose boxes sit right after the Signals anchor boxes (see GetSelectionBoxes order).
            int wireCount = wireAnchors?.Length ?? 0;
            int idx = blockSel.SelectionBoxIndex;
            if (idx >= wireCount && idx < wireCount + hoseAnchors.Length)
            {
                HoseAnchor a = hoseAnchors[idx - wireCount];
                return new NodePos(blockSel.Position, a.Index);
            }
            return null;
        }

        public bool CanAttachHose(IWorldAccessor world, NodePos pos, NodePos posInit = null) => true;

        public bool AllowsMultipleHoses(NodePos pos) => false;

        public NodePos[] GetHoseAnchors(IWorldAccessor world, BlockPos pos) => HoseAnchorUtil.GetHoseAnchors(hoseAnchors, pos);
        #endregion
    }
}
