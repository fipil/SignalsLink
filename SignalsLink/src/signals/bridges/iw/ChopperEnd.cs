using SignalsLink.src.signals.managedchute.transporting;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.bridges.iw
{
    /// <summary>
    /// The chopper as a device sees it: slot 1 is the log on the bed, slot 2 the head on the arm.
    /// Both are virtual; a put into them is carried into the chopper, nothing is ever taken out.
    /// </summary>
    public sealed class ChopperEnd : ITransferEnd
    {
        private readonly ChopperView view;

        public ChopperEnd(ICoreAPI api, IChopperAccess access, BlockEntity master, BlockPos cell, BlockFacing face)
        {
            Pos = cell;
            view = new ChopperView(api, access, master, cell, face);
        }

        public BlockPos Pos { get; }

        public IInventory View => view;

        // The slots tell the chopper themselves the moment they write into it.
        public void Changed(ICoreAPI api) { }
    }

    public sealed class ChopperView : InventoryBase
    {
        public const int LogSlot = 0;
        public const int HeadSlot = 1;

        private readonly ChopperLogSlot log;
        private readonly ChopperHeadSlot head;
        private readonly BlockEntity master;

        public ChopperView(ICoreAPI api, IChopperAccess access, BlockEntity master, BlockPos cell, BlockFacing face)
            : base("signalslink-chopper", api)
        {
            this.master = master;
            log = new ChopperLogSlot(this, access, master, cell, face);
            head = new ChopperHeadSlot(this, access, master);
        }

        public override int Count => 2;

        public override ItemSlot this[int slotId]
        {
            get
            {
                // Mirrors are refreshed on every look: the chopper changes them on its own.
                switch (slotId)
                {
                    case LogSlot: log.Sync(); return log;
                    case HeadSlot: head.Sync(); return head;
                    default: return null;
                }
            }
            set => throw new System.NotSupportedException("A chopper slot cannot be reassigned.");
        }

        public override void FromTreeAttributes(ITreeAttribute tree) { }
        public override void ToTreeAttributes(ITreeAttribute tree) { }

        internal void PlayPlaceSound()
        {
            SoundAttributes? sound = master?.Block?.Sounds?.Place;
            if (sound.HasValue && master.Pos != null) Api?.World?.PlaySoundAt(sound.Value, master.Pos, -0.25, null);
        }
    }

    /// <summary>A slot nothing is taken out of; the mirror is only ever read or put into.</summary>
    public abstract class ChopperSlot : ItemSlot
    {
        protected ChopperSlot(ChopperView view) : base(view)
        {
            MaxSlotStackSize = 1;
        }

        public abstract void Sync();

        public override bool CanTake() => false;
        public override ItemStack TakeOut(int quantity) => null;
        public override ItemStack TakeOutWhole() => null;
        public override int GetRemainingSlotSpace(ItemStack forItemstack) => itemstack == null ? 1 : 0;
        public override void MarkDirty() { }
    }

    public sealed class ChopperLogSlot : ChopperSlot
    {
        private readonly ChopperView view;
        private readonly IChopperAccess access;
        private readonly BlockEntity master;
        private readonly BlockPos cell;
        private readonly BlockFacing face;

        public ChopperLogSlot(ChopperView view, IChopperAccess access, BlockEntity master, BlockPos cell, BlockFacing face) : base(view)
        {
            this.view = view;
            this.access = access;
            this.master = master;
            this.cell = cell;
            this.face = face;
        }

        public override void Sync() => itemstack = access.InputSlot(master)?.Itemstack;

        // The chopper's own gate decides: assembled, bed empty, axe up, a log, the right side.
        private bool Admits(ItemSlot source)
            => face != null && source?.Itemstack != null && access.AutoPushSlot(master, face, source, cell) != null;

        public override bool CanHold(ItemSlot sourceSlot) => Admits(sourceSlot);

        public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge) => Admits(sourceSlot);

        public override void OnItemSlotModified(ItemStack sinkStack)
        {
            ItemSlot real = access.InputSlot(master);
            if (real == null || itemstack == null) return;

            real.Itemstack = itemstack;
            real.MarkDirty();
            access.Changed(master);
            view.PlayPlaceSound();
        }
    }

    public sealed class ChopperHeadSlot : ChopperSlot
    {
        public const string HeadDomain = "immersivewoodworking";
        public const string HeadCode = "chopperhead";

        private readonly ChopperView view;
        private readonly IChopperAccess access;
        private readonly BlockEntity master;

        public ChopperHeadSlot(ChopperView view, IChopperAccess access, BlockEntity master) : base(view)
        {
            this.view = view;
            this.access = access;
            this.master = master;
        }

        public override void Sync() => itemstack = access.Head(master);

        public static bool IsHead(ItemStack stack)
        {
            AssetLocation code = stack?.Collectible?.Code;
            return code != null && code.Domain == HeadDomain && stack.Collectible.FirstCodePart() == HeadCode;
        }

        // A head goes on only while there is none: the chopper destroys a worn-out one itself.
        private bool Admits(ItemSlot source)
            => access.CanFitHead && IsHead(source?.Itemstack) && access.Head(master) == null && access.HasArm(master);

        public override bool CanHold(ItemSlot sourceSlot) => Admits(sourceSlot);

        public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge) => Admits(sourceSlot);

        public override void OnItemSlotModified(ItemStack sinkStack)
        {
            if (itemstack == null) return;

            access.SetHead(master, itemstack);
            access.Changed(master);
            view.PlayPlaceSound();
        }
    }
}
