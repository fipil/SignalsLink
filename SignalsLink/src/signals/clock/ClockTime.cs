namespace SignalsLink.src.signals.clock;

/// <summary>
/// The clock's view of the game day: 4096 steps from midnight, read as three hex digits
/// Phase (16 per day), Turn (16 per Phase) and Beat (16 per Turn). Pure; the block feeds it the calendar.
/// </summary>
public static class ClockTime
{
    public const int StepsPerDay = 4096;

    /// <summary>Step of the day for a position in it, 0 at midnight; anything outside [0, 1) wraps.</summary>
    public static int StepOf(double dayFraction)
    {
        if (double.IsNaN(dayFraction) || double.IsInfinity(dayFraction)) return 0;
        int step = (int)Math.Floor(dayFraction * StepsPerDay) % StepsPerDay;
        return step < 0 ? step + StepsPerDay : step;
    }

    /// <summary>Step of the day from the calendar's hour of day and hours per day.</summary>
    public static int StepOf(double hourOfDay, double hoursPerDay) =>
        hoursPerDay <= 0 ? 0 : StepOf(hourOfDay / hoursPerDay);

    public static int Phase(int step) => (step >> 8) & 0xF;
    public static int Turn(int step) => (step >> 4) & 0xF;
    public static int Beat(int step) => step & 0xF;

    /// <summary>The digit of one group: 0 = phase, 1 = turn, 2 = beat.</summary>
    public static int Digit(int step, int group) => group switch { 0 => Phase(step), 1 => Turn(step), _ => Beat(step) };

    /// <summary>True when <paramref name="current"/> directly follows <paramref name="previous"/>, midnight included.</summary>
    public static bool IsNextStep(int previous, int current) => (previous + 1) % StepsPerDay == current;

    /// <summary>Time as the player reads it on the pins: hex digits Phase:Turn:Beat.</summary>
    public static string Format(int step) => $"{Phase(step):X}:{Turn(step):X}:{Beat(step):X}";
}
