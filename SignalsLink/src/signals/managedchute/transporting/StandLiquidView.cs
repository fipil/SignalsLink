using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.managedchute.transporting
{
    /// <summary>
    /// The bucket on a stand, seen by a valve as the liquid in it: a slot holding the bucket's
    /// content in litres, read from the bucket when built and written back when marked dirty.
    /// Without a bucket the slot is empty and holds nothing.
    /// </summary>
    public sealed class BucketLiquidSlot : ItemSlot, ILiquidHoldingSlot
    {
        private readonly IBucketSlotEnd end;
        private readonly ICoreAPI api;
        private readonly BlockLiquidContainerBase container;

        public BucketLiquidSlot(InventoryBase view, IBucketSlotEnd end, ICoreAPI api) : base(view)
        {
            this.end = end;
            this.api = api;

            ItemStack bucket = end.Slot.Itemstack;
            container = bucket?.Collectible as BlockLiquidContainerBase;
            itemstack = container?.GetContent(bucket)?.Clone();
            if (itemstack != null && itemstack.StackSize <= 0) itemstack = null;
        }

        public bool HasBucket => container != null;

        public float CapacityLitres => container?.CapacityLitres ?? 0f;

        public override bool CanHold(ItemSlot sourceSlot)
        {
            if (container == null) return false;

            ItemStack liquid = sourceSlot?.Itemstack;
            if (BlockLiquidContainerBase.GetContainableProps(liquid) == null) return false;

            return itemstack == null || itemstack.Equals(api?.World, liquid, Vintagestory.API.Config.GlobalConstants.IgnoredStackAttributes);
        }

        public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge) => CanHold(sourceSlot);

        public override int GetRemainingSlotSpace(ItemStack forItemstack)
        {
            WaterTightContainableProps props = BlockLiquidContainerBase.GetContainableProps(forItemstack);
            if (container == null || props == null) return 0;

            int capacity = (int)(CapacityLitres * props.ItemsPerLitre);
            return System.Math.Max(0, capacity - StackSize);
        }

        /// <summary>Writes the litres back into the bucket. Every change ends with MarkDirty.</summary>
        public override void MarkDirty()
        {
            if (container == null) return;

            ItemStack bucket = end.Slot.Itemstack;
            if (bucket?.Collectible != container) return;

            container.SetContent(bucket, itemstack != null && itemstack.StackSize > 0 ? itemstack : null);
            end.Slot.MarkDirty();
            end.Changed(api);
        }

        public override void OnItemSlotModified(ItemStack sinkStack) => MarkDirty();
    }

    /// <summary>
    /// A stand's inventory as a valve sees it: the real slots, with the bucket slot replaced by
    /// the liquid in the bucket. Slot numbers stay those of the block (condenser: 1 water, 2
    /// bucket; press: 1 mash, 2 bucket), so `source 2` and `target 2` mean the bucket.
    /// </summary>
    public sealed class StandLiquidView : InventoryBase
    {
        private readonly IInventory real;
        private readonly int bucketIndex;
        private readonly BucketLiquidSlot bucket;

        public StandLiquidView(IInventory real, IBucketSlotEnd end, ICoreAPI api) : base("signalslink-standliquid", api)
        {
            this.real = real;
            bucketIndex = real.GetSlotId(end.Slot);
            bucket = new BucketLiquidSlot(this, end, api);
        }

        public BucketLiquidSlot Bucket => bucket;

        public override int Count => real.Count;

        public override ItemSlot this[int slotId]
        {
            get => slotId == bucketIndex ? bucket : (slotId >= 0 && slotId < real.Count ? real[slotId] : null);
            set => throw new System.NotSupportedException("A stand view cannot be reassigned.");
        }

        public override void DidModifyItemSlot(ItemSlot slot, ItemStack extractedStack = null) { }
        public override void FromTreeAttributes(ITreeAttribute tree) { }
        public override void ToTreeAttributes(ITreeAttribute tree) { }
    }
}
