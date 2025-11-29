using signals.src.transmission;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.blocksensor
{
    public class BlockSensor : BlockConnection
    {
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (!base.OnBlockInteractStart(world, byPlayer, blockSel)) return false;

            BEBlockSensor be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BEBlockSensor;

            if (be != null)
            {
                string[] scanningModes = new string[] { "fwddown", "fwd", "fwdup", "fwdrightup", "fwdright", "fwdrightdown" };
                int currentIndex = Array.IndexOf(scanningModes, be.ScanningDirection);
                int nextIndex = (currentIndex + 1) % scanningModes.Length;

                be.SetScanningDirection(scanningModes[nextIndex]);

                return true;
            }

            return false;
        }

        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);

            (world.BlockAccessor.GetBlockEntity(pos) as BEBlockSensor)?.OnNeighbourBlockChange(neibpos);
        }

    }
}
