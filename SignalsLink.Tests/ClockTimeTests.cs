using SignalsLink.src.signals.clock;
using Xunit;

namespace SignalsLink.Tests;

/// <summary>The clock reads the game day as 4096 steps and three hex digits.</summary>
public class ClockTimeTests
{
    [Theory]
    [InlineData(0.0, "0:0:0")]      // midnight
    [InlineData(6.0, "4:0:0")]      // six in the morning
    [InlineData(12.0, "8:0:0")]     // noon
    [InlineData(18.0, "C:0:0")]     // six in the evening
    [InlineData(23.999, "F:F:F")]   // the last step before midnight
    public void Reads_the_hour_of_a_24_hour_day_as_hex_digits(double hour, string expected)
    {
        Assert.Equal(expected, ClockTime.Format(ClockTime.StepOf(hour, 24)));
    }

    [Fact]
    public void A_day_has_4096_steps_of_equal_length()
    {
        Assert.Equal(0, ClockTime.StepOf(0.0));
        Assert.Equal(1, ClockTime.StepOf(1.0 / 4096));
        Assert.Equal(4095, ClockTime.StepOf(4095.5 / 4096));
        Assert.Equal(4096 / 2, ClockTime.StepOf(0.5));
    }

    [Fact]
    public void Digits_split_the_step_like_a_hex_number()
    {
        int step = 0x37F;
        Assert.Equal(3, ClockTime.Phase(step));
        Assert.Equal(7, ClockTime.Turn(step));
        Assert.Equal(15, ClockTime.Beat(step));
        Assert.Equal("3:7:F", ClockTime.Format(step));
    }

    [Fact]
    public void Out_of_range_fractions_wrap_instead_of_failing()
    {
        Assert.Equal(0, ClockTime.StepOf(1.0));
        Assert.Equal(4095, ClockTime.StepOf(-0.0001));
        Assert.Equal(0, ClockTime.StepOf(double.NaN));
        Assert.Equal(0, ClockTime.StepOf(5, 0));
    }

    [Fact]
    public void The_next_step_follows_across_midnight()
    {
        Assert.True(ClockTime.IsNextStep(0, 1));
        Assert.True(ClockTime.IsNextStep(4095, 0));
        Assert.False(ClockTime.IsNextStep(0, 2));
        Assert.False(ClockTime.IsNextStep(5, 5));
        Assert.False(ClockTime.IsNextStep(1, 0));
    }
}
