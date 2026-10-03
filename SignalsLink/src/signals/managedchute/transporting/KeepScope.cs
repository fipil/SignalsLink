using Vintagestory.API.Common;

namespace SignalsLink.src.signals.managedchute.transporting
{
    /// <summary>
    /// Where `keep N` counts. Without a chosen target slot it is the whole target inventory; with
    /// one (`target N`, `target last`, or the Target pin) it is that slot alone: "put it in slot 3
    /// and hold three there" is what such a paper says. The view shares the slot's live stack and
    /// is only ever read.
    /// </summary>
    public static class KeepScope
    {
        public static IInventory Of(IInventory target, int effectiveTargetSlotSignal, ICoreAPI api)
        {
            if (target == null || effectiveTargetSlotSignal <= 0 || effectiveTargetSlotSignal > target.Count) return target;
            // NOTE: the inventory id MUST contain a dash - VS derives className/instanceId from it.
            InventoryGeneric view = new InventoryGeneric(1, "signalslink-keepslot", api);
            view[0].Itemstack = target[effectiveTargetSlotSignal - 1].Itemstack;
            return view;
        }
    }
}
