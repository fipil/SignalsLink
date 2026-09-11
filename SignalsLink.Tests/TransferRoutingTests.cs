using SignalsLink.src.signals.cargo;
using SignalsLink.src.signals.manageddock;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>
    /// Which transfer the dock builds for a pair of holds.
    ///
    /// The fault this was written for moved nothing and said nothing: the crate names a position,
    /// the position was read as "a place in the world", and so loading a wagon out of the crate was
    /// built as a transfer that looks for piles standing on the dock's own block. There are none,
    /// it moved nothing, and that is exactly what a train which has not arrived looks like too.
    /// </summary>
    public class TransferRoutingTests
    {
        [Fact]
        public void The_crate_hands_to_a_wagon_through_the_slots()
        {
            Assert.Equal(TransferRoute.BetweenInventories, TransferRouting.Between(Crate(), Wagon()));
        }

        [Fact]
        public void A_wagon_hands_to_the_crate_through_the_slots()
        {
            Assert.Equal(TransferRoute.BetweenInventories, TransferRouting.Between(Wagon(), Crate()));
        }

        [Fact]
        public void The_crate_and_a_yard_keep_the_transfer_two_blocks_have()
        {
            // Both stand in the world, so the dock exchanges with paving exactly as a chute does
            // with a chest. This is the path that already works in game; it must not move.
            Assert.Equal(TransferRoute.BetweenPlaces, TransferRouting.Between(Crate(), Tile()));
            Assert.Equal(TransferRoute.BetweenPlaces, TransferRouting.Between(Tile(), Crate()));
        }

        [Fact]
        public void A_yard_and_a_wagon_go_through_the_world()
        {
            // The tile's inventory is only a reading of the piles on it. Writing into it would
            // leave the blocks where they stand, so this pair has to keep the world transfers.
            Assert.Equal(TransferRoute.PlaceToInventory, TransferRouting.Between(Tile(), Wagon()));
            Assert.Equal(TransferRoute.InventoryToPlace, TransferRouting.Between(Wagon(), Tile()));
        }

        [Fact]
        public void Two_wagons_exchange_directly()
        {
            Assert.Equal(TransferRoute.BetweenInventories, TransferRouting.Between(Wagon(), Wagon()));
        }

        [Fact]
        public void Two_tiles_are_two_places()
        {
            Assert.Equal(TransferRoute.BetweenPlaces, TransferRouting.Between(Tile(), Tile()));
        }

        [Fact]
        public void A_hold_with_neither_slots_nor_a_position_is_nothing()
        {
            // Not reachable from any finder written so far, and the point is that it stays that
            // way: a hold that offers no way in must be refused rather than half-transferred.
            ICargoHold nothing = new FakeHold("nothing", null, null, container: true);

            Assert.Equal(TransferRoute.None, TransferRouting.Between(nothing, Wagon()));
            Assert.Equal(TransferRoute.None, TransferRouting.Between(Wagon(), nothing));
            Assert.Equal(TransferRoute.None, TransferRouting.Between(null, Wagon()));
        }

        /// <summary>The dock's own crate: slots of its own, standing at a position.</summary>
        private static ICargoHold Crate()
        {
            return new FakeHold("dock", Inventory("dock"), new BlockPos(4, 5, 6), container: true);
        }

        /// <summary>A wagon: slots, and no position because it will drive away.</summary>
        private static ICargoHold Wagon()
        {
            return new FakeHold("wagon0", Inventory("wagon"), null, container: true);
        }

        /// <summary>A yard column: a position, and an inventory that is only a reading of it.</summary>
        private static ICargoHold Tile()
        {
            return new FakeHold("yard", Inventory("yard"), new BlockPos(9, 5, 9), container: false);
        }

        private static IInventory Inventory(string name)
        {
            return new InventoryGeneric(4, "signalslink-fake" + name, null);
        }

        private sealed class FakeHold : ICargoHold
        {
            public FakeHold(string code, IInventory inventory, BlockPos pos, bool container)
            {
                Code = code;
                Inventory = inventory;
                Pos = pos;
                IsContainer = container;
            }

            public string Code { get; }
            public IInventory Inventory { get; }
            public BlockPos Pos { get; }
            public bool IsContainer { get; }
            public void MarkDirty() { }
        }
    }
}
