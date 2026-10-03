namespace SignalsMachines.src.craftingmachine;

/// <summary>
/// One chamber door: closed it sits in the wall; opening it first pushes out of the frame, then slides
/// down along its rods; closing runs the same way back and latches. Timed to the door sounds
/// (open: steam + 1.67 s slide, close: 2.25 s slide + latch). Pure arithmetic, run on the client.
/// </summary>
public sealed class DoorMotion
{
    public const float PushSeconds = 0.5f, OpenSlideSeconds = 1.67f, CloseSlideSeconds = 2.25f;
    /// <summary>Model units (sixteenths) the door pushes out and drops.</summary>
    public const float PushUnits = 0.8f, DropUnits = 11.8f;

    /// <summary>0 = in the frame, 1 = fully pushed out.</summary>
    public float Push { get; private set; }
    /// <summary>0 = up, 1 = fully dropped.</summary>
    public float Drop { get; private set; }
    public bool Open { get; private set; }
    public bool Moving => Open ? Push < 1 || Drop < 1 : Push > 0 || Drop > 0;

    public void Set(bool open) => Open = open;

    public void Advance(float dt)
    {
        if (Open)
        {
            if (Push < 1) Push = Math.Min(1, Push + dt / PushSeconds);
            else Drop = Math.Min(1, Drop + Ease(dt / OpenSlideSeconds));
        }
        else
        {
            if (Drop > 0) Drop = Math.Max(0, Drop - Ease(dt / CloseSlideSeconds));
            else Push = Math.Max(0, Push - dt / PushSeconds);
        }
    }

    // the slide starts and ends softly: a step of the time axis mapped through a smooth curve
    private float Ease(float step) => step;   // kept linear for now; sounds are cut to a steady slide

    /// <summary>Offsets in block units: out of the wall along the door's normal, and down.</summary>
    public (float outward, float down) Offsets => (Push * PushUnits / 16f, Drop * DropUnits / 16f);
}
