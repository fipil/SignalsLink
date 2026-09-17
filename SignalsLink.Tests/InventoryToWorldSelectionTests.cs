using System.Reflection;
using SignalsLink.src.signals.managedchute.transporting;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>
    /// Which slot the chute sets down from, decided before anything is placed.
    ///
    /// Found in game: two chests in the first slot, a full bucket in the second, target signal 2.
    /// The chests were chosen, could not be placed, and the bucket was never tried - until the
    /// chests were taken out.
    /// </summary>
    public class InventoryToWorldSelectionTests
    {
        private const byte Throw = 0;
        private const byte PlaceBlock = 1;
        private const byte BucketOrPile = 2;

        [Theory]
        [InlineData("")]
        [InlineData("game:*\n")]
        public void A_bucket_behind_chests_is_still_set_down(string paper)
        {
            // With and without a paper: both walks ask the same question of each slot.
            QuietChest source = Slots(Chests(2), Bucket());

            Assert.Same(source[1], Pick(BucketOrPile, source, paper));
        }

        [Fact]
        public void A_pile_is_started_only_from_something_pileable()
        {
            QuietChest source = Slots(Firewood(), Ingots());

            Assert.Same(source[1], Pick(BucketOrPile, source));
        }

        [Fact]
        public void Placing_a_block_passes_over_plain_items()
        {
            QuietChest source = Slots(Firewood(), Chests(1));

            Assert.Same(source[1], Pick(PlaceBlock, source));
        }

        [Fact]
        public void A_thrown_slot_may_hold_anything()
        {
            QuietChest source = Slots(Chests(2), Bucket());

            Assert.Same(source[0], Pick(Throw, source));
        }

        [Fact]
        public void Nothing_is_chosen_when_nothing_could_be_set_down()
        {
            Assert.Null(Pick(BucketOrPile, Slots(Chests(2), Firewood())));
        }

        private static ItemSlot Pick(byte mode, QuietChest source, string paper = "")
        {
            PaperConditionsEvaluator evaluator = new PaperConditionsEvaluator();
            evaluator.SetConditionsText(paper);

            return new Probe(source, mode, evaluator).Pick();
        }

        private static QuietChest Slots(params ItemStack[] stacks)
        {
            QuietChest inventory = new QuietChest(stacks.Length);
            for (int i = 0; i < stacks.Length; i++) inventory[i].Itemstack = stacks[i];

            return inventory;
        }

        private static ItemStack Chests(int count) => new ItemStack(new Block { Code = new AssetLocation("game:chest") }, count);

        private static ItemStack Bucket() => new ItemStack(new TestBucket { Code = new AssetLocation("game:bucket") }, 1);

        private static ItemStack Firewood() => new ItemStack(new Item { Code = new AssetLocation("game:firewood") }, 8);

        private static ItemStack Ingots() => new ItemStack(new TestPileable { Code = new AssetLocation("game:ingot-copper") }, 4);

        /// <summary>No world behind it: the target is nowhere, so only the choice can be watched.</summary>
        private sealed class Probe : InventoryToWorldTransfer
        {
            public Probe(InventoryBase source, byte mode, PaperConditionsEvaluator evaluator)
                : base(DispatchProxy.Create<ICoreAPI, ServerTransferRegressionTests.NullApi>(), source, 0, null, mode, evaluator)
            {
            }

            public ItemSlot Pick() => GetSourceSlot();
        }

        /// <summary>An empty bucket that never asks the world what it holds.</summary>
        private sealed class TestBucket : BlockLiquidContainerBase
        {
            public TestBucket()
            {
                api = DispatchProxy.Create<ICoreAPI, ServerTransferRegressionTests.NullApi>();
            }

            public override ItemStack[] GetContents(IWorldAccessor world, ItemStack itemstack) => null;
        }

        /// <summary>A chest that does not tell anyone when a slot changes: there is no one to tell.</summary>
        private sealed class QuietChest : InventoryGeneric
        {
            public QuietChest(int slots) : base(slots, "test-world", null, (i, inv) => new QuietSlot(inv))
            {
            }

            public override void DidModifyItemSlot(ItemSlot slot, ItemStack extracted = null)
            {
            }
        }

        private sealed class QuietSlot : ItemSlot
        {
            public QuietSlot(InventoryBase inventory) : base(inventory)
            {
            }

            public override void OnItemSlotModified(ItemStack stack)
            {
            }

            public override void MarkDirty()
            {
            }
        }

        private sealed class TestPileable : ItemPileable
        {
            protected override AssetLocation PileBlockCode => new AssetLocation("game:ingotpile");
        }
    }
}
