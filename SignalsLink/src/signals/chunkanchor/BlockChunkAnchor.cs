using Vintagestory.API.Common;

namespace SignalsLink.src.signals.chunkanchor
{
    /// <summary>
    /// The anchor block. Right click opens its map.
    ///
    /// It will grow two signal pins, and then the click has to be shared: a click on a pin belongs
    /// to Signals, so that the player can wire the thing, and only a click on the body opens the
    /// map. That split is written now, while there is nothing to get wrong, because the alternative
    /// is a block that cannot be wired because a dialog keeps jumping out from under the wire.
    /// </summary>
    public class BlockChunkAnchor : Block
    {
        /// <summary>Selection boxes below this belong to the pins; -1 while there are none.</summary>
        public int PinBoxes => Attributes?["signalPins"].AsInt(0) ?? 0;

        /// <summary>
        /// Contains, not equals: the charge behaviour looks the item up the same way, and the two
        /// have to agree or the block would take a gear the anchor then refuses.
        /// </summary>
        private bool IsGear(ItemSlot slot)
        {
            string code = GetBehavior<behaviours.BlockBehaviorTemporalCharge>()?.ChargeItemCode;
            string path = slot?.Itemstack?.Collectible?.Code?.Path;

            return code != null && path != null && path.Contains(code);
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (blockSel?.Position == null) return base.OnBlockInteractStart(world, byPlayer, blockSel);

            if (blockSel.SelectionBoxIndex < PinBoxes)
            {
                return base.OnBlockInteractStart(world, byPlayer, blockSel);
            }

            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BEChunkAnchor anchor)
            {
                return base.OnBlockInteractStart(world, byPlayer, blockSel);
            }

            // A gear in hand feeds it; an empty hand opens the map. The same split the sensor and
            // the igniter make, so a player who has charged one of those already knows this one.
            ItemSlot hand = byPlayer?.InventoryManager?.ActiveHotbarSlot;

            if (IsGear(hand))
            {
                if (world.Side == EnumAppSide.Server) anchor.TryFeed(hand);
                return true;
            }

            if (world.Side == EnumAppSide.Client) anchor.OpenMap();

            return true;
        }
    }
}
