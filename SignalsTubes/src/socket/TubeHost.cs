using signals.src.signalNetwork;
using SignalsTubes.src.circuit;
using SignalsTubes.src.programtube;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsTubes.src.socket;

/// <summary>
/// Runs a programmed tube inside a machine's socket, for any machine. The machine says which of its pins
/// it reads (the tube's outputs drive them; wires on them are ignored); every other pin the tube reads
/// from the world. Server side only; the host keeps the simulator's state across saves.
/// </summary>
public sealed class TubeHost
{
    private readonly ICoreAPI api;
    private CircuitProgram program;
    private CircuitSimulator sim;
    private byte[] savedState;   // from the save game, applied once a program is loaded

    public TubeHost(ICoreAPI api) { this.api = api; }

    /// <summary>A programmed tube is loaded and takes control.</summary>
    public bool Controls => program != null;

    /// <summary>Loads the tube's program (null or a blank tube unloads).</summary>
    public void Load(ItemStack tube)
    {
        program = tube == null || api?.Side != EnumAppSide.Server ? null : TubeProgram.Get(tube, api);
        sim = program == null ? null : new CircuitSimulator(program, id => ProgramStore.Of(api)?.Get(id));
        if (sim != null && savedState != null) sim.LoadState(savedState);
        savedState = null;
    }

    public byte[] SaveState() => sim?.SaveState();
    /// <summary>Remembers a saved simulator state until the program is loaded.</summary>
    public void RestoreState(byte[] state) { savedState = state; if (sim != null && state != null) { sim.LoadState(state); savedState = null; } }

    public bool IsOutputPin(int index) => program?.Pins.Any(p => p.Index == index && p.Role == PinRole.Output) == true;

    /// <summary>
    /// One Signals step. <paramref name="pinLevel"/> is what the world shows on a pin;
    /// <paramref name="machineInput"/> marks the pins the machine reads, which the tube never reads back.
    /// </summary>
    public void Step(System.Func<int, byte> pinLevel, System.Func<int, bool> machineInput)
    {
        if (sim == null) return;
        foreach (var pin in program.Pins)
            if (pin.Role != PinRole.Output) sim.SetInput(pin.Index, lastIn[pin.Index] = machineInput(pin.Index) ? (byte)0 : pinLevel(pin.Index));
        sim.Step();
        foreach (var pin in program.Pins)
            if (pin.Role == PinRole.Output) lastOut[pin.Index] = sim.GetOutput(pin.Index);
    }

    private readonly byte[] lastIn = new byte[CircuitProgram.MaxPins], lastOut = new byte[CircuitProgram.MaxPins];

    /// <summary>The last step on one line: in[pin=level ...] out[pin=level ...] (for traces).</summary>
    public string DescribeShort()
    {
        if (program == null) return "tube: none";
        var pins = program.Pins.OrderBy(p => p.Index).ToList();
        return "tube in[" + string.Join(" ", pins.Where(p => p.Role != PinRole.Output).Select(p => $"{p.Index + 1}={lastIn[p.Index]}"))
            + "] out[" + string.Join(" ", pins.Where(p => p.Role == PinRole.Output).Select(p => $"{p.Index + 1}={lastOut[p.Index]}")) + "]";
    }

    /// <summary>What the tube saw and gave on each of its pins in the last step (for /tubes diag).</summary>
    public string Describe()
    {
        if (program == null) return "no program";
        var sb = new System.Text.StringBuilder();
        foreach (var pin in program.Pins.OrderBy(p => p.Index))
            sb.AppendLine($"tube pin {pin.Index + 1}: {pin.Role.ToString().ToLowerInvariant()}{(pin.Name == null ? "" : " \"" + pin.Name + "\"")}, "
                + (pin.Role == PinRole.Output ? $"gives {lastOut[pin.Index]}" : $"reads {lastIn[pin.Index]}"));
        return sb.ToString();
    }

    /// <summary>The tube's level on one of its output pins after a step; 0 where it has none.</summary>
    public byte Output(int index) => IsOutputPin(index) ? sim.GetOutput(index) : (byte)0;

    /// <summary>
    /// What a tube sees on a pin of its host: the network's value, but at least what the host itself drives
    /// onto that pin. A pin nothing is wired to belongs to no network, so the network alone would read 0
    /// even while the host reports a state there.
    /// </summary>
    public static byte PinLevel(BEBehaviorSignalConnector connector, BlockPos pos, int index)
    {
        var node = connector?.GetNodeAt(new NodePos(pos, index));
        return node == null ? (byte)0 : Math.Max(node.value, node.output);
    }
}
