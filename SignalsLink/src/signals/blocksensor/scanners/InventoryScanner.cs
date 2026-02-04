using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.blocksensor.scanners
{
    public class InventoryScanner : SlotScanner
    {
        public InventoryScanner(PaperConditionsEvaluator conditionsEvaluator) : base(conditionsEvaluator)
        {
            
        }

        public override byte CalculateSignal(IWorldAccessor world, BlockPos position, Block block, BlockEntity blockEntity, byte inputSignal)
        {
            IInventory inventory = GetInventory(blockEntity);

            if (inputSignal == 15)
            {
                return CalculateInventorySignal(inventory, inputSignal);
            }
            else
            {
                return CalculateSlotSignal(world, inventory, inputSignal);
            }
        }


        public byte CalculateInventorySignal(IInventory inventory, byte inputSignal)
        {
            if (inventory == null || inventory.Empty)
                return 0;

            int totalSlots = 0;
            float totalFillLevel = 0f;

            foreach (var slot in inventory)
            {
                totalSlots++;

                if (!slot.Empty)
                {
                    float fillRatio = (float)slot.StackSize / slot.Itemstack.Collectible.MaxStackSize;
                    totalFillLevel += fillRatio;
                }
            }

            if (totalSlots == 0)
                return 0;

            float averageFill = totalFillLevel / totalSlots;
            byte result = (byte)Math.Floor(averageFill * 15);

            if (result == 0 && !inventory.Empty)
                result = 1;

            return result;
        }
    }
}
