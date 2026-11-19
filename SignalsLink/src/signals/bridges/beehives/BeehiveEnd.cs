using SignalsLink.src.signals.managedchute.transporting;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.bridges.beehives
{
    /// <summary>
    /// The hive as a device sees it: its frame slots, one frame each. A frame goes into an empty
    /// slot only and nothing else goes in at all; any frame comes out. The hive itself turns an
    /// empty frame into a filled one in place, so the slot numbers are the hive's own.
    /// </summary>
    public sealed class BeehiveEnd : ITransferEnd
    {
        private readonly IBeehiveAccess access;
        private readonly BlockEntity hive;
        private readonly BeehiveView view;

        public BeehiveEnd(ICoreAPI api, IBeehiveAccess access, BlockEntity hive, IInventory frames)
        {
            this.access = access;
            this.hive = hive;
            view = new BeehiveView(api, frames);
        }

        public BlockPos Pos => hive.Pos;
        public IInventory View => view;

        public void Changed(ICoreAPI api) => access.Changed(hive);
    }

    public sealed class BeehiveView : InventoryBase
    {
        private readonly IInventory real;
        private readonly BeehiveSlot[] slots;

        public BeehiveView(ICoreAPI api, IInventory real) : base("signalslink-beehive", api)
        {
            this.real = real;
            slots = new BeehiveSlot[real.Count];
            for (int i = 0; i < slots.Length; i++) slots[i] = new BeehiveSlot(this, real[i]);
        }

        public override int Count => slots.Length;

        public override ItemSlot this[int slotId]
        {
            get
            {
                if (slotId < 0 || slotId >= slots.Length) return null;
                // Mirrors are refreshed on every look: the hive changes them on its own.
                slots[slotId].Sync();
                return slots[slotId];
            }
            set => throw new System.NotSupportedException("A hive slot cannot be reassigned.");
        }

        public override void FromTreeAttributes(ITreeAttribute tree) { }
        public override void ToTreeAttributes(ITreeAttribute tree) { }
    }

    /// <summary>A mirror of one hive slot: a frame put into it is carried into the hive, a take comes out of the hive.</summary>
    public sealed class BeehiveSlot : ItemSlot
    {
        public const string FrameDomain = "orekiwoofsbeehives";
        public const string FramePrefix = "beehiveframe";

        private readonly ItemSlot real;

        public BeehiveSlot(BeehiveView view, ItemSlot real) : base(view)
        {
            this.real = real;
            MaxSlotStackSize = 1;
        }

        // A copy, never the hive's own stack: a merge out of this slot subtracts from the mirror,
        // and the difference is settled into the hive in OnItemSlotModified.
        public void Sync()
        {
            // A stack of nothing is no frame; drop it so it never walks into a chest.
            if (real.Itemstack != null && real.Itemstack.StackSize <= 0) { real.Itemstack = null; real.MarkDirty(); }
            itemstack = real.Itemstack?.Clone();
        }

        public static bool IsFrame(ItemStack stack)
        {
            AssetLocation code = stack?.Collectible?.Code;
            return code != null && code.Domain == FrameDomain && code.Path.StartsWith(FramePrefix);
        }

        // One frame, into an empty slot; the hive never merges, and neither does this.
        private bool Admits(ItemSlot source) => real.Empty && IsFrame(source?.Itemstack);

        public override bool CanHold(ItemSlot sourceSlot) => Admits(sourceSlot);

        public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge) => Admits(sourceSlot);

        public override int GetRemainingSlotSpace(ItemStack forItemstack) => real.Empty && IsFrame(forItemstack) ? 1 : 0;

        public override bool CanTake() => !real.Empty;

        public override ItemStack TakeOut(int quantity)
        {
            ItemStack taken = real.TakeOut(quantity);
            Sync();
            return taken;
        }

        public override ItemStack TakeOutWhole()
        {
            ItemStack taken = real.TakeOutWhole();
            Sync();
            return taken;
        }

        public override void OnItemSlotModified(ItemStack sinkStack)
        {
            int now = itemstack?.StackSize ?? 0;
            int before = real.StackSize;

            if (now < before)
            {
                // A merge took out of the mirror: take the same out of the hive.
                real.TakeOut(before - now);
                real.MarkDirty();
            }
            else if (itemstack != null && real.Empty)
            {
                // A put landed in the mirror: carry it into the hive.
                real.Itemstack = itemstack;
                real.MarkDirty();
            }

            Sync();
        }

        public override void MarkDirty() => real.MarkDirty();
    }
}
