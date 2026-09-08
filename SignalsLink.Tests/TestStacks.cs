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
        // One Item instance per code, the way the game registry hands out one instance per item.
        // Without this, two stacks of the same code hold two different Collectible objects and the
        // transfer code - which compares them by reference before anything else - reads them as
        // different materials.
        private static readonly Dictionary<string, Item> items = new Dictionary<string, Item>();

        public static ItemStack Item(string code, int stackSize = 1)
        {
            if (!items.TryGetValue(code, out Item item))
            {
                item = new Item { Code = new AssetLocation(code) };
                items[code] = item;
            }

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
