using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.managedchute.transporting
{
    /// <summary>
    /// A rack, a shelf or a bookcase draws what it holds, and redraws only when its block entity
    /// is sent to the clients again. A dirty slot is enough for a chest and not for these: the
    /// scrolls were gone from the rack and still drawn in it.
    /// </summary>
    public static class DisplayRefresh
    {
        public static void After(ICoreAPI api, IInventory inventory)
        {
            BlockPos pos = (inventory as InventoryBase)?.Pos;
            if (pos == null) return;

            // Only these: sending a whole chest again after every item would be a lot of traffic.
            if (api?.World?.BlockAccessor?.GetBlockEntity(pos) is BlockEntityDisplay display) display.MarkDirty(true);
        }
    }
}
