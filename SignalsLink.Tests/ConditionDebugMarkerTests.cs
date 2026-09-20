using SignalsLink.src.signals.paperConditions;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>
    /// Which papers ask to be traced. Getting this wrong is quiet in the worst way: the player
    /// writes the marker, sees no trace, and concludes the device is dead rather than the marker
    /// mistyped.
    /// </summary>
    public class ConditionDebugMarkerTests
    {
        [Theory]
        [InlineData("# debug")]
        [InlineData("#debug")]
        [InlineData("#DEBUG")]
        [InlineData("# Debug")]
        [InlineData("game:firewood\n#debug\n")]
        public void The_marker_is_recognised_however_it_is_written(string paper)
        {
            Assert.True(ConditionDebug.IsMarked(paper));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("game:firewood")]
        [InlineData("# debugging the yard")]
        public void And_an_ordinary_paper_is_left_alone(string paper)
        {
            // The last one is a comment that happens to begin with the word. It traces, and that
            // is accepted: a false yes costs some log, a false no costs an afternoon.
            Assert.Equal(paper != null && paper.Contains("debug"), ConditionDebug.IsMarked(paper));
        }
    }
}
