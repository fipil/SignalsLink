using System.Collections.Generic;
using SignalsLink.src.signals.cargo;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>
    /// A holder shown to the paper as one inventory.
    ///
    /// Found in game with this section on a dock beside a boxcar:
    ///
    ///     from yard to train
    ///     game:firewood / in target / game:firewood 15-
    ///
    /// which should put sixteen logs on the train and put thirty-two on it instead — the boxcar
    /// has two storage points and the cap was asked of each of them separately. A train of four
    /// boxcars would have taken four times the number written. Nothing on the paper mentions
    /// wagons, so there was no way to read that off it.
    /// </summary>
    public class CompositeHoldTests
    {
        [Fact]
        public void Several_wagons_become_one_inventory()
        {
            IReadOnlyList<ICargoHold> ends = CompositeHold.Over(null, new ICargoHold[] { Wagon(4), Wagon(4) });

            Assert.Single(ends);
            Assert.Equal(8, ends[0].Inventory.Count);
        }

        [Fact]
        public void The_composed_slots_are_the_wagons_own()
        {
            // Nothing is copied: a stack written through the composed view is in the wagon.
            FakeHold first = Wagon(2);
            IReadOnlyList<ICargoHold> ends = CompositeHold.Over(null, new ICargoHold[] { first, Wagon(2) });

            ItemStack firewood = TestStacks.Item("game:firewood", 16);
            ends[0].Inventory[0].Itemstack = firewood;

            Assert.Same(firewood, first.Inventory[0].Itemstack);
        }

        [Fact]
        public void A_yard_is_left_alone()
        {
            // Columns of piles standing at positions. There is no one inventory to write into, and
            // the device goes on pairing them the long way round.
            ICargoHold[] tiles = { Tile(9, 5, 9), Tile(10, 5, 9) };

            Assert.Same(tiles, CompositeHold.Over(null, tiles));
        }

        [Fact]
        public void One_hold_is_already_one_inventory()
        {
            ICargoHold[] one = { Wagon(4) };

            Assert.Same(one, CompositeHold.Over(null, one));
            Assert.Null(CompositeHold.Over(null, null));
        }

        [Fact]
        public void A_mixed_end_is_left_alone()
        {
            // Not reachable from any finder written so far, and it must stay safe if one appears:
            // composing a place away would lose the only thing that says where it is.
            ICargoHold[] mixed = { Wagon(4), Tile(9, 5, 9) };

            Assert.Same(mixed, CompositeHold.Over(null, mixed));
        }

        [Fact]
        public void It_is_empty_only_while_every_wagon_is()
        {
            FakeHold loaded = Wagon(2);
            loaded.Inventory[0].Itemstack = TestStacks.Item("game:firewood", 4);

            Assert.False(CompositeHold.Over(null, new ICargoHold[] { Wagon(2), loaded })[0].IsEmpty);
            Assert.True(CompositeHold.Over(null, new ICargoHold[] { Wagon(2), Wagon(2) })[0].IsEmpty);
        }

        [Fact]
        public void Only_the_wagon_that_changed_is_told_it_changed()
        {
            // The reason this matters is two rooms away: the bridge to the other mod checks, after
            // the first transfer, that what a vehicle SAVES really changed. An untouched wagon told
            // it was written to would fail that check and stand the whole bridge down.
            FakeHold written = Wagon(2);
            FakeHold untouched = Wagon(2);

            written.Inventory[0].Itemstack = TestStacks.Item("game:firewood", 4);
            written.Inventory.MarkSlotDirty(0);

            CompositeHold.Over(null, new ICargoHold[] { written, untouched })[0].MarkDirty();

            Assert.Equal(1, written.DirtyCalls);
            Assert.Equal(0, untouched.DirtyCalls);
        }

        private static FakeHold Wagon(int slots)
        {
            return new FakeHold("wagon", new InventoryGeneric(slots, "signalslink-fakewagon", null), null);
        }

        private static FakeHold Tile(int x, int y, int z)
        {
            return new FakeHold("yard", new InventoryGeneric(1, "signalslink-faketile", null), new BlockPos(x, y, z));
        }

        private sealed class FakeHold : ICargoHold
        {
            public FakeHold(string code, InventoryGeneric inventory, BlockPos pos)
            {
                Code = code;
                Inventory = inventory;
                Pos = pos;
            }

            public string Code { get; }
            public InventoryGeneric Inventory { get; }
            public BlockPos Pos { get; }
            public int DirtyCalls { get; private set; }

            IInventory ICargoHold.Inventory => Inventory;

            public void MarkDirty()
            {
                DirtyCalls++;
            }
        }
    }
}
