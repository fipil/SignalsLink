using SignalsLink.src.signals.cargo;
using SignalsLink.YTT.src.train;
using Vintagestory.API.MathTools;

namespace SignalsLink.Tests
{
    /// <summary>
    /// Reading `unload train north 3 engine`.
    ///
    /// The header vocabulary belongs to the finder and nowhere else - which is exactly why it is
    /// worth testing here, where no game is needed to find out what a word means.
    /// </summary>
    public class TrainSelectorTests
    {
        [Fact]
        public void A_bare_train_means_the_whole_convoy_all_round()
        {
            TrainSelector selector = Parse();

            Assert.Null(selector.Direction);
            Assert.Null(selector.WagonIndex);
            Assert.False(selector.EngineOnly);
        }

        [Theory]
        [InlineData("north")]
        [InlineData("n")]
        [InlineData("NORTH")]
        public void A_direction_may_be_written_out_or_shortened(string token)
        {
            Assert.Equal(BlockFacing.NORTH, Parse(token).Direction);
        }

        [Theory]
        [InlineData("s", "south")]
        [InlineData("e", "east")]
        [InlineData("w", "west")]
        public void Every_direction_has_its_letter(string letter, string word)
        {
            Assert.Equal(Parse(word).Direction, Parse(letter).Direction);
        }

        [Theory]
        [InlineData("north5", 5)]
        [InlineData("n5", 5)]
        [InlineData("WEST12", 12)]
        public void A_number_against_the_direction_is_how_many_blocks_away(string token, int steps)
        {
            Assert.Equal(steps, Parse(token).Distance);
        }

        [Fact]
        public void A_bare_direction_asks_for_no_particular_distance()
        {
            Assert.Null(Parse("north").Distance);
        }

        [Fact]
        public void The_distance_does_not_eat_the_wagon_number()
        {
            // Why it is written against the word and not after it: `train north 5` would leave
            // the 5 arguing with the wagon number over the same token.
            TrainSelector selector = Parse("north5", "2");

            Assert.Equal(5, selector.Distance);
            Assert.Equal(2, selector.WagonIndex);
        }

        [Fact]
        public void A_distance_out_of_reach_is_a_paper_error()
        {
            // Past the search radius it could never match, and a header that matches nothing is
            // the hardest kind to debug.
            Assert.False(new TrainCargoHolderFinder().TryParseHeader(new[] { "north99" }, null, out _));
            Assert.False(new TrainCargoHolderFinder().TryParseHeader(new[] { "north0" }, null, out _));
        }

        [Fact]
        public void A_number_is_the_wagon_counted_from_the_head()
        {
            Assert.Equal(3, Parse("3").WagonIndex);
        }

        [Fact]
        public void Engine_asks_for_the_steam_engine_holds()
        {
            Assert.True(Parse("engine").EngineOnly);
        }

        [Fact]
        public void The_tokens_do_not_depend_on_their_order()
        {
            TrainSelector selector = Parse("engine", "2", "west");

            Assert.Equal(BlockFacing.WEST, selector.Direction);
            Assert.Equal(2, selector.WagonIndex);
            Assert.True(selector.EngineOnly);
        }

        [Fact]
        public void A_word_it_does_not_know_is_a_paper_error()
        {
            Assert.False(new TrainCargoHolderFinder().TryParseHeader(new[] { "sideways" }, null, out _));
        }

        [Fact]
        public void The_mistake_is_named_so_the_player_can_see_which_word_it_was()
        {
            var errors = new System.Collections.Generic.List<SignalsLink.src.signals.paperConditions.PaperConditionError>();
            var sink = new SignalsLink.src.signals.paperConditions.PaperErrorSink(errors) { CurrentLine = 4 };

            new TrainCargoHolderFinder().TryParseHeader(new[] { "sideways" }, sink, out _);

            Assert.Equal("holderspec", Assert.Single(errors).Reason);
            Assert.Equal(4, errors[0].Line);
            Assert.Contains("sideways", errors[0].Text);
        }

        [Fact]
        public void A_wagon_number_has_to_be_a_wagon_number()
        {
            // Zero and negatives are not wagons, and swallowing them would mean a header that
            // silently matches nothing.
            Assert.False(new TrainCargoHolderFinder().TryParseHeader(new[] { "0" }, null, out _));
            Assert.False(new TrainCargoHolderFinder().TryParseHeader(new[] { "-2" }, null, out _));
        }

        [Fact]
        public void The_keyword_is_train()
        {
            Assert.Equal("train", new TrainCargoHolderFinder().Keyword);
        }

        // ---------------------------------------------------------------- plumbing

        private static TrainSelector Parse(params string[] tokens)
        {
            Assert.True(new TrainCargoHolderFinder().TryParseHeader(tokens, null, out ICargoSelector selector));
            return Assert.IsType<TrainSelector>(selector);
        }
    }
}
