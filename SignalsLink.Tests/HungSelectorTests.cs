using System.Collections.Generic;
using SignalsLink.src.signals.cargo;
using SignalsLink.src.signals.paperConditions;
using SignalsLink.src.signals.vehicle;
using Vintagestory.API.MathTools;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>
    /// Reading `unload boat north5` and `load elk`.
    ///
    /// The same direction words as a train, and nothing else: a boat or an animal has no wagons to
    /// count and no engine to name.
    /// </summary>
    public class HungSelectorTests
    {
        [Fact]
        public void A_bare_keyword_means_whatever_is_around()
        {
            HungSelector selector = Parse();

            Assert.Null(selector.Direction);
            Assert.Null(selector.Distance);
        }

        [Theory]
        [InlineData("north")]
        [InlineData("n")]
        [InlineData("NORTH")]
        public void A_direction_may_be_written_out_or_shortened(string token)
        {
            Assert.Equal(BlockFacing.NORTH, Parse(token).Direction);
        }

        [Fact]
        public void A_number_against_the_direction_is_how_many_blocks_away()
        {
            HungSelector selector = Parse("east5");

            Assert.Equal(BlockFacing.EAST, selector.Direction);
            Assert.Equal(5, selector.Distance);
        }

        [Fact]
        public void A_distance_out_of_reach_is_a_paper_error()
        {
            Assert.False(HungCargoHolderFinder.Boats().TryParseHeader(new[] { "north99" }, null, out _));
            Assert.False(HungCargoHolderFinder.Boats().TryParseHeader(new[] { "north0" }, null, out _));
        }

        [Theory]
        [InlineData("engine")]
        [InlineData("3")]
        [InlineData("up")]
        public void Anything_that_is_not_a_direction_is_a_paper_error(string token)
        {
            // Train words mean nothing here, and a vertical direction means nothing anywhere.
            var errors = new List<PaperConditionError>();
            var sink = new PaperErrorSink(errors) { CurrentLine = 4 };

            Assert.False(HungCargoHolderFinder.Elks().TryParseHeader(new[] { token }, sink, out _));

            Assert.Equal("vehiclespec", Assert.Single(errors).Reason);
            Assert.Equal(4, errors[0].Line);
            Assert.Contains(token, errors[0].Text);
        }

        [Fact]
        public void The_keywords_are_boat_and_elk()
        {
            Assert.Equal("boat", HungCargoHolderFinder.Boats().Keyword);
            Assert.Equal("elk", HungCargoHolderFinder.Elks().Keyword);
        }

        [Fact]
        public void Nothing_is_remembered_between_ticks()
        {
            // It moves; a remembered holder is one the goods could be written into after it left.
            Assert.False(HungCargoHolderFinder.Boats().Cacheable);
        }

        private static HungSelector Parse(params string[] tokens)
        {
            Assert.True(HungCargoHolderFinder.Boats().TryParseHeader(tokens, null, out ICargoSelector selector));
            return Assert.IsType<HungSelector>(selector);
        }
    }
}
