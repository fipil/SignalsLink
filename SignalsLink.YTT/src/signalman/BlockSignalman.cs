using signals.src.signalNetwork;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.YTT.src.signalman
{
    /// <summary>
    /// The signalman block: a plate that goes on the ground only, turned by the Signals cover
    /// behavior like the stoker (ghost preview, direction from where on the top face the player
    /// aims; the side is always <c>down</c>), with its two pins on the strip at the back. A click
    /// on a pin is a wire; there is nothing else to click.
    /// </summary>
    public class BlockSignalman : BlockConnection
    {
        /// <summary>Selection boxes below this belong to the pins.</summary>
        public int PinBoxes => Attributes?["signalPins"].AsInt(0) ?? 0;

        private ItemStack Idle(IWorldAccessor world)
        {
            Block idle = world.GetBlock(CodeWithVariants(
                new[] { "mode", "orientation", "side" }, new[] { "both", "north", "down" }));

            return new ItemStack(idle ?? this);
        }

        private static bool IsWrench(ItemSlot slot)
        {
            AssetLocation code = slot?.Itemstack?.Collectible?.Code;

            return code != null && code.Path.StartsWith("wrench", System.StringComparison.Ordinal);
        }

        /// <summary>A pin is a wire; a wrench turns the mode. The block gets the click before the item in hand.</summary>
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (blockSel?.Position == null || blockSel.SelectionBoxIndex < PinBoxes)
            {
                return base.OnBlockInteractStart(world, byPlayer, blockSel);
            }

            if (IsWrench(byPlayer?.InventoryManager?.ActiveHotbarSlot)
                && world.BlockAccessor.GetBlockEntity(blockSel.Position) is BESignalman signalman)
            {
                if (world.Side == EnumAppSide.Server) signalman.CycleMode();
                return true;
            }

            return base.OnBlockInteractStart(world, byPlayer, blockSel);
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

        /// <summary>On the ground only: it lies at the edge of the block next to the track.</summary>
        public override bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref string failureCode)
        {
            if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode)) return false;

            BlockPos below = blockSel.Position.DownCopy();
            Block ground = world.BlockAccessor.GetBlock(below);

            if (ground == null || !ground.CanAttachBlockAt(world.BlockAccessor, this, below, BlockFacing.UP))
            {
                failureCode = "requiresolidground";
                return false;
            }

            return true;
        }

        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);

            // The ground went: so does the plate, like a torch.
            if (neibpos.Y != pos.Y - 1) return;

            Block ground = world.BlockAccessor.GetBlock(neibpos);
            if (ground != null && ground.CanAttachBlockAt(world.BlockAccessor, this, neibpos, BlockFacing.UP)) return;

            world.BlockAccessor.BreakBlock(pos, null);
        }
    }
}
