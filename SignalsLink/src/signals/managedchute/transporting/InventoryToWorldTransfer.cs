using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.managedchute.transporting
{
    // Přenos: inventář -> svět (spawn item entity).
    public class InventoryToWorldTransfer : InventorySourcedTransferBase, IItemTransfer
    {
        private readonly ICoreAPI api;
        private readonly BlockPos targetPos;

        public InventoryToWorldTransfer(ICoreAPI api, IInventory sourceInv, byte inputSlotSignal, BlockPos targetPos): base(sourceInv, inputSlotSignal)
        {
            this.api = api;
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

    }
}