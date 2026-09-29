namespace SignalsLink.Testing;

public enum RigStatus { Running, Passed, Failed, Error, Cancelled, InvalidEnvironment }
public enum StepMode { Eventually, Stable }

public sealed record RigStep(string Name, Action Enter, Func<bool> Check, Func<string> Observe,
    long TimeoutMs = 5000, StepMode Mode = StepMode.Eventually, long StableMs = 0);

/// <summary>Runs on the server thread. No sleeps, tasks or direct calls to device ticks.</summary>
public sealed class RigRunner
{
    private readonly IReadOnlyList<RigStep> steps;
    private readonly Action<string, string> record;
    private int index;
    private bool entered;
    private long started;
    public RigStatus Status { get; private set; } = RigStatus.Running;
    public string Detail { get; private set; } = "";
    public string CurrentStep => index < steps.Count ? steps[index].Name : "complete";

    public RigRunner(IReadOnlyList<RigStep> steps, Action<string, string> record)
    {
        if (steps == null || steps.Count == 0) throw new ArgumentException("A rig needs at least one step.");
        if (steps.Any(s => s.TimeoutMs <= 0 || s.StableMs < 0 || s.StableMs >= s.TimeoutMs))
            throw new ArgumentException("Each step needs a finite deadline longer than its stability window.");
        this.steps = steps;
        this.record = record;
    }

    public void Tick(long now)
    {
        if (Status != RigStatus.Running) return;
        RigStep step = steps[index];
        try
        {
            if (!entered)
            {
                started = now;
                entered = true;
                record("STEP", step.Name);
                if (Status != RigStatus.Running) return;
                step.Enter?.Invoke();
                return; // even immediate setup gets at least one real server tick to settle
            }
            long elapsed = now - started;
            if (elapsed > step.TimeoutMs)
            {
                Stop(RigStatus.Failed, "timeout " + step.Name + " | " + step.Observe());
                return;
            }
            bool holds = step.Check();
            if (step.Mode == StepMode.Stable && !holds)
            {
                Stop(RigStatus.Failed, "unstable " + step.Name + " | " + step.Observe());
                return;
            }
            if (!holds || (step.Mode == StepMode.Stable && elapsed < step.StableMs)) return;
            record("PASS", step.Name + " | " + step.Observe());
            index++;
            entered = false;
            if (index == steps.Count) Stop(RigStatus.Passed, "all steps passed");
        }
        catch (Exception ex) { Stop(RigStatus.Error, step.Name + " | " + ex); }
    }

    public void Stop(RigStatus status, string detail)
    {
        if (Status != RigStatus.Running) return;
        if (status == RigStatus.Running) throw new ArgumentException("A terminal status is required.");
        Status = status;
        Detail = detail;
    }
}
