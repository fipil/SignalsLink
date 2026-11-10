using signals.src.signalNetwork;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.YTT.src.stoker
{
    /// <summary>
    /// The stoker block. A click on a pin is a wire, a wrench sets the reach, anything else opens
    /// its slots. The wrench is recognised HERE: the game hands a click to the block before the
    /// item in hand, so a block that opens on every click never lets the wrench act.
    ///
    /// A BlockConnection turned by VARIANT, like the anchor: Signals reads the pin positions out
    /// of the block's attributes, so each direction is a block of its own with already-rotated
    /// attributes. Turned by the Signals cover behavior, which draws the ghost and takes the
    /// direction from where on the top face the player aims; that behavior wants an
    /// <c>orientation</c> and a <c>side</c>, and the side is always <c>down</c> here, because
    /// the stoker goes on the ground only. The lit variant is how it looks, not what it is, so
    /// the inventory always hands over the idle one facing north.
    /// </summary>
    public class BlockStoker : BlockConnection
    {
        /// <summary>Selection boxes below this belong to the pins.</summary>
        public int PinBoxes => Attributes?["signalPins"].AsInt(0) ?? 0;

        private ItemStack Idle(IWorldAccessor world)
        {
            Block idle = world.GetBlock(CodeWithVariants(
                new[] { "state", "orientation", "side" }, new[] { "off", "north", "down" }));

            return new ItemStack(idle ?? this);
        }

        /// <summary>
        /// On the ground only. Refused before the cover behavior gets to it: for a wall it would
        /// ask for a side variant that does not exist.
        /// </summary>
        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            if (blockSel?.Face != BlockFacing.UP)
            {
                failureCode = "requiresolidground";
                return false;
            }

            return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) => Idle(world);

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            return new[] { Idle(world) };
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (blockSel?.Position == null || blockSel.SelectionBoxIndex < PinBoxes)
            {
                return base.OnBlockInteractStart(world, byPlayer, blockSel);
            }

            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BEStoker stoker)
            {
                return base.OnBlockInteractStart(world, byPlayer, blockSel);
            }

            if (IsWrench(byPlayer?.InventoryManager?.ActiveHotbarSlot))
            {
                if (world.Side == EnumAppSide.Server) stoker.CycleReach();
                return true;
            }

            return stoker.OnPlayerRightClick(byPlayer, blockSel);
        }

        private static bool IsWrench(ItemSlot slot)
        {
            AssetLocation code = slot?.Itemstack?.Collectible?.Code;

            return code != null && code.Path.StartsWith("wrench", System.StringComparison.Ordinal);
        }
    }
}
