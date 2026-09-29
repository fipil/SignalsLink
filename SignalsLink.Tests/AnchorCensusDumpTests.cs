using System.Linq;
using System.Text;
using SignalsLink.src.signals.chunkanchor;
using Vintagestory.API.Common;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>
    /// <c>/slanchor dump</c>: a small station came to nine hundred active blocks and nobody could
    /// say of what. The dump is only worth having if it reads the same way every time and puts
    /// the big numbers first.
    /// </summary>
    public class AnchorCensusDumpTests
    {
        [Fact]
        public void The_most_numerous_comes_first()
        {
            CensusTally tally = new CensusTally();
            tally.Add("game:chest", 2);
            tally.Add("yangtransport:widerails", 400);
            tally.Add("game:farmland", 30);

            Assert.Equal(new[] { "yangtransport:widerails", "game:farmland", "game:chest" }, tally.Ranked().Select(p => p.Key));
            Assert.Equal(432, tally.Total);
        }

        [Fact]
        public void Equal_counts_are_ordered_by_name_so_two_dumps_can_be_compared()
        {
            CensusTally tally = new CensusTally();
            tally.Add("b"); tally.Add("a"); tally.Add("c");

            Assert.Equal(new[] { "a", "b", "c" }, tally.Ranked().Select(p => p.Key));
        }

        [Fact]
        public void The_same_name_again_adds_up()
        {
            CensusTally tally = new CensusTally();
            tally.Add("game:chest"); tally.Add("game:chest"); tally.Add("game:chest", 3);

            Assert.Equal(5, Assert.Single(tally.Ranked()).Value);
        }

        [Fact]
        public void Something_without_a_name_is_still_counted()
        {
            CensusTally tally = new CensusTally();
            tally.Add(null); tally.Add("");

            Assert.Equal("?", Assert.Single(tally.Ranked()).Key);
            Assert.Equal(2, tally.Total);
        }

        [Fact]
        public void The_table_says_its_total_and_lists_every_line()
        {
            CensusTally tally = new CensusTally();
            tally.Add("game:chest", 2);
            tally.Add("game:firepit", 1);

            StringBuilder text = new StringBuilder();
            tally.WriteTo(text, "ACTIVE BLOCKS");

            string written = text.ToString();
            Assert.StartsWith("ACTIVE BLOCKS (3)", written);
            Assert.Contains("game:chest", written);
            Assert.Contains("game:firepit", written);
        }

        [Theory]
        [InlineData("game:log-placed-oak-ud", "game:log")]
        [InlineData("yangtransport:widerails_straight-we-metal", "yangtransport:widerails_straight")]
        [InlineData("game:chest", "game:chest")]
        public void Variants_are_gathered_into_their_family(string code, string family)
        {
            // Four hundred lines of rail variants hide the one fact that they are all rails.
            Assert.Equal(family, AnchorCensusDump.Family(new AssetLocation(code)));
        }

        [Fact]
        public void No_code_is_a_family_of_its_own()
        {
            Assert.Equal("?", AnchorCensusDump.Family(null));
        }
    }
}
