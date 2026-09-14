using System.Collections.Generic;
using SignalsLink.src.signals.chunkanchor;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>
    /// An anchor must never unload half a train. Whatever it was set to hold, it also holds every
    /// column of a group that stands partly on its ground - and lets them go together.
    ///
    /// The one rule that needs care is when to stop: a train driving off is followed to the edge
    /// of the window and no further, or the anchor would go with it across the map.
    /// </summary>
    public class AnchorHaloTests
    {
        private const int Cx = 100, Cz = 100, Window = 7;

        [Fact]
        public void A_group_standing_partly_on_the_anchor_gets_the_rest_of_its_columns_held()
        {
            HashSet<long> halo = AnchorHalo.Compute(Base(K(100, 100)), null, Groups(Group(K(100, 100), K(100, 101), K(100, 102))), Cx, Cz, Window, out int guests);

            Assert.Equal(new HashSet<long> { K(100, 101), K(100, 102) }, halo);
            Assert.Equal(1, guests);
        }

        [Fact]
        public void A_group_entirely_on_the_anchor_needs_nothing_but_still_counts_as_a_guest()
        {
            // The guest count is what keeps the anchor from going to sleep on top of it.
            HashSet<long> halo = AnchorHalo.Compute(Base(K(100, 100), K(100, 101)), null, Groups(Group(K(100, 100), K(100, 101))), Cx, Cz, Window, out int guests);

            Assert.Empty(halo);
            Assert.Equal(1, guests);
        }

        [Fact]
        public void A_group_that_touches_nothing_of_the_anchors_is_none_of_its_business()
        {
            HashSet<long> halo = AnchorHalo.Compute(Base(K(100, 100)), null, Groups(Group(K(100, 103), K(100, 104))), Cx, Cz, Window, out int guests);

            Assert.Empty(halo);
            Assert.Equal(0, guests);
        }

        [Fact]
        public void A_group_that_has_moved_off_into_the_halo_is_still_held()
        {
            // It left the anchor's own columns but stands on ones held for it; dropping those now
            // would split it just the same.
            HashSet<long> previous = new HashSet<long> { K(100, 101), K(100, 102) };

            HashSet<long> halo = AnchorHalo.Compute(Base(K(100, 100)), previous, Groups(Group(K(100, 102), K(100, 103))), Cx, Cz, Window, out int guests);

            Assert.Equal(new HashSet<long> { K(100, 102), K(100, 103) }, halo);
            Assert.Equal(1, guests);
        }

        [Fact]
        public void But_never_past_the_window()
        {
            // 100+7 is the last column inside; 108 is out, and the train mod takes over there.
            HashSet<long> halo = AnchorHalo.Compute(Base(K(100, 100)), null, Groups(Group(K(100, 100), K(100, 107), K(100, 108))), Cx, Cz, Window, out _);

            Assert.Equal(new HashSet<long> { K(100, 107) }, halo);
        }

        [Fact]
        public void A_group_that_has_left_the_halo_is_let_go()
        {
            HashSet<long> previous = new HashSet<long> { K(100, 101) };

            HashSet<long> halo = AnchorHalo.Compute(Base(K(100, 100)), previous, Groups(Group(K(100, 105), K(100, 106))), Cx, Cz, Window, out int guests);

            Assert.Empty(halo);
            Assert.Equal(0, guests);
        }

        [Fact]
        public void A_group_in_one_column_cannot_be_split_and_is_ignored()
        {
            HashSet<long> halo = AnchorHalo.Compute(Base(K(100, 100)), null, Groups(Group(K(100, 100))), Cx, Cz, Window, out int guests);

            Assert.Empty(halo);
            Assert.Equal(0, guests);
        }

        [Fact]
        public void Two_groups_add_up()
        {
            HashSet<long> halo = AnchorHalo.Compute(Base(K(100, 100)), null,
                Groups(Group(K(100, 100), K(100, 101)), Group(K(100, 100), K(99, 100))), Cx, Cz, Window, out int guests);

            Assert.Equal(new HashSet<long> { K(100, 101), K(99, 100) }, halo);
            Assert.Equal(2, guests);
        }

        [Fact]
        public void Nothing_to_look_at_holds_nothing()
        {
            Assert.Empty(AnchorHalo.Compute(Base(K(100, 100)), null, null, Cx, Cz, Window, out int guests));
            Assert.Equal(0, guests);
        }

        private static long K(int x, int z) => AnchorArea.Key(x, z);

        private static HashSet<long> Base(params long[] keys) => new HashSet<long>(keys);

        private static IReadOnlyCollection<long> Group(params long[] keys) => new HashSet<long>(keys);

        private static IEnumerable<IReadOnlyCollection<long>> Groups(params IReadOnlyCollection<long>[] groups) => groups;
    }
}
