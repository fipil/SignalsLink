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
            var target = selection.DidOffset ? selection.Position.AddCopy(selection.Face.Opposite) : selection.Position;
            return FacingPlayer(
                player.Entity.Pos.X + player.Entity.LocalEyePos.X - target.X - selection.HitPosition.X,
                player.Entity.Pos.Z + player.Entity.LocalEyePos.Z - target.Z - selection.HitPosition.Z);
        }
        // Our model's named side is its front. Choose the direction towards the player
        // explicitly rather than inheriting another block's orientation convention.
        public static BlockFacing FacingPlayer(double dx, double dz) => Math.Abs(dx) > Math.Abs(dz)
            ? (dx > 0 ? BlockFacing.EAST : BlockFacing.WEST)
            : (dz > 0 ? BlockFacing.SOUTH : BlockFacing.NORTH);

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
