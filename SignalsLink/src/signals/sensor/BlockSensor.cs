using signals.src.transmission;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;

namespace SignalsLink.src.signals.sensor
{
    public class BlockSensor : BlockConnection
    {
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (!base.OnBlockInteractStart(world, byPlayer, blockSel)) return false;

            BESensor be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BESensor;

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

    }
}
