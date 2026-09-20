using System;
using System.Collections.Generic;
using signals.src.signalNetwork;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.link
{
    /// <summary>
    /// Base for an endpoint block that carries BOTH Signals wire anchors and exactly one link
    /// anchor — the Hose Valve and the Sleeve Damper. It derives from the Signals
    /// <c>BlockConnection</c> for the wire anchors (index 0 = Input, 1 = Output) and implements
    /// <see cref="ILinkAnchor"/> for the line anchor that follows them.
    ///
    /// Everything here is kind-agnostic; a subclass only names its canonical block code, its
    /// accepted <see cref="LinkKind"/> and the lang key shown on its anchor.
    /// </summary>
    public abstract class BlockLinkEndpointBase : BlockConnection, ILinkAnchor
    {
        protected LinkAnchor[] linkAnchors = Array.Empty<LinkAnchor>();

        /// <summary>The single block code this endpoint is picked up and dropped as, whatever mount
        /// variant it currently wears (hung / stand / drain / roof are separate block codes).</summary>
        protected abstract AssetLocation CanonicalCode { get; }

        /// <summary>Lang key shown in the tooltip when the player looks at the line anchor.</summary>
        protected abstract string AnchorLangKey { get; }

        /// <summary>
        /// Which way the line leaves this endpoint when it is mounted on the floor (side = down).
        ///
        /// False, the normal case: straight up, along the mount normal — the mirror image of a
        /// ceiling mount, whose line leaves straight down. True only for something that lies on
        /// the ground and points at the block beside it, which is what a drain does.
        /// </summary>
        public virtual bool FloorMountExitsSideways => false;

        public abstract byte AcceptedLinkKind { get; }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            linkAnchors = LinkAnchorUtil.Parse(Attributes, api, Code);
        }

        // Always pick/drop the canonical item, regardless of the current mount state.
        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            Block canonical = world.GetBlock(CanonicalCode);
            return new ItemStack(canonical ?? this);
        }

        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);
            (world.BlockAccessor.GetBlockEntity(pos) as ILinkMountHost)?.OnNeighbourChanged(neibpos);
        }

        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor world, BlockPos pos)
        {
            // base (BlockConnection) = [Signals anchors..., block body...].
            Cuboidf[] baseBoxes = base.GetSelectionBoxes(world, pos);
            if (linkAnchors.Length == 0) return baseBoxes;

            // Order the boxes so ALL connectors come first and the block body is LAST:
            // [Signals anchors..., link anchors..., body...]. BlockBehaviorPaperConditions shows
            // its tooltip only on box indices >= SignalInputsCount, i.e. on the body — so the
            // connectors must precede the body (same layout as ManagedChute).
            int wireCount = wireAnchors?.Length ?? 0;
            List<Cuboidf> boxes = new List<Cuboidf>(baseBoxes.Length + linkAnchors.Length);
            for (int i = 0; i < wireCount && i < baseBoxes.Length; i++) boxes.Add(baseBoxes[i]); // Signals anchors
            foreach (LinkAnchor a in linkAnchors) boxes.Add(a.RotatedCopy());                    // link anchors
            for (int i = wireCount; i < baseBoxes.Length; i++) boxes.Add(baseBoxes[i]);          // body last
            return boxes.ToArray();
        }

        public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
        {
            int? index = forPlayer?.CurrentBlockSelection?.SelectionBoxIndex;
            if (index != null)
            {
                int wireCount = wireAnchors?.Length ?? 0;
                if (index >= wireCount && index < wireCount + linkAnchors.Length)
                {
                    return Lang.Get(AnchorLangKey);
                }
            }

            return base.GetPlacedBlockInfo(world, pos, forPlayer);
        }

        public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
        {
            base.OnBlockRemoved(world, pos);                                // Signals wires
            api.ModLoader.GetModSystem<LinkNetworkMod>()?.RemoveAllAt(pos); // hoses / sleeves
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            // Handle the line anchor before the Signals wire.
            PlacingLinksMod linkMod = api.ModLoader.GetModSystem<PlacingLinksMod>();
            if (linkMod != null)
            {
                NodePos lpos = GetNodePosForLink(world, blockSel, linkMod.GetPendingNode());
                if (lpos != null && CanAttachLink(world, lpos, linkMod.GetPendingNode()) && linkMod.ConnectLink(lpos, byPlayer, this))
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
            // Link boxes sit right after the Signals anchor boxes (see GetSelectionBoxes order).
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

        // An endpoint accepts several lines on its single anchor: it pulls from all connected
        // sources in turn (round-robin, see the block entity).
        public virtual bool AllowsMultipleLinks(NodePos pos) => true;

        public NodePos[] GetLinkAnchors(IWorldAccessor world, BlockPos pos) => LinkAnchorUtil.GetLinkAnchors(linkAnchors, pos);
        #endregion
    }

    /// <summary>
    /// A block entity that changes its mount variant when a neighbour appears or disappears
    /// (host present / absent). Implemented by the valve and the damper.
    /// </summary>
    public interface ILinkMountHost
    {
        void OnNeighbourChanged(BlockPos neibpos);
    }
}
