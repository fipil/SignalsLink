using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;

namespace SignalsLink.src.signals.blocksensor.scanners
{
    public class SensorScannerFactory
    {
        private readonly List<IBlockSensorScanner> scanners;
        private readonly IBlockSensorScanner defaultScanner;

        public SensorScannerFactory()
        {
            scanners = new List<IBlockSensorScanner>();
            defaultScanner = new DefaultScanner();

            // Register scanners in order of priority (specific ones first)
            RegisterScanner(new InventoryScanner());
        }

        public void RegisterScanner(IBlockSensorScanner scanner)
        {
            scanners.Add(scanner);
        }

        public IBlockSensorScanner GetScanner(Block block, BlockEntity blockEntity)
        {
            // Find the first scanner that can process this block
            foreach (var scanner in scanners)
            {
                if (scanner.CanScan(block, blockEntity))
                    return scanner;
            }

            // Fallback to default
            return defaultScanner;
        }
    }

}
