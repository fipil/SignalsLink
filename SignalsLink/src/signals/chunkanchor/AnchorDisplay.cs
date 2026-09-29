using System;
using System.Globalization;
using System.Linq;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.chunkanchor;

public static class AnchorDisplay
{
    public const int MaxNameLength = 64;

    public static string CleanName(string name) => new string((name ?? "")
        .Where(c => !char.IsControl(c)).Take(MaxNameLength).ToArray()).Trim();

    public static string Location(string name, BlockPos pos) => string.IsNullOrEmpty(name)
        ? pos.ToString() : "\"" + name + "\" " + pos;

    public static string TimeOfDay(double totalHours, double hoursPerDay = 24)
    {
        if (!double.IsFinite(totalHours)) return "--:--";
        if (!double.IsFinite(hoursPerDay) || hoursPerDay <= 0) hoursPerDay = 24;
        double hour = (totalHours % hoursPerDay + hoursPerDay) % hoursPerDay;
        return Duration(hour);
    }

    public static string Duration(double hours)
    {
        if (!double.IsFinite(hours)) return "--:--";
        long minutes = (long)Math.Floor(Math.Max(0, hours) * 60 + 1e-7);
        return (minutes / 60).ToString("00", CultureInfo.InvariantCulture) + ":"
            + (minutes % 60).ToString("00", CultureInfo.InvariantCulture);
    }
}