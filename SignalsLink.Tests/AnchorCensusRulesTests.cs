using System.Collections.Generic;
using SignalsLink.src.signals.chunkanchor;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>
    /// What an anchor is charged for.
    ///
    /// Measured on a small station with two chiselled houses: 950 block entities, of which 730
    /// were chiselled blocks, 112 were piles on the storage yard, and 28 were the machines the
    /// anchor was put there for - plus monsters in the caves underneath at ten blocks apiece.
    /// A gear lasted nineteen days. What counts now is what the server works on.
    /// </summary>
    public class AnchorCensusRulesTests
    {
        // ---------------------------------------------------------------- blocks

        [Fact]
        public void A_block_that_ticks_is_an_active_block()
        {
            Assert.True(AnchorCensus.IsActive(new Machine(ticking: true)));
        }

        [Fact]
        public void A_block_that_only_holds_something_is_not()
        {
            // A chest, a trapdoor, a sign: state of its own, and nothing the server does about it.
            Assert.False(AnchorCensus.IsActive(new Machine(ticking: false)));
        }

        [Fact]
        public void A_chiselled_block_never_counts()
        {
            Assert.False(AnchorCensus.IsActive(new Chiselled(ticking: false)));
            Assert.False(AnchorCensus.IsActive(new Chiselled(ticking: true)));
        }

        [Fact]
        public void A_pile_on_the_ground_never_counts_even_while_it_burns()
        {
            // A burning pile does tick. A yard of them must not start to cost because of it.
            Assert.False(AnchorCensus.IsActive(new Pile(ticking: false)));
            Assert.False(AnchorCensus.IsActive(new Pile(ticking: true)));
        }

        [Fact]
        public void Nothing_is_not_a_block()
        {
            Assert.False(AnchorCensus.IsActive(null));
        }

        // ---------------------------------------------------------------- animals

        [Theory]
        [InlineData("game:pig-wild-female", 1)]
        [InlineData("game:sheep-bighorn-male", 4)]
        [InlineData("game:chicken-hen", 2)]
        public void An_animal_born_in_captivity_is_somebodys_animal(string code, int generation)
        {
            Assert.True(AnchorCensus.IsLivestock(new AssetLocation(code), generation));
        }

        [Fact]
        public void A_tamed_elk_is_too_whatever_its_generation()
        {
            Assert.True(AnchorCensus.IsLivestock(new AssetLocation("game:tameddeer-female"), 0));
        }

        [Theory]
        [InlineData("game:deer-marsh-baby-female")]   // walked through the station
        [InlineData("game:pig-wild-male")]            // the same species as livestock, but wild
        [InlineData("game:drifter-deep")]             // in the caves underneath
        [InlineData("game:bowtorn-surface")]
        [InlineData("game:wolf-male")]
        public void Wild_animals_and_monsters_are_not(string code)
        {
            Assert.False(AnchorCensus.IsLivestock(new AssetLocation(code), 0));
        }

        [Fact]
        public void Nothing_is_not_an_animal()
        {
            Assert.False(AnchorCensus.IsLivestock(null, 0));
            Assert.False(AnchorCensus.IsLivestock(null));
        }

        // ---------------------------------------------------------------- stand-ins

        private sealed class Machine : BlockEntity
        {
            public Machine(bool ticking) { if (ticking) TickHandlers = new List<long> { 1 }; }
        }

        private sealed class Chiselled : BlockEntityMicroBlock
        {
            public Chiselled(bool ticking) { if (ticking) TickHandlers = new List<long> { 1 }; }
        }

        private sealed class Pile : BlockEntityGroundStorage
        {
            public Pile(bool ticking) { if (ticking) TickHandlers = new List<long> { 1 }; }
        }
    }
}
