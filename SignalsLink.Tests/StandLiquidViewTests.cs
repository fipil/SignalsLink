using SignalsLink.src.signals.managedchute.transporting;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>
    /// A valve on a bucket stand sees the bucket as the liquid in it, at the bucket's own slot
    /// number; the other slots stay what they are.
    /// </summary>
    public class StandLiquidViewTests
    {
        [Fact]
        public void The_bucket_slot_is_replaced_and_the_rest_kept()
        {
            var real = new QuietInventory(2);
            real[0].Itemstack = TestStacks.Item("game:pressedmash-apple");
            var end = new FakeEnd(real, 1);

            var view = new StandLiquidView(real, end, null);

            Assert.Equal(2, view.Count);
            Assert.Same(real[0], view[0]);
            Assert.Same(view.Bucket, view[1]);
            Assert.Null(view[2]);
        }

        [Fact]
        public void Without_a_bucket_the_slot_holds_nothing()
        {
            var real = new QuietInventory(2);
            var view = new StandLiquidView(real, new FakeEnd(real, 1), null);

            Assert.False(view.Bucket.HasBucket);
            Assert.True(view.Bucket.Empty);
            Assert.Equal(0f, view.Bucket.CapacityLitres);
            Assert.False(view.Bucket.CanHold(new DummySlot(Water())));
            Assert.Equal(0, view.Bucket.GetRemainingSlotSpace(Water()));
        }

        [Fact]
        public void An_empty_bucket_takes_its_capacity_in_liquid_and_is_written_back()
        {
            var real = new QuietInventory(2);
            real[1].Itemstack = new ItemStack(new Bucket(10) { Code = new AssetLocation("game:woodbucket") });
            var end = new FakeEnd(real, 1);
            var view = new StandLiquidView(real, end, null);
            ItemStack water = Water();

            Assert.True(view.Bucket.HasBucket);
            Assert.Equal(10f, view.Bucket.CapacityLitres);
            Assert.True(view.Bucket.CanHold(new DummySlot(water)));
            Assert.Equal(1000, view.Bucket.GetRemainingSlotSpace(water));

            view.Bucket.Itemstack = water;
            view.Bucket.MarkDirty();

            Assert.True(real[1].Itemstack.Attributes.HasAttribute("contents"));
            Assert.Equal(1, end.Changes);
        }

        // ---------------------------------------------------------------- plumbing

        private static ItemStack Water() => TestStacks.Liquid(new Item(), "game:waterportion", 500);

        class Bucket : BlockLiquidContainerTopOpened
        {
            private readonly float capacity;

            public Bucket(float capacity)
            {
                this.capacity = capacity;
                // Reading the content asks the api for its world; an empty bucket needs nothing more.
                api = AnchorLifecycleTests.Proxy.Make<ICoreAPI>((m, a) => AnchorLifecycleTests.Proxy.Unhandled);
            }

            public override float CapacityLitres => capacity;
        }

        class FakeEnd : IBucketSlotEnd
        {
            private readonly IInventory inv;
            private readonly int index;
            public int Changes;
            public FakeEnd(IInventory inv, int index) { this.inv = inv; this.index = index; }
            public BlockPos Pos { get; } = new BlockPos(1, 2, 3, 0);
            public ItemSlot Slot => inv[index];
            public IInventory View => new SingleSlotView(Slot, null);
            public bool Accepts(ItemStack stack) => true;
            public void Changed(ICoreAPI api) => Changes++;
        }

        class QuietInventory : InventoryGeneric { public QuietInventory(int n) : base(n, "test-stand", null, (i, inv) => new QuietSlot(inv)) { } public override void DidModifyItemSlot(ItemSlot s, ItemStack extracted = null) { } }
        class QuietSlot : ItemSlot { public QuietSlot(InventoryBase inv) : base(inv) { } public override void OnItemSlotModified(ItemStack s) { } public override void MarkDirty() { } }
    }
}
