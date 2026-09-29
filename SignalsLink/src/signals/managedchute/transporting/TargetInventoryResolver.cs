using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.managedchute.transporting
{
    /// <summary>
    /// What sits at one end of a device, offered to the paper conditions as an inventory.
    ///
    /// The point is that conditions should not have to know what kind of thing they are looking at.
    /// A chest, a stack of ground-storage piles and a burnt-out charcoal pile are three completely
    /// different constructions in the game, and without this each of them would need its own case
    /// in every condition. Wrapped up as an inventory, <c>game:charcoal 96</c> means the same thing
    /// over all three.
    ///
    /// The inventories are read-only views built for the moment: the stacks inside are the live
    /// ones for a container and for ground storage, while a layered column is measured and
    /// described. Nothing is moved through them.
    /// </summary>
    public static class TargetInventoryResolver
    {
        /// <summary>
        /// The end at <paramref name="pos"/> as an inventory, or null when there is nothing there
        /// that can be read at all.
        /// </summary>
        public static IInventory Resolve(ICoreAPI api, BlockPos pos)
        {
            if (api?.World == null || pos == null) return null;

            // Ground storage first: a pile IS a container, but a device cares about the whole
            // column standing there, not about the one pile it happens to point at.
            if (api.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityGroundStorage)
            {
                return ResolveGroundColumn(api, pos);
            }

            if (api.World.BlockAccessor.GetBlockEntity(pos) is IBlockEntityContainer container && container.Inventory != null)
            {
                return container.Inventory;
            }

            return ResolveLayeredColumn(api, pos);
        }

        /// <summary>
        /// The ground-storage column standing at <paramref name="pos"/>, one slot per pile.
        ///
        /// Always at least one slot: no piles is not "no answer", it is "nothing there", and the
        /// difference decides whether <c>in target game:firewood 95-</c> can fire. Handing back
        /// null makes every target condition fail, which is how an Output pin set while the column
        /// was full used to stay stuck at that value once the column was gone.
        /// </summary>
        public static IInventory ResolveGroundColumn(ICoreAPI api, BlockPos pos)
        {
            IBlockAccessor ba = api.World.BlockAccessor;
            List<BlockEntityGroundStorage> piles = new List<BlockEntityGroundStorage>();

            for (int dy = 0; dy < 64; dy++)
            {
                if (ba.GetBlockEntity(pos.AddCopy(0, dy, 0)) is not BlockEntityGroundStorage pile) break;
                piles.Add(pile);
            }

            InventoryGeneric inventory = NewInventory(api, "signalslink-groundcolumn", System.Math.Max(1, piles.Count));

            for (int i = 0; i < piles.Count; i++)
            {
                inventory[i].Itemstack = piles[i].Inventory?[0]?.Itemstack;
            }

            return inventory;
        }

        /// <summary>
        /// A column of layered blocks (a charcoal pile, and anything else built the same way) as an
        /// inventory: one slot per block, holding as many pieces as that block would drop.
        ///
        /// Layered blocks carry no block entity at all, which is why neither the container route
        /// nor the ground-storage route can see them — a burnt-out charcoal pile was simply
        /// invisible to the conditions. Returns null when there is no such column here.
        /// </summary>
        public static IInventory ResolveLayeredColumn(ICoreAPI api, BlockPos pos)
        {
            IBlockAccessor ba = api.World.BlockAccessor;

            Block first = null;
            string layerGroup = null;
            List<Block> column = new List<Block>();

            for (int dy = 0; dy < 64; dy++)
            {
                Block current = ba.GetBlock(pos.AddCopy(0, dy, 0));
                string group = current?.Attributes?["layerGroupCode"].AsString(null);
                if (group == null) break;

                if (first == null)
                {
                    first = current;
                    layerGroup = group;
                }
                else if (current.FirstCodePart() != first.FirstCodePart())
                {
                    break; // a different material ends the column, the same way a transfer sees it
                }

                column.Add(current);
            }

            if (column.Count == 0) return null;

            ItemStack perLayer = GetLayerDrop(first);
            if (perLayer?.Collectible == null || perLayer.StackSize <= 0) return null;

            InventoryGeneric inventory = NewInventory(api, "signalslink-layercolumn", column.Count);

            for (int i = 0; i < column.Count; i++)
            {
                int layers = GetLayerCount(column[i], layerGroup);
                if (layers <= 0) continue;

                // `amount` counts pieces, so a block of N layers reads as N layers worth of drops.
                ItemStack stack = perLayer.Clone();
                stack.StackSize = perLayer.StackSize * layers;
                inventory[i].Itemstack = stack;
            }

            return inventory;
        }

        private static InventoryGeneric NewInventory(ICoreAPI api, string id, int slots)
        {
            // NOTE: the inventory id MUST contain a dash - VS derives className/instanceId from it
            // by splitting on '-', and an id without one throws IndexOutOfRangeException.
            return new InventoryGeneric(slots, id, api);
        }

        private static int GetLayerCount(Block block, string layerGroup)
        {
            string value = block?.Variant?[layerGroup];
            return value != null && int.TryParse(value, out int layers) ? layers : 0;
        }

        /// <summary>
        /// What one layer is worth. The JSON drops of a layered block describe a single layer — the
        /// game multiplies them by the layer count when the whole block is broken — so one entry
        /// is one layer.
        /// </summary>
        private static ItemStack GetLayerDrop(Block block)
        {
            BlockDropItemStack[] drops = block?.Drops;
            if (drops == null || drops.Length == 0) return null;

            return drops[0].GetNextItemStack();
        }
    }
}
