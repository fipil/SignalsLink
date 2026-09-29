using SignalsLink.src.signals.managedchute.transporting;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;
using static SignalsLink.Tests.AnchorLifecycleTests;

namespace SignalsLink.Tests
{
    /// <summary>
    /// Found in game: a damper emptied a scroll rack into a chest, and the rack went on showing
    /// scrolls nobody could take. A rack draws what it holds and redraws only when its block
    /// entity is sent again; marking a slot dirty is enough for a chest and not for a rack.
    /// </summary>
    public class DisplayRefreshTests
    {
        private static readonly BlockPos Here = new BlockPos(3, 4, 5);

        [Fact]
        public void A_rack_is_sent_again_after_goods_moved()
        {
            FakeRack rack = new FakeRack();

            DisplayRefresh.After(ApiWith(rack), InventoryAt(Here));

            Assert.True(rack.SentWithRedraw);
        }

        [Fact]
        public void A_chest_is_left_alone()
        {
            // Its window is kept up to date slot by slot; sending the whole block is traffic for nothing.
            FakeChest chest = new FakeChest();

            DisplayRefresh.After(ApiWith(chest), InventoryAt(Here));

            Assert.False(chest.Sent);
        }

        [Fact]
        public void An_inventory_that_stands_nowhere_is_left_alone()
        {
            // A wagon or a bag: there is no block to send.
            DisplayRefresh.After(ApiWith(new FakeRack()), InventoryAt(null));
            DisplayRefresh.After(ApiWith(new FakeRack()), null);
        }

        private static InventoryGeneric InventoryAt(BlockPos pos)
        {
            return new InventoryGeneric(1, "test-refresh", null) { Pos = pos };
        }

        private static ICoreAPI ApiWith(BlockEntity entity)
        {
            return Proxy.Make<ICoreAPI>((method, args) =>
                method.Name == "GetBlockEntity" && Here.Equals(args[0]) ? entity : Proxy.Unhandled);
        }

        private sealed class FakeRack : BlockEntityDisplay
        {
            private readonly InventoryGeneric inventory = new InventoryGeneric(1, "test-rack", null);

            public bool SentWithRedraw;

            public override InventoryBase Inventory => inventory;

            public override string InventoryClassName => "test-rack";

            public override void MarkDirty(bool redrawOnClient = false, IPlayer skipPlayer = null)
            {
                SentWithRedraw = redrawOnClient;
            }

            protected override float[][] genTransformationMatrices() => null;
        }

        private sealed class FakeChest : BlockEntity
        {
            public bool Sent;

            public override void MarkDirty(bool redrawOnClient = false, IPlayer skipPlayer = null)
            {
                Sent = true;
            }
        }
    }
}
