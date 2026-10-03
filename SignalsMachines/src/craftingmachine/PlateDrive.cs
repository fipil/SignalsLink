namespace SignalsMachines.src.craftingmachine;

/// <summary>
/// The crafting plate behind the clutch. With the clutch closed it spins up towards the network speed,
/// open it coasts down faster. Pure arithmetic, shared by server, client picture and tests.
/// </summary>
public static class PlateDrive
{
    /// <summary>Seconds to cover 63 % of the gap to the target speed while the clutch drives the plate.</summary>
    public const float TimeConstant = 2f;
    /// <summary>Coasting down with the clutch open is quicker; the client brakes the picture into the home position itself.</summary>
    public const float ReleaseTimeConstant = 0.7f;
    /// <summary>Fraction of the network speed the plate needs for the machine to work.</summary>
    public const float RunningFraction = 0.3f;
    /// <summary>Below this the plate counts as standing.</summary>
    public const float Still = 0.01f;

    public static float Step(float speed, float target, float dt)
    {
        float tau = target > 0 ? TimeConstant : ReleaseTimeConstant;
        float next = speed + (target - speed) * (1 - MathF.Exp(-dt / tau));
        return MathF.Abs(next) < Still && target == 0 ? 0 : next;
    }

    /// <summary>
    /// Braking plan for the picture: from angular speed w (rad/s) at angle a, stop exactly at the next home
    /// angle (a multiple of 2 pi), adding whole turns until the uniform deceleration takes at least MinStop
    /// seconds. Returns the distance to travel.
    /// </summary>
    public const float MinStop = 0.8f;
    public static float BrakingDistance(float angle, float w)
    {
        float toHome = (MathF.Tau - angle % MathF.Tau) % MathF.Tau;
        float d = toHome;
        while (2 * d / w < MinStop) d += MathF.Tau;          // too abrupt: one more turn
        return d;
    }

    /// <summary>Angle after t seconds of uniform deceleration from w over distance d (stop time 2d/w).</summary>
    public static float Brake(float start, float w, float d, float t)
    {
        float stopTime = 2 * d / w;
        if (t >= stopTime) return start + d;
        float a = w * w / (2 * d);
        return start + w * t - a * t * t / 2;
    }

    /// <summary>The plate turns fast enough relative to what drives it.</summary>
    public static bool Running(float plateSpeed, float networkSpeed) =>
        networkSpeed > Still && plateSpeed >= RunningFraction * networkSpeed;
}
