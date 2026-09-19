using System.Collections.Generic;
using SignalsLink.src.signals.cargo;
using SignalsLink.src.signals.paperConditions;
using SignalsLink.YTT.src.probe;
using SignalsLink.YTT.src.train;
using Vintagestory.API.Common;
using Xunit;
using static SignalsLink.Tests.AnchorLifecycleTests;

namespace SignalsLink.Tests
{
    /// <summary>
    /// Naming a kind of wagon: <c>load train fridge</c>, <c>load train fridge ice</c>.
    ///
    /// Found in play: a dock fills whatever hold is nearest and takes the goods, so firewood went
    /// into the refrigerated wagon. A wagon cannot be named in the other mod, and a number stops
    /// being true the day the train is coupled differently - the kind is what stays.
    /// </summary>
    public class TrainVehicleKindTests
    {
        // ---------------------------------------------------------------- the words

        [Theory]
        [InlineData("boxcar", TrainVehicleKind.Boxcar)]
        [InlineData("fridge", TrainVehicleKind.Fridge)]
        [InlineData("reefer", TrainVehicleKind.Fridge)]
        [InlineData("cart", TrainVehicleKind.Cart)]
        [InlineData("minecart", TrainVehicleKind.Cart)]
        [InlineData("FRIDGE", TrainVehicleKind.Fridge)]
        public void A_kind_is_a_word_in_the_header(string token, string kind)
        {
            TrainSelector selector = Parse(token);

            Assert.Equal(kind, selector.Kind);
            Assert.False(selector.IceOnly);
            Assert.False(selector.EngineOnly);
        }

        [Fact]
        public void No_kind_means_any_vehicle_as_it_always_did()
        {
            Assert.Null(Parse("north", "2").Kind);
        }

        [Fact]
        public void A_kind_sits_with_the_direction_and_the_number_in_any_order()
        {
            TrainSelector selector = Parse("2", "fridge", "south5");

            Assert.Equal(TrainVehicleKind.Fridge, selector.Kind);
            Assert.Equal(2, selector.WagonIndex);
            Assert.Equal(5, selector.Distance);
        }

        [Fact]
        public void Ice_asks_for_the_refrigerant_slots_and_names_the_kind_by_itself()
        {
            // Only a refrigerated wagon has them, so `load train ice` needs no `fridge`.
            TrainSelector selector = Parse("ice");

            Assert.True(selector.IceOnly);
            Assert.Equal(TrainVehicleKind.Fridge, selector.Kind);
        }

        [Fact]
        public void Ice_may_be_said_together_with_fridge()
        {
            TrainSelector selector = Parse("fridge", "ice", "2");

            Assert.True(selector.IceOnly);
            Assert.Equal(2, selector.WagonIndex);
        }

        [Theory]
        [InlineData("boxcar", "ice")]
        [InlineData("cart", "ice")]
        [InlineData("engine", "ice")]
        [InlineData("engine", "fridge")]
        [InlineData("engine", "boxcar")]
        public void Words_that_cannot_both_be_meant_are_a_paper_error(string first, string second)
        {
            // A boxcar has no ice slots and an engine is no wagon. Said while the paper is being
            // written, because a header that simply never matches is the hardest kind to debug.
            var errors = new List<PaperConditionError>();
            var sink = new PaperErrorSink(errors) { CurrentLine = 7 };

            Assert.False(new TrainCargoHolderFinder().TryParseHeader(new[] { first, second }, sink, out _));
            Assert.Equal("holderspec", Assert.Single(errors).Reason);
            Assert.Equal(7, errors[0].Line);
        }

        // ---------------------------------------------------------------- telling a kind

        [Theory]
        [InlineData("yangtransport:sgwagon_boxcar", TrainVehicleKind.Boxcar, true)]
        [InlineData("yangtransport:sgwagon_early_boxcar", TrainVehicleKind.Boxcar, true)]
        [InlineData("yangtransport:sgwagon_fridge", TrainVehicleKind.Boxcar, false)]
        [InlineData("yangtransport:sgwagon_fridge", TrainVehicleKind.Fridge, true)]
        [InlineData("yangtransport:sgwagon_boxcar", TrainVehicleKind.Fridge, false)]
        [InlineData("yangtransport:minecart", TrainVehicleKind.Cart, true)]
        [InlineData("yangtransport:enginecart", TrainVehicleKind.Cart, false)]
        [InlineData("yangtransport:sglocomotive", TrainVehicleKind.Boxcar, false)]
        [InlineData("yangtransport:sgwagon_passenger", TrainVehicleKind.Boxcar, false)]
        [InlineData("game:boat-sailed", TrainVehicleKind.Cart, false)]      // another mod's, whatever it is called
        [InlineData("othermod:sgwagon_fridge", TrainVehicleKind.Fridge, false)]
        public void A_vehicle_is_told_by_its_entity_code(string code, string kind, bool matches)
        {
            Assert.Equal(matches, TrainVehicleKind.Matches(kind, new AssetLocation(code)));
        }

        [Fact]
        public void No_kind_asked_for_matches_everything()
        {
            Assert.True(TrainVehicleKind.Matches(null, new AssetLocation("yangtransport:sglocomotive")));
            Assert.True(TrainVehicleKind.Matches(null, null));
        }

        [Fact]
        public void A_kind_this_does_not_know_matches_nothing()
        {
            Assert.False(TrainVehicleKind.Matches("tanker", new AssetLocation("yangtransport:sgwagon_boxcar")));
            Assert.False(TrainVehicleKind.Matches(TrainVehicleKind.Fridge, null));
        }

