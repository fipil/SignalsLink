using System;
using Vintagestory.API.Common;

namespace SignalsLink.src.signals.testchest;

/// <summary>
/// The bottomless chest's inventory: a plain InventoryGeneric that understands one extra gesture in its GUI.
/// Shift-clicking a filled slot with an empty mouse deletes that slot's template instead of pulling the stack.
/// Runs on both sides like any slot activation; the server owns the stock, the client just empties its copy.
/// </summary>
public class ClearableInventory : InventoryGeneric
{
    /// <summary>Server: forget the slot's template; the slot itself is emptied here.</summary>
    public Action<int> OnClearSlot;

    public ClearableInventory(int quantitySlots, string className, string instanceId, ICoreAPI api) : base(quantitySlots, className, instanceId, api) { }

    public static bool IsClearGesture(ItemSlot slot, ItemSlot sourceSlot, in ItemStackMoveOperation op) =>
        op.ShiftDown && !op.CtrlDown && op.MouseButton == EnumMouseButton.Left && !slot.Empty && (sourceSlot == null || sourceSlot.Empty);

    public override object ActivateSlot(int slotId, ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        if (slotId < 0 || slotId >= Count || !IsClearGesture(this[slotId], sourceSlot, op)) return base.ActivateSlot(slotId, sourceSlot, ref op);
        object packet = InvNetworkUtil?.GetActivateSlotPacket(slotId, op);   // the client sends this on to the server
        if (OnClearSlot != null) OnClearSlot(slotId);
        else { this[slotId].Itemstack = null; this[slotId].MarkDirty(); }
        return packet;
    }
}
