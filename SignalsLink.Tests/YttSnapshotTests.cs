using Vintagestory.API.Datastructures;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>
    /// What a snapshot of a vehicle's saved goods is actually made of.
    ///
    /// The net under the bridge compares the saved tree before a transfer with the one after: the
    /// goods have changed, so the tree must have changed too. That is only true if the comparison
    /// can SEE a change. A snapshot that reads the same whatever is in the tree turns the net into
    /// a trap which fires on the first honest transfer.
    /// </summary>
    public class YttSnapshotTests
    {
        [Fact]
        public void A_tree_that_gained_goods_does_not_read_the_same()
        {
            TreeAttribute tree = new TreeAttribute();
            tree.SetString("slot0", "empty");

            string before = SignalsLink.YTT.src.probe.YttPersistence.Describe(tree);

            tree.SetString("slot0", "ingot-iron x16");

            Assert.NotEqual(before, SignalsLink.YTT.src.probe.YttPersistence.Describe(tree));
        }

        [Fact]
        public void A_tree_that_gained_a_key_does_not_read_the_same()
        {
            TreeAttribute tree = new TreeAttribute();
            tree.SetString("slot0", "ingot-iron x16");

            string before = SignalsLink.YTT.src.probe.YttPersistence.Describe(tree);

            tree.SetString("slot1", "firewood x8");

            Assert.NotEqual(before, SignalsLink.YTT.src.probe.YttPersistence.Describe(tree));
        }

        [Fact]
        public void A_nested_change_does_not_read_the_same()
        {
            // The other mod keeps one subtree per storage point, so everything that matters is a
            // level down. A snapshot that stops at the top would see nothing at all.
            TreeAttribute tree = new TreeAttribute();
            TreeAttribute point = new TreeAttribute();
            point.SetInt("qty", 0);
            tree["point0"] = point;

            string before = SignalsLink.YTT.src.probe.YttPersistence.Describe(tree);

            point.SetInt("qty", 16);

            Assert.NotEqual(before, SignalsLink.YTT.src.probe.YttPersistence.Describe(tree));
        }

        [Fact]
        public void An_unchanged_tree_reads_the_same()
        {
            TreeAttribute tree = new TreeAttribute();
            tree.SetString("slot0", "ingot-iron x16");

            Assert.Equal(SignalsLink.YTT.src.probe.YttPersistence.Describe(tree),
                SignalsLink.YTT.src.probe.YttPersistence.Describe(tree));
        }

        [Fact]
        public void Nothing_saved_at_all_is_not_a_description()
        {
            Assert.Null(SignalsLink.YTT.src.probe.YttPersistence.Describe(null));
        }
    }
}
