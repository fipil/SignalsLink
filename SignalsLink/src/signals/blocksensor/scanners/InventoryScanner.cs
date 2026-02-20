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
    public class InventoryScanner : SlotScanner
    {
        public override byte CalculateSignal(IWorldAccessor world, PaperConditionsEvaluator conditionsEvaluator, BlockPos position, Block block, BlockEntity blockEntity, byte inputSignal)
        {
            IInventory inventory = GetInventory(blockEntity);

            if (inputSignal == 15)
            {
                return CalculateInventorySignal(world, conditionsEvaluator, inventory, inputSignal);
            }
            else
            {
                return CalculateSlotSignal(world, conditionsEvaluator, inventory, inputSignal);
            }
        }


        public byte CalculateInventorySignal(IWorldAccessor world, PaperConditionsEvaluator conditionsEvaluator, IInventory inventory, byte inputSignal)
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
                    if (conditionsEvaluator.HasConditions)
                    {
                        ItemStack stackForEval = slot.Itemstack;
                        var ctx = ItemConditionContextUtil.BuildContext(world, stackForEval);

                        if (conditionsEvaluator.Evaluate(stackForEval, ctx, out byte output))
                        {
                            if (output > 0 && output < 15)
                                return output;
                            else if (output == 15)
                            {
                                return (byte)totalSlots; // Return slot number as signal
                            }
                        }
                    }

                    float fillRatio = (float)slot.StackSize / slot.Itemstack.Collectible.MaxStackSize;
                    totalFillLevel += fillRatio;
                }
            }

            if(conditionsEvaluator.HasConditions)
            {
                // No condition met, so return zero
                return 0;
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
