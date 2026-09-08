using System.Collections.Generic;
using Vintagestory.API.Common;

namespace SignalsLink.Tests
{
    /// <summary>
    /// Hand-built game objects for tests. Nothing here touches a running game: an Item needs only
    /// a Code to answer the questions the paper conditions ask of it.
    /// </summary>
    public static class TestStacks
    {
        public static ItemStack Item(string code, int stackSize = 1)
        {
            Item item = new Item { Code = new AssetLocation(code) };
            return new ItemStack(item, stackSize);
        }

        /// <summary>An inventory of <paramref name="slotCount"/> slots holding the given stacks in order.</summary>
        public static InventoryGeneric Inventory(int slotCount, params ItemStack[] stacks)
        {
            InventoryGeneric inventory = new InventoryGeneric(slotCount, "signalslink-test", null);

            for (int i = 0; i < stacks.Length && i < slotCount; i++)
            {
                inventory[i].Itemstack = stacks[i];
            }

            return inventory;
        }

        public static IDictionary<string, object> Ctx()
        {
            return new Dictionary<string, object>();
        }
    }
}
