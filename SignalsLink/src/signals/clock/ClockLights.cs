using System.Numerics;

namespace SignalsLink.src.signals.clock;

/// <summary>
/// Where the twelve lights of one clock are at any moment. Fed the current step of the day and the clock
/// time, it starts a glide for every group whose digit changed when the step is the next one, and sets the
/// lights down in their new figure at once after a jump (chunk load, skipped night, time command, a client
/// calendar correction). A glide takes <see cref="TransitionMs"/>, or less when steps come faster than that.
/// </summary>
public sealed class ClockLights
{
    public const int TransitionMs = 200;

    private sealed class Group
    {
        public string From, To;
        public long StartMs;
        public int DurationMs;
    }

    private readonly Group[] groups = { new(), new(), new() };
    private int lastStep = -1;

    /// <summary>The step the lights show, -1 before the first update.</summary>
    public int Step => lastStep;

    /// <summary>
    /// Brings the lights to <paramref name="step"/>. <paramref name="msPerStep"/> is how much real time one
    /// step of the day lasts (infinity when time stands still); it only ever shortens a glide.
    /// </summary>
    public void Update(int step, long nowMs, double msPerStep)
    {
        if (step == lastStep) return;
        bool glide = lastStep >= 0 && ClockTime.IsNextStep(lastStep, step);
        int duration = (int)Math.Clamp(Math.Floor(Math.Min(TransitionMs, msPerStep)), 1, TransitionMs);
        for (int g = 0; g < ClockFigures.Groups; g++)
        {
            string figure = ClockFigures.Figure(g, ClockTime.Digit(step, g));
            var group = groups[g];
            if (group.To == figure && group.From != null) continue;   // this digit did not change
            if (glide && group.To != null)
            {
                group.From = group.To;
                group.StartMs = nowMs;
                group.DurationMs = duration;
            }
            else group.From = figure;   // settle without a glide
            group.To = figure;
        }
        lastStep = step;
    }

    /// <summary>True while some light is still on its way.</summary>
    public bool Moving(long nowMs) => groups.Any(g => g.From != g.To && nowMs < g.StartMs + g.DurationMs);

    /// <summary>
    /// Position of a group's point in the cube's own frame, for a cube with this centre and edge.
    /// Linear glide between the old and the new corner; at rest in the corner itself.
    /// </summary>
    public Vector3 Point(int group, int index, long nowMs, Vector3 center, float edge)
    {
        var g = groups[group];
        if (g.To == null) return center;
        var to = ClockFigures.Position(g.To[index], center, edge);
        if (g.From == g.To) return to;
        float t = Math.Clamp((nowMs - g.StartMs) / (float)g.DurationMs, 0, 1);
        if (t >= 1) return to;
        var from = ClockFigures.Position(g.From[index], center, edge);
        return Vector3.Lerp(from, to, t);
    }

    /// <summary>The figure a group currently heads to, null before the first update.</summary>
    public string Figure(int group) => groups[group].To;
}
