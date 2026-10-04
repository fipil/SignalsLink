namespace SignalsMachines.src.craftingmachine;

/// <summary>What the process sees of the machine in one step.</summary>
public readonly record struct Sense(MachineInputs In, bool DoorsClosed, int Cells, bool HasRecipe, bool ProductEmpty, bool NetworkRunning, bool PlateRunning);

/// <summary>
/// The crafting sequence as a state machine (contract 13-22), stepped every 0.1 s on the server.
/// It decides and reports; the block entity acts on the events (crafts, burns, drives the pin).
/// </summary>
public sealed class MachineProcess
{
    public const byte Waiting = 0, Ready = 1, Preparing = 2, Crafting = 3, Done = 4;
    public const byte Overloaded = 10, PlateStill = 11, DoorOpened = 12, WrongStrength = 13, OutputFull = 14, NoDrive = 15;
    public enum Event { None, Craft, Overload }

    // tunables (contract: "lad.")
    public const float BaseSeconds = 3f, SecondsPerCell = 1f;
    public const float PauseReset = 5f, WrongStrengthGrace = 2f, PlateStillGrace = 1f, NoDriveGrace = 3f;
    public const int MaxStrengthStep = 2;

    public byte State { get; private set; }
    public float Progress { get; private set; }
    /// <summary>Why the last step saw an overload condition, or null (for traces).</summary>
    public string LastReason { get; private set; }
    public float Duration(int cells) => BaseSeconds + SecondsPerCell * cells;
    public bool InError => State >= Overloaded && State != OutputFull;

    private float pausedFor, wrongStrengthFor, plateStillFor, noDriveFor;
    private byte lastStrength;
    private bool first = true;

    public Event Step(in Sense s, float dt)
    {
        var inp = s.In;
        // the physics first: too steep a rise, or more than the batch can take while the crystal is down
        bool tooSteep = !first && inp.Strength > lastStrength + MaxStrengthStep;
        // too much for the batch on the plate; not while the finished product waits (the batch is gone),
        // and not over an empty plate (nothing to burn - the leak through an open door punishes that)
        bool tooStrong = inp.Crystal > 0 && s.Cells > 0 && s.ProductEmpty && inp.Strength > s.Cells + 1;
        LastReason = tooSteep ? $"steep: strength {lastStrength} -> {inp.Strength}" : tooStrong ? $"too strong: strength {inp.Strength} over {s.Cells} cells" : null;
        lastStrength = inp.Strength;
        first = false;
        if (State != Overloaded && (tooSteep || tooStrong))
        {
            Fail(Overloaded);
            return Event.Overload;
        }

        if (InError)
        {
            // an error holds until the operator backs off completely: strength 0, crystal up, clutch open
            // (without the clutch a "no drive" error would clear and return every step in a calm)
            if (inp.Strength == 0 && inp.Crystal == 0 && inp.Clutch == 0) { State = Waiting; ResetTimers(); }
            return Event.None;
        }

        if (!s.ProductEmpty) { State = s.HasRecipe ? OutputFull : Done; Progress = 0; return Event.None; }

        if (!s.DoorsClosed || !s.HasRecipe)
        {
            if (State == Crafting && !s.DoorsClosed) { Fail(DoorOpened); return Event.None; }   // the batch stays, the run is lost
            State = Waiting; Progress = 0; ResetTimers();
            return Event.None;
        }

        // doors closed, recipe on the plate, output free: watch the operator
        bool crystalDown = inp.Crystal > 0;
        plateStillFor = crystalDown && !s.PlateRunning ? plateStillFor + dt : 0;
        if (plateStillFor > PlateStillGrace) { Fail(PlateStill); return Event.None; }
        noDriveFor = inp.Clutch > 0 && !s.NetworkRunning ? noDriveFor + dt : 0;
        if (noDriveFor > NoDriveGrace) { Fail(NoDrive); return Event.None; }
        // too little strength is just the ramp-up (preparing, no limit); too much for too long is a fault
        wrongStrengthFor = crystalDown && inp.Strength > s.Cells ? wrongStrengthFor + dt : 0;
        if (wrongStrengthFor > WrongStrengthGrace) { Fail(WrongStrength); return Event.None; }

        bool running = inp.Clutch > 0 && s.PlateRunning && crystalDown && inp.Strength == s.Cells;
        if (running)
        {
            State = Crafting;
            pausedFor = 0;
            Progress += dt;
            if (Progress >= Duration(s.Cells)) { Progress = 0; State = Done; return Event.Craft; }
            return Event.None;
        }

        // not running: a started run waits a while, then is forgotten
        if (Progress > 0 && (pausedFor += dt) > PauseReset) Progress = 0;
        State = inp.Clutch > 0 || crystalDown || inp.Strength > 0 ? Preparing : Ready;
        return Event.None;
    }

    private void Fail(byte error) { State = error; Progress = 0; ResetTimers(); }

    /// <summary>The next step takes the strength as it comes, without the steepness check (a tube was just put in or taken out).</summary>
    public void Forgive() => first = true;
    private void ResetTimers() { pausedFor = wrongStrengthFor = plateStillFor = noDriveFor = 0; }

    /// <summary>Restores a saved state (progress keeps running after a reload).</summary>
    public void Restore(byte state, float progress) { State = state; Progress = progress; }
}
