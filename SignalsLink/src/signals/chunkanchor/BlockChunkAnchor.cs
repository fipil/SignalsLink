using signals.src.signalNetwork;
using Vintagestory.API.MathTools;
using Vintagestory.API.Common;

namespace SignalsLink.src.signals.chunkanchor
{
    /// <summary>
    /// The anchor block. Right click opens its map; a click on one of its two pins is a wire.
    ///
    /// <b>A BlockConnection, not a plain Block</b>, like every other device here with anchors on
    /// it. That is what turns the <c>signalNodes</c> in the block's attributes into selection boxes
    /// a wire can be hooked to, and it puts those boxes FIRST - which is what the pin count below
    /// counts on.
    ///
    /// It turns in the four world directions through a VARIANT, the way the chute and the valve do,
    /// and not through a mesh angle the way the dock tries to. That distinction is the whole
    /// reason the dock cannot be turned today: Signals reads its anchor positions out of the
    /// block's attributes rather than from the selection boxes, so a freely turned block leaves
    /// its wires hanging in mid-air. A variant is a different block carrying its own
    /// already-rotated attributes, so the two have no way to disagree - which is why every rotated
    /// chute in the world wires up correctly.
    /// </summary>
    public class BlockChunkAnchor : BlockConnection
    {
        /// <summary>Selection boxes below this belong to the pins; -1 while there are none.</summary>
        public int PinBoxes => Attributes?["signalPins"].AsInt(0) ?? 0;

        /// <summary>
        /// Always handed over as the idle variant facing north.
        ///
        /// The turned and the lit variants are how the block LOOKS where it stands; carrying four
        /// directions and two states around as eight different items in the inventory would be
        /// eight ways to say the same thing. The behavior sets the direction again on placement,
        /// and the block entity lights it when it starts holding.
        /// </summary>
        private ItemStack Idle(IWorldAccessor world)
        {
            Block idle = world.GetBlock(CodeWithVariants(
                new[] { "state", "side" }, new[] { "off", "north" }));

            return new ItemStack(idle ?? this);
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) => Idle(world);

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            return new[] { Idle(world) };
        }

        /// <summary>
        /// Match the charge item exactly, as the inventory slot does.
        /// </summary>
        private bool IsGear(ItemSlot slot)
        {
            string code = GetBehavior<behaviours.BlockBehaviorTemporalCharge>()?.ChargeItemCode;
            string path = slot?.Itemstack?.Collectible?.Code?.Path;

            return code != null && path != null && path == code;
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
