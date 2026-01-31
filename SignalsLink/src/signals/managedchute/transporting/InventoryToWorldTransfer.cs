using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.managedchute.transporting
{
    // Pøenos: inventáø -> svìt (spawn item entity).
    public class InventoryToWorldTransfer : IItemTransfer
    {
        private readonly ICoreAPI api;
        private readonly IInventory sourceInv;
        private readonly byte inputSlotSignal;
        private readonly BlockPos targetPos;

        public InventoryToWorldTransfer(ICoreAPI api, IInventory sourceInv, byte inputSlotSignal, BlockPos targetPos)
        {
            this.api = api;
            this.sourceInv = sourceInv;
            this.inputSlotSignal = inputSlotSignal;
            this.targetPos = targetPos;
        }

        public int TryMoveOneItem(ItemStackMoveOperation opTemplate)
        {
            ItemSlot src = GetSourceSlot();
            if (src == null || src.Empty) return 0;

            Block blockAtTarget = api.World.BlockAccessor.GetBlock(targetPos);
            if (blockAtTarget.Replaceable < 6000) return 0;

            // Vezmi jeden kus a spawnuj ho
            ItemStack taken = src.TakeOut(1);
            if (taken == null || taken.StackSize <= 0) return 0;

            Vec3d spawnPos = targetPos.ToVec3d().Add(0.5, 0.5, 0.5);
            api.World.SpawnItemEntity(taken, spawnPos);

            src.MarkDirty();
            return 1;
        }

        private ItemSlot GetSourceSlot()
        {
            if (inputSlotSignal > 0)
            {
                int index = inputSlotSignal - 1;
                if (index >= 0 && index < sourceInv.Count)
                {
                    return sourceInv[index];
                }
                return null;
            }

            ItemSlot lastNonEmpty = null;
            for (int i = sourceInv.Count - 1; i >= 0; i--)
            {
                if (!sourceInv[i].Empty)
                {
                    lastNonEmpty = sourceInv[i];
                    break;
                }
            }

            if (lastNonEmpty != null) return lastNonEmpty;
            return sourceInv.Count > 0 ? sourceInv[sourceInv.Count - 1] : null;
        }
    }
}