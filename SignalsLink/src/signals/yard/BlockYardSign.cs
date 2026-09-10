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

            // Facing the player, the same way a sign does, so the name reads from where you stand.
            //
            // NOT the opposite: SuggestedHVOrientation already answers with the direction from the
            // block TOWARDS whoever is placing it, so turning it round showed them the back of the
            // sign - and the name came out mirrored, being drawn on the face pointing away.
            BlockFacing[] horizontal = SuggestedHVOrientation(byPlayer, blockSel);
            BlockFacing facing = horizontal != null && horizontal.Length > 0 && horizontal[0] != null
                ? horizontal[0]
                : BlockFacing.NORTH;

            Block oriented = world.BlockAccessor.GetBlock(CodeWithVariant("side", facing.Code));
            if (oriented != null) world.BlockAccessor.ExchangeBlock(oriented.BlockId, blockSel.Position);

            // Straight into the form: a sign with no name on it is the one thing nobody wants.
            OpenDialog(world, blockSel.Position, byPlayer);
            return true;
        }

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
