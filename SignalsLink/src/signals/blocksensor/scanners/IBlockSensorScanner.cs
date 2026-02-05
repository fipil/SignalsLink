using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.blocksensor.scanners
{
    public interface IBlockSensorScanner
    {
        /// <summary>
        /// Returns true if this scanner can process the given block/BlockEntity
        /// </summary>
        bool CanScan(Block block, BlockEntity blockEntity, byte inputSignal);

        /// <summary>
        /// Calculates the output signal (0-15) based on the input and the scanned block
        /// </summary>
        byte CalculateSignal(IWorldAccessor world, PaperConditionsEvaluator conditionsEvaluator, BlockPos position, Block block, BlockEntity blockEntity, byte inputSignal);
    }

}
