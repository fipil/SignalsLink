namespace SignalsTubes.src.circuit;

/// <summary>
/// Runs a program in Signals steps (0.1 s). Each step: components react to the levels of the
/// previous step, then the network is solved. That reproduces the one-step latency of valves,
/// actuators and delays in the real network.
/// </summary>
public sealed class CircuitSimulator
{
    private readonly CircuitProgram program;
    private readonly byte[] values;
    private readonly byte[] pinLevels = new byte[CircuitProgram.MaxPins];
    private readonly bool[] pinUsed = new bool[CircuitProgram.MaxPins];

    // Static links first, then one dynamic link per Switch/Toggle/Valve/Tetrode.
    private readonly int[] linkA, linkB;
    private readonly byte[] linkAtt, linkRev;
    private readonly int[] dynamicLink;     // component index -> link slot, -1 if none
    private readonly byte[] source;         // per node, driven level for this step (0 = none)
    private readonly int[] paramPinOf;      // component index -> pin index, -1 if none
    private readonly byte[][] state;        // own copy: the program may be shared between tubes
    private readonly bool[] toggleOn;

    public CircuitSimulator(CircuitProgram program)
    {
        this.program = program;
        values = new byte[program.NodeCount];
        source = new byte[program.NodeCount];
        int dynamicCount = program.Components.Count(c => HasDynamicLink(c.Kind));
        int total = program.Links.Count + dynamicCount;
        linkA = new int[total]; linkB = new int[total]; linkAtt = new byte[total]; linkRev = new byte[total];
        dynamicLink = new int[program.Components.Count];
        paramPinOf = new int[program.Components.Count];
        state = new byte[program.Components.Count][];
        toggleOn = new bool[program.Components.Count];
        Array.Fill(paramPinOf, -1);
        for (int i = 0; i < program.Links.Count; i++)
        {
            var l = program.Links[i];
            linkA[i] = l.A; linkB[i] = l.B; linkAtt[i] = l.Att; linkRev[i] = l.RevAtt;
        }
        int slot = program.Links.Count;
        for (int i = 0; i < program.Components.Count; i++)
        {
            var c = program.Components[i];
            dynamicLink[i] = HasDynamicLink(c.Kind) ? slot++ : -1;
            state[i] = NewState(c.Kind);
            if (c.State != null) Array.Copy(c.State, state[i], Math.Min(c.State.Length, state[i].Length));
            toggleOn[i] = c.Param != 0;
        }
        foreach (var pin in program.Pins)
        {
            pinUsed[pin.Index] = true;
            if (pin.Component >= 0) paramPinOf[pin.Component] = pin.Index;
        }
    }

    private static bool HasDynamicLink(ComponentKind kind) =>
        kind is ComponentKind.Switch or ComponentKind.Toggle or ComponentKind.Valve or ComponentKind.Tetrode;

    private static byte[] NewState(ComponentKind kind) => kind switch
    {
        ComponentKind.Delay => new byte[CircuitProgram.DelaySlots],
        ComponentKind.Toggle => new byte[1],
        _ => Array.Empty<byte>()
    };

    public bool HasPin(int index) => pinUsed[index];

    public void SetInput(int pin, byte level) => pinLevels[pin] = Math.Min(level, CircuitProgram.MaxLevel);

    public byte GetOutput(int pin)
    {
        foreach (var p in program.Pins)
            if (p.Index == pin && p.Role == PinRole.Output) return values[p.Node];
        return 0;
    }

    public byte GetNode(int node) => values[node];

    public void Step()
    {
        Array.Clear(source);
        for (int i = 0; i < program.Components.Count; i++) React(i, program.Components[i]);
        foreach (var pin in program.Pins)
            if (pin.Role == PinRole.Input) Drive(pin.Node, pinLevels[pin.Index]);
        Solve();
    }

    private void Drive(int node, byte level) => source[node] = Math.Max(source[node], level);

    private void SetLink(int component, int a, int b, byte att, byte rev)
    {
        int s = dynamicLink[component];
        linkA[s] = a; linkB[s] = b; linkAtt[s] = att; linkRev[s] = rev;
    }

    private void React(int i, Component c)
    {
        int[] n = c.Nodes;
        int pin = paramPinOf[i];
        switch (c.Kind)
        {
            case ComponentKind.Source:
                Drive(n[0], c.Param);
                break;
            case ComponentKind.Switch:
            {
                bool on = pin >= 0 ? pinLevels[pin] > 0 : c.Param != 0;
                byte att = on ? (byte)0 : CircuitProgram.MaxLevel;
                SetLink(i, n[0], n[1], att, att);
                break;
            }
            case ComponentKind.Toggle:
            {
                // The Signals actuator fires on every change to a non-zero level, not only on 0 -> on.
                byte trigger = values[n[0]];
                if (trigger > 0 && trigger != state[i][0]) toggleOn[i] = !toggleOn[i];
                state[i][0] = trigger;
                byte att = toggleOn[i] ? (byte)0 : CircuitProgram.MaxLevel;
                SetLink(i, n[1], n[2], att, att);
                break;
            }
            case ComponentKind.Valve:
                SetLink(i, n[1], n[2], values[n[0]], CircuitProgram.MaxLevel);
                break;
            case ComponentKind.Tetrode:
            {
                int att = values[n[0]] * (values[n[3]] + 1);
                SetLink(i, n[1], n[2], (byte)Math.Min(att, CircuitProgram.MaxLevel), CircuitProgram.MaxLevel);
                break;
            }
            case ComponentKind.Delay:
            {
                var s = state[i];
                for (int k = s.Length - 1; k > 0; k--) s[k] = s[k - 1];
                s[0] = values[n[0]];
                int setting = c.Param;
                if (pin >= 0 && pinLevels[pin] > 0) setting = Math.Min((int)pinLevels[pin], CircuitProgram.DelaySlots - 1);
                Drive(n[1], s[setting]);
                break;
            }
            case ComponentKind.Buffer:
                Drive(n[1], values[n[0]]);
                break;
        }
    }

    // Level = max over sources of (source level - path attenuation), clamped at 0.
    // Relaxation until stable; levels only grow, so it ends within NodeCount rounds.
    private void Solve()
    {
        Array.Copy(source, values, values.Length);
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = 0; i < linkA.Length; i++)
            {
                int a = linkA[i], b = linkB[i];
                int viaA = values[a] - linkAtt[i];
                if (viaA > values[b]) { values[b] = (byte)viaA; changed = true; }
                int viaB = values[b] - linkRev[i];
                if (viaB > values[a]) { values[a] = (byte)viaB; changed = true; }
            }
        }
    }
}
