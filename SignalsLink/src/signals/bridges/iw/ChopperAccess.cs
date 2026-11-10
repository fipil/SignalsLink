using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.bridges.iw
{
    /// <summary>
    /// Everything the chopper end asks of the chopper, behind one interface so the slots can be
    /// tested against a fake and the reflection stays in one place.
    /// </summary>
    public interface IChopperAccess
    {
        bool CanFitHead { get; }

        /// <summary>The master (frame) or one of the invisible filler cells.</summary>
        bool IsPart(BlockEntity be);

        /// <summary>The master behind any cell, or null while the multiblock is not whole.</summary>
        BlockEntity Master(IBlockAccessor ba, BlockEntity be);

        ItemSlot InputSlot(BlockEntity master);

        /// <summary>The chopper's own hopper gate: the input slot when a log may go in from that side right now.</summary>
        ItemSlot AutoPushSlot(BlockEntity master, BlockFacing face, ItemSlot from, BlockPos cell);

        ItemStack Head(BlockEntity master);

        bool HasArm(BlockEntity master);

        void SetHead(BlockEntity master, ItemStack head);

        /// <summary>Persist and redraw after a change made from outside.</summary>
        void Changed(BlockEntity master);
    }

    public sealed class ReflectionChopperAccess : IChopperAccess
    {
        private readonly IwSurface surface;

        public ReflectionChopperAccess(IwSurface surface)
        {
            this.surface = surface ?? throw new ArgumentNullException(nameof(surface));
        }

        public bool CanFitHead => surface.CanFitHead;

        public bool IsPart(BlockEntity be)
        {
            Type type = be?.GetType();
            return type != null && (type == surface.MasterType || type == surface.PartType);
        }

        public BlockEntity Master(IBlockAccessor ba, BlockEntity be)
        {
            if (be == null) return null;
            if (be.GetType() == surface.MasterType) return be;
            if (be.GetType() != surface.PartType) return null;

            BlockPos principal = surface.Principal.GetValue(be) as BlockPos;
            if (principal == null) return null;

            BlockEntity master = ba?.GetBlockEntity(principal);
            return master != null && master.GetType() == surface.MasterType ? master : null;
        }

        public ItemSlot InputSlot(BlockEntity master)
            => (surface.Inventory.GetValue(master) as InventoryBase)?[0];

        public ItemSlot AutoPushSlot(BlockEntity master, BlockFacing face, ItemSlot from, BlockPos cell)
            => surface.AutoPush.Invoke(master, new object[] { face, from, cell }) as ItemSlot;

        public ItemStack Head(BlockEntity master)
            => surface.HeadStack?.GetValue(master) as ItemStack;

        public bool HasArm(BlockEntity master)
            => surface.HasArm != null && surface.HasArm.GetValue(master) is bool arm && arm;

        public void SetHead(BlockEntity master, ItemStack head)
        {
            if (!surface.CanFitHead) return;
            surface.HeadField.SetValue(master, head);
            surface.UpdateRenderer?.Invoke(master, null);
        }

        public void Changed(BlockEntity master) => master?.MarkDirty(true);
    }
}
