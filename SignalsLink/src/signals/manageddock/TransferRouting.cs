using SignalsLink.src.signals.cargo;

namespace SignalsLink.src.signals.manageddock
{
    /// <summary>Which of the transfers can move goods from one hold to another.</summary>
    public enum TransferRoute
    {
        /// <summary>Nothing can move between these two ends.</summary>
        None,

        /// <summary>Two things standing in the world, built by the factory every device uses.</summary>
        BetweenPlaces,

        /// <summary>Both ends keep their goods in slots, so the slots are written directly.</summary>
        BetweenInventories,

        /// <summary>Out of slots into the world: piles built on a paved tile.</summary>
        InventoryToPlace,

        /// <summary>Out of the world into slots: piles taken apart off a paved tile.</summary>
        PlaceToInventory
    }

    /// <summary>
    /// Which transfer a pair of holds needs.
    ///
    /// Kept apart from the dock so it can be reasoned about without a world. Getting it wrong does
    /// not throw: the wrong transfer looks in the wrong place, finds nothing, and reports that it
    /// moved nothing - which in the game is indistinguishable from a train that has not arrived.
    /// </summary>
    public static class TransferRouting
    {
        public static TransferRoute Between(ICargoHold source, ICargoHold target)
        {
            if (source == null || target == null) return TransferRoute.None;

            // Both ends stand somewhere. One block handing goods to another is what a chute has
            // always done, and everything it knows comes with that path: containers, piles,
            // firepits, a bucket set down on the floor.
            if (source.Pos != null && target.Pos != null) return TransferRoute.BetweenPlaces;

            // Past here one end has no position at all - a wagon, which will drive away. It can
            // only be reached by writing its slots, so the other end has to be read as slots too,
            // whenever it HAS any of its own. A crate has; a paved tile has not, its goods are
            // blocks standing on it and only the world transfers can build or take those apart.
            bool sourceSlots = source.IsContainer && source.Inventory != null;
            bool targetSlots = target.IsContainer && target.Inventory != null;

            if (sourceSlots && targetSlots) return TransferRoute.BetweenInventories;
            if (sourceSlots && target.Pos != null) return TransferRoute.InventoryToPlace;
            if (targetSlots && source.Pos != null) return TransferRoute.PlaceToInventory;

            return TransferRoute.None;
        }
    }
}
