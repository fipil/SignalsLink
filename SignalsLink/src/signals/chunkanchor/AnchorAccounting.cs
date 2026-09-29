using System;

namespace SignalsLink.src.signals.chunkanchor;

/// <summary>Accounts small active intervals; loading/offline gaps never become a debt.</summary>
public sealed class AnchorAccounting
{
    private double previous;
    public void Reset(double now) => previous = now;

    public double Advance(double now, bool billable)
    {
        double elapsed = now - previous;
        previous = now;
        return billable && double.IsFinite(elapsed) && elapsed > 0 && elapsed <= 24 ? elapsed : 0;
    }

    public static bool CanRun(bool enabled, double charge, bool sleeping) => enabled && charge > 0 && !sleeping;
    public static bool ShouldSleep(double now, double awakeSince, double interval, double window, bool alive, bool signal)
        => alive && !signal && interval > 0 && now - awakeSince >= window;
}
