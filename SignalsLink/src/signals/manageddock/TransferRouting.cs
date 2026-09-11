using SignalsLink.src.signals.cargo;

namespace SignalsLink.src.signals.manageddock
{
    public enum TransferRoute
    {
        None,
        BetweenPlaces,
        BetweenInventories,
        InventoryToPlace,
        PlaceToInventory
    }

    /// <summary>
    /// Which transfer a pair of holds needs. Kept out of the block entity so it can be tested
    /// without a world - the wrong route does not throw, it silently moves nothing.
    /// </summary>
    public static class TransferRouting
    {
        public static TransferRoute Between(ICargoHold source, ICargoHold target)
        {
            if (source == null || target == null) return TransferRoute.None;

            if (source.Pos != null && target.Pos != null) return TransferRoute.BetweenPlaces;

            // One end has no position: a wagon, reachable only through its slots. So the other end
            // is read as slots too, whenever it has any. A paved tile has none - its goods are
            // blocks, and only the world transfers can build or take those apart.
            bool sourceSlots = source.IsContainer && source.Inventory != null;
            bool targetSlots = target.IsContainer && target.Inventory != null;

            if (sourceSlots && targetSlots) return TransferRoute.BetweenInventories;
            if (sourceSlots && target.Pos != null) return TransferRoute.InventoryToPlace;
            if (targetSlots && source.Pos != null) return TransferRoute.PlaceToInventory;

            return TransferRoute.None;
        }
    }
}
