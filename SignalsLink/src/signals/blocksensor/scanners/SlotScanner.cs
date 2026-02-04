using SignalsLink.src.signals.paperConditions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.blocksensor.scanners
{
    public class SlotScanner : ConditionalScanner, IBlockSensorScanner
    {
        public SlotScanner(PaperConditionsEvaluator conditionsEvaluator) : base(conditionsEvaluator)
        {
        }

        public virtual bool CanScan(Block block, BlockEntity blockEntity, byte inputSignal)
        {
            if (blockEntity == null)
                return false;

            return GetInventory(blockEntity) != null;
        }

        public virtual byte CalculateSignal(IWorldAccessor world, BlockPos position, Block block, BlockEntity blockEntity, byte inputSignal)
        {
            IInventory inventory = GetInventory(blockEntity);

            return CalculateSlotSignal(world, inventory, inputSignal);
        }

        protected byte CalculateSlotSignal(IWorldAccessor world, IInventory inventory, byte inputSignal)
        {
            int slotIndex = inputSignal - 1; // 1 -> slot 0, 2 -> slot 1, etc.

            if (slotIndex < 0 || slotIndex >= inventory.Count)
                return 0; // Invalid index

            ItemSlot slot = inventory[slotIndex];

            if (slot.Empty)
                return 0;

            // Return the slot fill level as a signal 0-15
            float fillRatio = (float)slot.StackSize / slot.Itemstack.Collectible.MaxStackSize;
            var result = (byte)Math.Ceiling(fillRatio * 15);

            if(conditionsEvaluator.HasConditions)
            {
                var ctx = ItemConditionContextUtil.BuildContext(world, slot.Itemstack);
                conditionsEvaluator.Evaluate(slot.Itemstack, ctx, out byte matchedBlockIndex);
                return matchedBlockIndex;
            }

            if (result == 0 && !slot.Empty)
                result = 1;

            return result;
        }

        protected IInventory GetInventory(BlockEntity be)
        {
            if (be is IBlockEntityContainer container)
            {
                return container.Inventory;
            }
            return null;
        }
    }
}
