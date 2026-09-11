using signals.src.hangingwires;
using SignalsLink.src.signals.paperConditions;
using signals.src.signalNetwork;
using signals.src.transmission;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.manageddock
{
    /// <summary>
    /// The freight dock: a crate with two signal anchors, which loads and unloads whatever is
    /// standing around it.
    ///
    /// <b>It is a BlockConnection</b>, like every other device here with anchors on it. That is what
    /// turns the <c>signalNodes</c> in the block's attributes into selection boxes a wire can be
    /// hooked to - as a plain Block the anchors were drawn and could not be clicked, and the
    /// tooltips landed on the wrong boxes, because the node boxes were never in the list at all.
    /// The node boxes come first, which is what <c>SignalInputsCount</c> counts.
    ///
    /// It is set down turned, the way a crate or a chest is, and it turns its working corner - the
    /// one carrying the anchors - towards whoever put it there. Reaching round the back of a crate
    /// to plug a wire in would be a poor joke.
    /// </summary>
    public class BlockManagedDock : BlockConnection
    {
        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            if (!base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode)) return false;

            BlockFacing[] horizontal = SuggestedHVOrientation(byPlayer, blockSel);
            BlockFacing facing = horizontal != null && horizontal.Length > 0 && horizontal[0] != null
                ? horizontal[0]
                : BlockFacing.NORTH;

            Block oriented = world.BlockAccessor.GetBlock(CodeWithVariant("side", facing.Code));
            if (oriented != null) world.BlockAccessor.ExchangeBlock(oriented.BlockId, blockSel.Position);

            return true;
        }

        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);

            // Somebody laid or broke a block next door - it may well be the paving.
            (world.BlockAccessor.GetBlockEntity(pos) as BEManagedDock)?.OnNeighbourBlockChange(neibpos);
        }

        /// <summary>
        /// A click on the dock can mean three things, and they have to be tried in this order. The
        /// first two are the workaround for the Signals BlockConnection that the sensor and the
        /// igniter carry as well.
        ///
        /// <b>A wire first</b>, when one is being drawn: a click that landed on an anchor was aimed
        /// at the anchor, and it has to be swallowed even when no wire ends up attached. Then the
        /// behaviors, where paper conditions live. The crate last, which is what an empty hand means.
        /// </summary>
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            PlacingWiresMod wires = api.ModLoader.GetModSystem<PlacingWiresMod>();

            if (wires != null)
            {
                NodePos node = GetNodePosForWire(world, blockSel, wires.GetPendingNode());

                if (node != null && CanAttachWire(world, node, wires.GetPendingNode()))
                {
                    wires.ConnectWire(node, byPlayer, this);
                    return false;
                }
            }

            foreach (BlockBehavior behavior in BlockBehaviors)
            {
                EnumHandling handling = EnumHandling.PassThrough;
                bool result = behavior.OnBlockInteractStart(world, byPlayer, blockSel, ref handling);

                if (handling == EnumHandling.PreventDefault) return result;
            }

            // Paper in hand means the click is about the paper even when the behavior declined it
            // - otherwise opening the crate swallows the click.
            if (IsAboutPaper(byPlayer)) return true;

            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEManagedDock dock)
            {
                return dock.OnPlayerRightClick(byPlayer, blockSel);
            }

            return false;
        }

        /// <summary>
        /// Is this click the paper's business? Either the player is holding paper, or they are
        /// holding Ctrl - which is how an item's own description is copied onto the device.
        /// </summary>
        private static bool IsAboutPaper(IPlayer byPlayer)
        {
            ItemSlot slot = byPlayer?.InventoryManager?.ActiveHotbarSlot;
            if (slot?.Itemstack == null) return false;

            return BlockBehaviorPaperConditions.IsPaperStack(slot.Itemstack)
                || byPlayer.Entity?.Controls?.CtrlKey == true;
        }
    }
}
