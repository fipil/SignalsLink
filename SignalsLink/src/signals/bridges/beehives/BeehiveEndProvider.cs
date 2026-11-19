using SignalsLink.src.signals.managedchute.transporting;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.bridges.beehives
{
    /// <summary>Offers a <see cref="BeehiveEnd"/> for a hive block.</summary>
    public sealed class BeehiveEndProvider : ITransferEndProvider
    {
        private readonly IBeehiveAccess access;

        public BeehiveEndProvider(IBeehiveAccess access)
        {
            this.access = access;
        }

        public ITransferEnd At(ICoreAPI api, BlockPos pos, BlockPos devicePos)
        {
            BlockEntity be = api?.World?.BlockAccessor?.GetBlockEntity(pos);
            if (be == null || !access.IsHive(be)) return null;

            IInventory frames = access.Frames(be);
            return frames == null ? null : new BeehiveEnd(api, access, be, frames);
        }
    }
}
