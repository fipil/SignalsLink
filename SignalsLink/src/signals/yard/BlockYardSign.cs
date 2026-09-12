using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.yard
{
    /// <summary>
    /// A named plate standing on a storage yard, so a paper can say which yard it means.
    ///
    /// Placed like a vanilla sign: it turns to face the player, and the name is typed into a small
    /// form. No paper or ink — this is a label, not a document.
    /// </summary>
    public class BlockYardSign : Block
    {
        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            if (!base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode)) return false;

            BlockFacing facing = FacingForPlacement(byPlayer, blockSel);

            Block oriented = world.BlockAccessor.GetBlock(CodeWithVariant("side", facing.Code));
            if (oriented != null) world.BlockAccessor.ExchangeBlock(oriented.BlockId, blockSel.Position);

            // Straight into the form: a sign with no name on it is the one thing nobody wants.
            OpenDialog(world, blockSel.Position, byPlayer);
            return true;
        }

        public static BlockFacing FacingForPlacement(IPlayer player, BlockSelection selection)
        {
            return PlacementFacing.TowardsPlayer(player, selection);
        }

        public static BlockFacing FacingPlayer(double dx, double dz) => PlacementFacing.FromOffset(dx, dz);

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            OpenDialog(world, blockSel.Position, byPlayer);
            return true;
        }

        private static void OpenDialog(IWorldAccessor world, BlockPos pos, IPlayer byPlayer)
        {
            if (world.Side != EnumAppSide.Client) return;
            if (world.BlockAccessor.GetBlockEntity(pos) is BEYardSign sign) sign.OpenNameDialog(byPlayer);
        }
    }
}
