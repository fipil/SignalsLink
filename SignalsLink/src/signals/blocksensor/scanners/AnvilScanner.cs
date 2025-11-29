using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.blocksensor.scanners
{
    public class AnvilScanner : IBlockSensorScanner
    {
        public byte CalculateSignal(IWorldAccessor world, BlockPos position, Block block, BlockEntity blockEntity, byte inputSignal)
        {
            return (blockEntity as BlockEntityAnvil)?.CanWorkCurrent ?? false ? (byte)1 : (byte)0;
        }

        public bool CanScan(Block block, BlockEntity blockEntity, byte inputSignal)
        {
            return blockEntity is BlockEntityAnvil;
        }
    }
}
