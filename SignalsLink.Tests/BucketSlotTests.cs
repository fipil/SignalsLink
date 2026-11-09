using System.Reflection;
using SignalsLink.src.signals.managedchute.transporting;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;
using NullApi = SignalsLink.Tests.ServerTransferRegressionTests.NullApi;

namespace SignalsLink.Tests
{
    /// <summary>
    /// A condenser spout and the bottom of a fruit press are bucket stands: to a device the block
    /// is that one slot. What stands there is what the vanilla interaction would take from a hand.
    /// </summary>
    public class BucketSlotTests
    {
        static readonly ICoreAPI Api = DispatchProxy.Create<ICoreAPI, NullApi>();
        static ItemStackMoveOperation Op(int n = 1) => new(null, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, n);
        static PaperConditionsEvaluator Eval(string paper) { var e = new PaperConditionsEvaluator(); e.SetConditionsText(paper); return e; }

        [Fact]
        public void The_condenser_takes_an_open_container_between_one_and_twenty_litres()
        {
            Assert.True(BucketSlotRules.CondenserAccepts(new Bucket(10)));
            Assert.True(BucketSlotRules.CondenserAccepts(new Bucket(1)));
            Assert.False(BucketSlotRules.CondenserAccepts(new Bucket(0.5f)));
            Assert.False(BucketSlotRules.CondenserAccepts(new Bucket(20)));
            Assert.False(BucketSlotRules.CondenserAccepts(TestStacks.Item("game:stick").Collectible));
            Assert.False(BucketSlotRules.CondenserAccepts(null));
        }

        [Fact]
        public void The_press_takes_an_open_container_under_twenty_litres()
        {
            Assert.True(BucketSlotRules.PressAccepts(new Bucket(10)));
            Assert.False(BucketSlotRules.PressAccepts(new Bucket(50)));
            Assert.False(BucketSlotRules.PressAccepts(TestStacks.Item("game:stick").Collectible));
        }

        [Fact]
        public void A_bucket_goes_onto_an_empty_stand_and_the_stand_is_told()
        {
            var source = new QuietInventory(2);
            source[0].Itemstack = TestStacks.Item("game:stick", 4);
            source[1].Itemstack = new ItemStack(new Bucket(10) { Code = new AssetLocation("game:woodbucket") });
            var end = new FakeEnd();

            var transfer = new InventoryToBucketSlotTransfer(Api, source, end, 0, Eval(""));

            Assert.Equal(1, transfer.TryMove(Op()).MovedAmount);
            Assert.Equal("game:woodbucket", end.Slot.Itemstack.Collectible.Code.ToString());
            Assert.True(source[1].Empty);
            Assert.Equal(4, source[0].StackSize);
            Assert.Equal(1, end.Changes);
        }

        [Fact]
        public void An_occupied_stand_takes_nothing_more()
        {
            var source = new QuietInventory(1);
            source[0].Itemstack = new ItemStack(new Bucket(10) { Code = new AssetLocation("game:woodbucket") });
            var end = new FakeEnd();
            end.Slot.Itemstack = new ItemStack(new Bucket(10) { Code = new AssetLocation("game:woodbucket") });

            var transfer = new InventoryToBucketSlotTransfer(Api, source, end, 0, Eval(""));

            Assert.False(transfer.TryMove(Op()).Success);
            Assert.Equal(1, source[0].StackSize);
            Assert.Equal(0, end.Changes);
        }

        // Conditions on a bucket read its liquid through the game api, so picking a bucket by
        // its content is left to the game; here the stand is exercised without a paper.

        [Fact]
        public void Taking_from_a_stand_goes_through_the_slot_view_and_redraws()
        {
            var end = new FakeEnd();
            end.Slot.Itemstack = new ItemStack(new Bucket(10) { Code = new AssetLocation("game:woodbucket") });
            var target = new QuietInventory(1);

            IItemTransfer inner = new InventoryToInventoryTransfer(Api, end.View, target, null, 0, 0, Eval(""));
            var transfer = new EndRefreshTransfer(Api, inner, end);

            Assert.Equal(1, transfer.TryMove(Op()).MovedAmount);
            Assert.True(end.Slot.Empty);
            Assert.Equal("game:woodbucket", target[0].Itemstack.Collectible.Code.ToString());
            Assert.Equal(1, end.Changes);
        }

        // ---------------------------------------------------------------- plumbing

        class Bucket : BlockLiquidContainerTopOpened
        {
            private readonly float capacity;
            public Bucket(float capacity) { this.capacity = capacity; }
            public override float CapacityLitres => capacity;
        }

        class FakeEnd : IBucketSlotEnd
        {
            private readonly QuietInventory holder = new QuietInventory(1);
            public int Changes;
            public BlockPos Pos { get; } = new BlockPos(1, 2, 3, 0);
            public ItemSlot Slot => holder[0];
            public IInventory View => new SingleSlotView(Slot, null);
            public bool Accepts(ItemStack stack) => BucketSlotRules.CondenserAccepts(stack?.Collectible);
            public void Changed(ICoreAPI api) => Changes++;
        }

        class QuietInventory : InventoryGeneric { public QuietInventory(int n) : base(n, "test-bucket", null, (i, inv) => new QuietSlot(inv)) { } public override void DidModifyItemSlot(ItemSlot s, ItemStack extracted = null) { } }
        class QuietSlot : ItemSlot { public QuietSlot(InventoryBase inv) : base(inv) { } public override void OnItemSlotModified(ItemStack s) { } public override void MarkDirty() { } }
    }
}
