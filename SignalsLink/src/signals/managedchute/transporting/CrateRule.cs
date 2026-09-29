using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.managedchute.transporting
{
    /// <summary>
    /// The crate's one-kind rule. Vanilla enforces it only for the player's hand and the chute,
    /// through <see cref="InventoryBase.GetAutoPushIntoSlot"/>; the slots themselves take anything.
    /// A device that asks only the slots would mix a crate, so it asks the crate first - and only
    /// a crate, because the same hook on a quern, oven or trough means something else and would
    /// break papers that work today. Taking out is never gated: the rule is about what goes in.
    /// </summary>
    public static class CrateRule
    {
        public static bool IsCrate(ICoreAPI api, BlockPos pos)
        {
            return pos != null && api?.World?.BlockAccessor?.GetBlockEntity(pos) is BlockEntityCrate;
        }

        /// <summary>Whether the crate would let this stack in. A slot is only a permission; the caller keeps choosing where.</summary>
        public static bool Accepts(IInventory inventory, ItemStack stack)
        {
            if (stack == null || inventory is not InventoryBase crate) return true;
            return crate.GetAutoPushIntoSlot(BlockFacing.UP, new DummySlot(stack)) != null;
        }
    }
}
