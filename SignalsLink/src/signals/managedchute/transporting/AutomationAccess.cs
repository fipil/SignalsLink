using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.managedchute.transporting
{
    /// <summary>
    /// A block entity that lets automation in through some of its faces only. A chute or a flap asks
    /// with the block it touches (a part of a multiblock, or the entity's own block) and that block's
    /// face, before every move and in both directions; a closed answer leaves the inventory untouched
    /// this tick, as if nothing stood there. Containers without the interface are open from every side.
    /// </summary>
    public interface ISidedAutomation
    {
        bool AllowsAutomation(BlockPos touchedBlock, BlockFacing face);
    }

    /// <summary>
    /// What a device finds at the other end of its mouth. A part of a multiblock has no block
    /// entity of its own; the game hands out the controller's position, and that is where the
    /// inventory lives. Plain blocks answer as they always did.
    /// </summary>
    public static class AutomationAccess
    {
        /// <summary>The position holding the block entity for <paramref name="pos"/>: the controller of a multiblock part, else pos itself.</summary>
        public static BlockPos ControlPos(IBlockAccessor ba, BlockPos pos)
        {
            if (ba == null || pos == null) return pos;
            if (ba.GetBlockEntity(pos) != null) return pos;
            return ba.GetBlock(pos) is IMultiblockOffset part ? part.GetControlBlockPos(pos) ?? pos : pos;
        }

        public static BlockEntity EntityAt(IBlockAccessor ba, BlockPos pos) =>
            ba == null || pos == null ? null : ba.GetBlockEntity(ControlPos(ba, pos));

        /// <summary>
        /// Whether a device at <paramref name="devicePos"/> may touch the block at <paramref name="pos"/>.
        /// The face asked about is the one the device looks at: of a multiblock part, that part's face.
        /// </summary>
        public static bool Allows(IBlockAccessor ba, BlockPos pos, BlockPos devicePos)
        {
            if (EntityAt(ba, pos) is not ISidedAutomation sided) return true;
            return sided.AllowsAutomation(pos, FaceTowards(pos, devicePos));
        }

        /// <summary>Face of the block at <paramref name="pos"/> that looks at <paramref name="devicePos"/>.</summary>
        public static BlockFacing FaceTowards(BlockPos pos, BlockPos devicePos) =>
            BlockFacing.FromNormal(new Vec3i(devicePos.X - pos.X, devicePos.Y - pos.Y, devicePos.Z - pos.Z)) ?? BlockFacing.NORTH;
    }
}
