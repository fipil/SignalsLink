using SignalsLink.src.signals.managedchute.transporting;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.bridges.iw
{
    /// <summary>Offers a <see cref="ChopperEnd"/> for any cell of a chopper multiblock.</summary>
    public sealed class ChopperEndProvider : ITransferEndProvider
    {
        private readonly IChopperAccess access;

        public ChopperEndProvider(IChopperAccess access)
        {
            this.access = access;
        }

        public ITransferEnd At(ICoreAPI api, BlockPos pos, BlockPos devicePos)
        {
            IBlockAccessor ba = api?.World?.BlockAccessor;
            if (ba == null) return null;

            BlockEntity be = ba.GetBlockEntity(pos);
            if (be == null || !access.IsPart(be)) return null;

            BlockEntity master = access.Master(ba, be);
            if (master == null) return null;

            // Only a device that says where it stands may put logs in: the chopper admits them
            // from its back alone, and a reader has no side.
            BlockFacing face = devicePos == null ? null : AutomationAccess.FaceTowards(pos, devicePos);
            return new ChopperEnd(api, access, master, pos, face);
        }
    }
}
