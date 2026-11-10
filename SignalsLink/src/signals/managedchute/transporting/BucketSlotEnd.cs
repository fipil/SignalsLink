using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.managedchute.transporting
{
    /// <summary>
    /// A device end that is a bucket stand: the slot under the condenser's spout, or the one
    /// under the fruit press. To a device the block IS that slot - it never touches the other
    /// slots of the block entity (cooling water, mash), which have their own ways in.
    /// </summary>
    public interface IBucketSlotEnd : ITransferEnd
    {
        ItemSlot Slot { get; }

        bool Accepts(ItemStack stack);
    }

    /// <summary>What may stand under the spout. Mirrors the vanilla interaction checks.</summary>
    public static class BucketSlotRules
    {
        public static bool CondenserAccepts(CollectibleObject item)
            => item is BlockLiquidContainerTopOpened c && c.CapacityLitres >= 1f && c.CapacityLitres < 20f;

        public static bool PressAccepts(CollectibleObject item)
            => item is BlockLiquidContainerBase c && c.AllowHeldLiquidTransfer && c.IsTopOpened && c.CapacityLitres < 20f;
    }

    public static class BucketSlotEnds
    {
        /// <summary>The bucket stand at <paramref name="pos"/>, or null when the block is not one.</summary>
        public static IBucketSlotEnd At(IBlockAccessor blockAccess, BlockPos pos)
        {
            if (blockAccess == null || pos == null) return null;

            switch (blockAccess.GetBlockEntity(pos))
            {
                case BlockEntityCondenser condenser: return new CondenserEnd(condenser);
                case BlockEntityFruitPress press: return new PressBucketEnd(press);
                default: return null;
            }
        }

        /// <summary>
        /// A stand's inventory for a valve: its slots with the bucket read as the liquid in it,
        /// or null when the block is not a stand. The valve's host and far host resolve through
        /// this before the plain container lookup.
        /// </summary>
        public static IInventory LiquidViewAt(ICoreAPI api, BlockPos pos)
        {
            IBlockAccessor ba = api?.World?.BlockAccessor;
            IBucketSlotEnd end = At(ba, pos);
            if (end == null || ba.GetBlockEntity(pos) is not IBlockEntityContainer container || container.Inventory == null) return null;

            return new StandLiquidView(container.Inventory, end, api);
        }

        private sealed class CondenserEnd : IBucketSlotEnd
        {
            private readonly BlockEntityCondenser condenser;
            private SingleSlotView view;

            public CondenserEnd(BlockEntityCondenser condenser) { this.condenser = condenser; }

            public BlockPos Pos => condenser.Pos;
            public ItemSlot Slot => condenser.Inventory[1];
            public IInventory View => view ??= new SingleSlotView(Slot, condenser.Api);
            public bool Accepts(ItemStack stack) => BucketSlotRules.CondenserAccepts(stack?.Collectible);

            public void Changed(ICoreAPI api)
            {
                condenser.MarkDirty(true);

                // The condenser clears its bucket mesh only in the player interaction; a bucket
                // taken by a device would stay drawn until the chunk reloads.
                if (Slot.Empty) api?.ModLoader.GetModSystem<BucketSlotSync>()?.SendCleared(api, Pos);
            }
        }

        private sealed class PressBucketEnd : IBucketSlotEnd
        {
            private readonly BlockEntityFruitPress press;
            private SingleSlotView view;

            public PressBucketEnd(BlockEntityFruitPress press) { this.press = press; }

            public BlockPos Pos => press.Pos;
            public ItemSlot Slot => press.BucketSlot;
            public IInventory View => view ??= new SingleSlotView(Slot, press.Api);
            public bool Accepts(ItemStack stack) => BucketSlotRules.PressAccepts(stack?.Collectible);
            public void Changed(ICoreAPI api) => press.MarkDirty(true);
        }
    }

    /// <summary>
    /// One real slot of another inventory, offered as an inventory of its own. The slot object is
    /// the live one, so taking out of it reaches the block entity it belongs to.
    /// </summary>
    public sealed class SingleSlotView : InventoryBase
    {
        private readonly ItemSlot slot;

        // The id must contain a dash: VS splits it into class and instance id.
        public SingleSlotView(ItemSlot slot, ICoreAPI api) : base("signalslink-slotview", api)
        {
            this.slot = slot;
        }

        public override int Count => 1;

        public override ItemSlot this[int slotId]
        {
            get => slotId == 0 ? slot : null;
            set => throw new System.NotSupportedException("A slot view cannot be reassigned.");
        }

        public override void FromTreeAttributes(ITreeAttribute tree) { }
        public override void ToTreeAttributes(ITreeAttribute tree) { }
    }
}
