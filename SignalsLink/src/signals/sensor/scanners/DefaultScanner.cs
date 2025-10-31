using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;

namespace SignalsLink.src.signals.sensor.scanners
{
    public class DefaultScanner : IBlockSensorScanner
    {
        public bool CanScan(Block block, BlockEntity blockEntity)
        {
            return true; // Fallback pro všechno
        }

        public byte CalculateSignal(Block block, BlockEntity blockEntity, byte inputSignal)
        {
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
