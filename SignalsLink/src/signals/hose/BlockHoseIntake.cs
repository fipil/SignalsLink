using System;
using signals.src.signalNetwork;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.hose
{
    /// <summary>
    /// Intake — a passive liquid source (fresh/salt water) that must stand in water. One hose
    /// anchor on top. The liquid is pulled out of it by an active Valve; it moves nothing itself.
    /// A full water source (water-still-7 / saltwater-still-7) must be at its position or one of
    /// the six neighbours — the same blocks the transfer can actually draw from.
    /// </summary>
    public class BlockHoseIntake : BlockHoseAnchorBase
    {
        // The intake is a source that can feed many targets — its anchor accepts multiple hoses.
        public override bool AllowsMultipleHoses(NodePos pos) => true;

        public override bool CanPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref string failureCode)
        {
            if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode)) return false;

            if (!StandsInWater(world, blockSel.Position))
            {
                failureCode = "signalslink:hoseintake-needswater";
                return false;
            }
            return true;
        }

        private static bool StandsInWater(IWorldAccessor world, BlockPos pos)
        {
            if (IsWaterSource(world, pos)) return true;
            foreach (BlockFacing f in BlockFacing.ALLFACES)
            {
                if (IsWaterSource(world, pos.AddCopy(f))) return true;
            }
            return false;
        }

        private static bool IsWaterSource(IWorldAccessor world, BlockPos pos)
        {
            Block b = world.BlockAccessor.GetBlock(pos, BlockLayersAccess.Fluid);
            string path = b?.Code?.Path;
            return path != null
                && (path.Equals("water-still-7", StringComparison.OrdinalIgnoreCase)
                    || path.Equals("saltwater-still-7", StringComparison.OrdinalIgnoreCase));
        }
    }
}
