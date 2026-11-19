using System;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.bridges.beehives
{
    /// <summary>
    /// Everything the hive end asks of the hive, behind one interface so the slots can be tested
    /// against a fake and the type check stays in one place.
    /// </summary>
    public interface IBeehiveAccess
    {
        bool IsHive(BlockEntity be);

        /// <summary>The hive's frame slots, one frame each.</summary>
        IInventory Frames(BlockEntity hive);

        /// <summary>Persist and redraw after a change made from outside.</summary>
        void Changed(BlockEntity hive);
    }

    public sealed class TypeBeehiveAccess : IBeehiveAccess
    {
        private readonly BeehiveSurface surface;

        public TypeBeehiveAccess(BeehiveSurface surface)
        {
            this.surface = surface ?? throw new ArgumentNullException(nameof(surface));
        }

        public bool IsHive(BlockEntity be) => be != null && be.GetType() == surface.HiveType;

        public IInventory Frames(BlockEntity hive) => (hive as BlockEntityContainer)?.Inventory;

        public void Changed(BlockEntity hive) => hive?.MarkDirty(true);
    }
}