        // ---------------------------------------------------------------- the place in the train

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 2)]
        [InlineData(4, 5)]
        public void The_other_mods_own_numbering_is_used_when_it_is_there(int convoyIndex, int place)
        {
            // It counts from zero, the paper from one. The distance says nothing here: a boxcar is
            // seven blocks long, and guessing from it put the second wagon at eight.
            Assert.Equal(place, TrainVehicleKind.PlaceInConvoy(convoyIndex, distanceBehindHead: 8.4 * convoyIndex));
        }

        [Theory]
        [InlineData(0.0, 1)]
        [InlineData(1.2, 2)]
        [InlineData(2.4, 3)]
        public void Without_it_the_place_is_guessed_from_the_distance_as_for_mine_carts(double distance, int place)
        {
            Assert.Equal(place, TrainVehicleKind.PlaceInConvoy(null, distance));
        }

        [Fact]
        public void A_number_that_makes_no_sense_is_not_believed()
        {
            Assert.Equal(3, TrainVehicleKind.PlaceInConvoy(-1, 2.4));
            Assert.Equal(1, TrainVehicleKind.PlaceInConvoy(null, -5));
        }

        // ---------------------------------------------------------------- which one of its kind

        [Fact]
        public void A_number_with_a_kind_counts_among_that_kind_from_the_head()
        {
            // engine 0, boxcar 8, fridge 16, boxcar 24, fridge 32: the fridges ride at 16 and 32.
            double[] fridges = { 16, 32 };

            Assert.Equal(1, TrainVehicleKind.RankAmong(16, fridges));
            Assert.Equal(2, TrainVehicleKind.RankAmong(32, fridges));
        }

        [Fact]
        public void It_does_not_matter_in_what_order_they_were_found()
        {
            Assert.Equal(3, TrainVehicleKind.RankAmong(40, new double[] { 40, 8, 24 }));
        }

        [Fact]
        public void A_vehicle_does_not_count_itself_nor_a_float_that_is_nearly_itself()
        {
            Assert.Equal(1, TrainVehicleKind.RankAmong(16, new[] { 16.0, 16.0004, 15.9997 }));
        }

        [Fact]
        public void Alone_of_its_kind_it_is_the_first()
        {
            Assert.Equal(1, TrainVehicleKind.RankAmong(24, new double[0]));
            Assert.Equal(1, TrainVehicleKind.RankAmong(24, null));
        }

        // ---------------------------------------------------------------- reaching the ice slots

        [Fact]
        public void The_refrigerant_slots_are_found_even_when_they_are_not_public()
        {
            Assert.NotNull(YttSurface.RefrigerantInventoryOn(typeof(AsTheOtherModHasIt)));
        }

        [Fact]
        public void A_behavior_that_lost_them_or_changed_their_type_stands_this_down_alone()
        {
            // The probe says no; cargo is probed elsewhere and knows nothing of this.
            Assert.Null(YttSurface.RefrigerantInventoryOn(typeof(WithoutAnInventory)));
            Assert.Null(YttSurface.RefrigerantInventoryOn(typeof(WithSomethingElseUnderThatName)));
            Assert.Null(YttSurface.RefrigerantInventoryOn(null));
        }

        [Fact]
        public void The_save_key_is_read_off_the_other_mods_own_constant()
        {
            // So renaming it there does not quietly blind the check that ice really gets saved.
            Assert.Equal("somewhereElse", YttSurface.RefrigerantTreeKeyOn(typeof(AsTheOtherModHasIt)));
        }

        [Fact]
        public void Without_that_constant_the_name_known_today_is_used()
        {
            Assert.Equal(YttSurface.DefaultRefrigerantTreeKey, YttSurface.RefrigerantTreeKeyOn(typeof(WithoutAnInventory)));
            Assert.Equal(YttSurface.DefaultRefrigerantTreeKey, YttSurface.RefrigerantTreeKeyOn(null));
        }

        [Fact]
        public void Nothing_is_reached_without_a_probe_behind_it()
        {
            // The vocabulary-only finder on a client, or a version where the probe said no.
            Assert.Null(new YttRefrigeration(null, null).InventoryOf(null));
            Assert.Null(new YttRefrigeration(new YttSurface(), null).InventoryOf(null));
        }

        [Fact]
        public void Ice_that_is_not_being_saved_costs_the_ice_and_not_the_cargo()
        {
            // An inventory nobody from the other mod listens to: what is put there is never saved.
            ICoreAPI api = Proxy.Make<ICoreAPI>((method, args) => Proxy.Unhandled);
            YttPersistence cargo = new YttPersistence(api, typeof(TrainVehicleKindTests).Assembly);
            YttPersistence ice = cargo.For(YttSurface.DefaultRefrigerantTreeKey, "only the ice");

            Assert.False(ice.IsWired(new InventoryGeneric(1, "nobody-listens", null)));

            Assert.False(ice.Trusted);
            Assert.True(cargo.Trusted);
        }

        private sealed class AsTheOtherModHasIt
        {
            internal const string InventoryTreeAttribute = "somewhereElse";

            internal InventoryGeneric Inventory => null;
        }

        private sealed class WithoutAnInventory
        {
        }

        private sealed class WithSomethingElseUnderThatName
        {
            internal string Inventory => "";
        }

        private static TrainSelector Parse(params string[] tokens)
        {
            Assert.True(new TrainCargoHolderFinder().TryParseHeader(tokens, null, out ICargoSelector selector));
            return Assert.IsType<TrainSelector>(selector);
        }
    }
}
