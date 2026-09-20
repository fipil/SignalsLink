using SignalsLink.src.signals.yard;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>
    /// The size a yard's name is written in. The game's text editor has always offered the choice
    /// on the sign; the sign took the text and dropped the size.
    /// </summary>
    public class YardSignFontTests
    {
        [Theory]
        [InlineData(14f)]
        [InlineData(22f)]
        [InlineData(40f)]
        public void A_size_the_editor_offers_is_kept_as_it_is(float size)
        {
            Assert.Equal(size, BEYardSign.ClampFontSize(size));
        }

        [Theory]
        [InlineData(3f, BEYardSign.MinFontSize)]
        [InlineData(500f, BEYardSign.MaxFontSize)]
        public void A_size_from_a_packet_is_held_to_what_the_editor_offers(float sent, float kept)
        {
            // The packet comes from a client; the board is only so big.
            Assert.Equal(kept, BEYardSign.ClampFontSize(sent));
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(-5f)]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        public void What_is_not_a_size_means_the_size_the_block_asks_for(float sent)
        {
            // Zero is how a sign saved before sizes existed reads, and it must look as it did.
            Assert.Equal(0f, BEYardSign.ClampFontSize(sent));
        }
    }
}
