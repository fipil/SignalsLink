using System.Text;
using signals.src;
using signals.src.signalNetwork;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SignalsLink.src.signals.clock;

/// <summary>
/// Server: every Signals step the clock reads the calendar and, when the step of the day moved, puts the
/// three digits on its pins; the first step after placing or loading publishes the current time at once.
/// Client: holds the lights' motion state and registers with the shared <see cref="ClockRenderer"/>.
/// </summary>
public class BEClock : BlockEntity
{
    public const int Pins = 3;

    private SignalNetworkMod signalMod;
    private int published = -1;

    /// <summary>Client: where the lights are.</summary>
    public ClockLights Lights { get; } = new();

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server)
        {
            signalMod = api.ModLoader.GetModSystem<SignalNetworkMod>();
            signalMod.RegisterSignalTickListener(OnSignalTick);
        }
        else api.ModLoader.GetModSystem<ClockRenderer>()?.Add(this);
    }

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        Leave();
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        Leave();
    }

    private void Leave()
    {
        signalMod?.DisposeSignalTickListener(OnSignalTick);
        if (Api?.Side == EnumAppSide.Client) Api.ModLoader.GetModSystem<ClockRenderer>()?.Remove(this);
    }

    /// <summary>Step of the day by this side's calendar.</summary>
    public int CurrentStep()
    {
        var cal = Api.World.Calendar;
        return ClockTime.StepOf(cal.HourOfDay, cal.HoursPerDay);
    }

    private void OnSignalTick()
    {
        var beh = GetBehavior<BEBehaviorSignalConnector>();
        if (beh == null) return;
        int step = CurrentStep();
        if (step == published) return;
        for (int pin = 0; pin < Pins; pin++)
            beh.UpdateSource(new NodePos(Pos, pin), (byte)ClockTime.Digit(step, pin));
        published = step;
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        // Looking at a pin: only its name (appended by BlockConnection after this) belongs in the tooltip.
        // The wire anchors come first among the selection boxes, so their indices are 0..Pins-1.
        var sel = forPlayer?.CurrentBlockSelection;
        if (sel != null && sel.Position.Equals(Pos) && sel.SelectionBoxIndex < Pins) return;
        dsc.AppendLine(Lang.Get("signalslink:clock-time", ClockTime.Format(CurrentStep())));
    }
}
