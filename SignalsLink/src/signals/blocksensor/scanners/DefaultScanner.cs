using SignalsLink.src.signals.paperConditions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.blocksensor.scanners
{
    public class DefaultScanner : IBlockSensorScanner
    {
        public bool CanScan(Block block, BlockEntity blockEntity, byte inputSignal)
        {
            return true; // Fallback pro všechno
        }

        public byte CalculateSignal(IWorldAccessor world, PaperConditionsEvaluator conditionsEvaluator, BlockPos position, Block block, BlockEntity blockEntity, byte inputSignal)
        {
            if(conditionsEvaluator.HasConditions)
            {
                conditionsEvaluator.Evaluate(world.Api, position, out byte matchedBlockIndex);
                return matchedBlockIndex;
            }

            // 0 = vzduch
            if (block == null || block.Id == 0)
                return 0;

            // 2/3 = voda
            if (block.BlockMaterial == EnumBlockMaterial.Liquid)
            {
                // Rozlišení sladká/slaná voda
                if (block.Code.Path.Contains("saltwater") || block.Code.Path.Contains("salt"))
                    return 3; // Slaná
                return 2; // Sladká
            }

            // 1 = nějaký blok
            return 1;
        }
    }

}
