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
    public class DoorScanner : IBlockSensorScanner
    {
        public byte CalculateSignal(IWorldAccessor world, BlockPos position, Block block, BlockEntity blockEntity, byte inputSignal)
        {
            var behavior = blockEntity?.GetBehavior<BEBehaviorTrapDoor>();
            if (behavior != null)
            {
                return (byte)(behavior.Opened ? 1 : 0);
            }
            var doorBehavior = blockEntity?.GetBehavior<BEBehaviorDoor>();
            if (doorBehavior != null)
            {
                return (byte)(doorBehavior.Opened ? 1 : 0);
            }
            return 0;
        }

        public bool CanScan(Block block, BlockEntity blockEntity, byte inputSignal)
        {
            var behavior = blockEntity?.GetBehavior<BEBehaviorTrapDoor>();
            if (behavior != null)
            {
                return true;
            }
            var doorBehavior = blockEntity?.GetBehavior<BEBehaviorDoor>();
            if (doorBehavior != null)
            {
                return true;
            }
            return false;
        }
    }
}
