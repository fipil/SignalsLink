using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;

namespace SignalsLink.src.signals.sensor.scanners
{
    public class SlotScanner : IBlockSensorScanner
    {
        public virtual bool CanScan(Block block, BlockEntity blockEntity)
        {
            if (blockEntity == null)
                return false;

            return GetInventory(blockEntity) != null;
        }

        public virtual byte CalculateSignal(Block block, BlockEntity blockEntity, byte inputSignal)
        {
            IInventory inventory = GetInventory(blockEntity);

            return CalculateSlotSignal(inventory, inputSignal);
        }

        protected byte CalculateSlotSignal(IInventory inventory, byte inputSignal)
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
