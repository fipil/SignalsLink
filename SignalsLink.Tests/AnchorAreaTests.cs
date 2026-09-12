using System.Collections.Generic;
using SignalsLink.src.signals.chunkanchor;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>
    /// Which columns an anchor may hold.
    ///
    /// The same arithmetic decides three things that must agree: what the map lets the player
    /// click, what the packet is allowed to carry, and what the server claims. So it is tested
    /// here once, away from all three.
    /// </summary>
    public class AnchorAreaTests
    {
        private const int Cx = 100;
        private const int Cz = -40;

        [Fact]
        public void A_column_survives_being_turned_into_a_number_and_back()
        {
            // Negative coordinates are the ones that break a hand-rolled packing, and half the
            // world has them.
            foreach ((int x, int z) in new[] { (0, 0), (1, 1), (-1, -1), (100, -40), (-2147483, 2147483) })
            {
                Assert.Equal((x, z), AnchorArea.Of(AnchorArea.Key(x, z)));
            }
        }

        [Fact]
        public void Distinct_columns_never_share_a_number()
        {
            HashSet<long> keys = new HashSet<long>();

            for (int x = -20; x <= 20; x++)
            for (int z = -20; z <= 20; z++)
            {
                Assert.True(keys.Add(AnchorArea.Key(x, z)), "collision at " + x + "," + z);
            }
        }

        [Fact]
        public void A_new_anchor_holds_only_the_ground_it_stands_on()
        {
            HashSet<long> held = AnchorArea.JustTheAnchor(Cx, Cz);

            Assert.Single(held);
            Assert.Contains(AnchorArea.Key(Cx, Cz), held);
        }

        [Fact]
        public void Clicking_a_column_turns_it_on_and_clicking_again_turns_it_off()
        {
            HashSet<long> held = AnchorArea.JustTheAnchor(Cx, Cz);

            Assert.True(AnchorArea.Toggle(held, Cx + 2, Cz, Cx, Cz));
            Assert.Equal(2, held.Count);

            Assert.True(AnchorArea.Toggle(held, Cx + 2, Cz, Cx, Cz));
            Assert.Single(held);
        }

        [Fact]
        public void The_anchors_own_column_can_never_be_given_up()
        {
            // Letting it go would unload the anchor itself - after which it can no longer let go
            // of anything else either.
            HashSet<long> held = AnchorArea.JustTheAnchor(Cx, Cz);

            Assert.False(AnchorArea.Toggle(held, Cx, Cz, Cx, Cz));
            Assert.Single(held);
        }

        [Theory]
        [InlineData(7, 0)]
        [InlineData(0, -7)]
        [InlineData(7, 7)]
        public void The_edge_of_the_window_is_still_inside_it(int dx, int dz)
        {
            Assert.True(AnchorArea.InWindow(Cx + dx, Cz + dz, Cx, Cz));
        }

        [Theory]
        [InlineData(8, 0)]
        [InlineData(0, -8)]
        [InlineData(7, 8)]
        public void And_one_column_further_is_not(int dx, int dz)
        {
            HashSet<long> held = AnchorArea.JustTheAnchor(Cx, Cz);

            Assert.False(AnchorArea.InWindow(Cx + dx, Cz + dz, Cx, Cz));
            Assert.False(AnchorArea.Toggle(held, Cx + dx, Cz + dz, Cx, Cz));
            Assert.Single(held);
        }

        [Fact]
        public void A_selection_arriving_from_a_client_is_cut_down_to_what_is_allowed()
        {
            // Nothing stops someone sending a set that holds the whole world. The rule is applied
            // again where it is claimed, not only where it is drawn.
            long[] wanted =
            {
                AnchorArea.Key(Cx + 1, Cz + 1),
                AnchorArea.Key(Cx + 200, Cz),
                AnchorArea.Key(Cx, Cz - 4000)
            };

            HashSet<long> clean = AnchorArea.Sanitise(wanted, Cx, Cz);

            Assert.Equal(2, clean.Count);
            Assert.Contains(AnchorArea.Key(Cx, Cz), clean);
            Assert.Contains(AnchorArea.Key(Cx + 1, Cz + 1), clean);
        }

        [Fact]
        public void And_always_comes_back_holding_the_anchors_own_column()
        {
            Assert.Contains(AnchorArea.Key(Cx, Cz), AnchorArea.Sanitise(null, Cx, Cz));
            Assert.Contains(AnchorArea.Key(Cx, Cz), AnchorArea.Sanitise(new long[0], Cx, Cz));
        }

        [Fact]
        public void The_fixed_square_of_the_first_generation_reads_back_as_columns()
        {
            // How an anchor saved by the old build is understood, so nothing standing in a world
            // quietly stops holding what it held.
            HashSet<long> square = AnchorArea.Square(Cx, Cz, 3);

            Assert.Equal(49, square.Count);
            Assert.Contains(AnchorArea.Key(Cx, Cz), square);
            Assert.Contains(AnchorArea.Key(Cx - 3, Cz + 3), square);
            Assert.DoesNotContain(AnchorArea.Key(Cx - 4, Cz), square);
        }
    }
}
