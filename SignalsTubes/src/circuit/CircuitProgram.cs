namespace SignalsTubes.src.circuit;

/// <summary>
/// Portable description of an imprinted circuit. Knows nothing about the world or about Signals;
/// it only mirrors Signals semantics: node levels 0-15, links with a per-direction attenuation.
/// </summary>
public sealed class CircuitProgram
{
    public const int CurrentFormat = 1;
    public const int MaxPins = 8;
    public const byte MaxLevel = 15;
    public const int DelaySlots = 6;

    public int NodeCount;
    public List<Link> Links = new();
    public List<Component> Components = new();
    public List<Pin> Pins = new();
}

/// <summary>Static link. Att applies A→B, RevAtt applies B→A. A wire is 0/0.</summary>
public sealed class Link
{
    public int A, B;
    public byte Att, RevAtt;

    public Link() { }
    public Link(int a, int b, byte att = 0, byte revAtt = 0) { A = a; B = b; Att = att; RevAtt = revAtt; }
}

public enum ComponentKind : byte
{
    Source,   // [node]                        Param = output
    Switch,   // [a, b]                        Param = 1 on
    Toggle,   // [trigger, a, b]               Param = 1 on; flips whenever trigger changes to a non-zero level (actuator + switch)
    Valve,    // [grid, cathode, anode]        cathode→anode att = grid
    Tetrode,  // [grid, cathode, anode, screen] att = grid*(screen+1)
    Delay,    // [in, out]                     Param = 0..5, State = shift register
    Buffer,   // [in, out]                     out = in of the previous step; the input-pin latency of an inlined tube
}

public sealed class Component
{
    public ComponentKind Kind;
    public int[] Nodes;
    public byte Param;
    public byte[] State;

    public Component() { }
    public Component(ComponentKind kind, byte param, params int[] nodes) { Kind = kind; Param = param; Nodes = nodes; }

    public static int Arity(ComponentKind kind) => kind switch
    {
        ComponentKind.Source => 1,
        ComponentKind.Switch => 2,
        ComponentKind.Toggle => 3,
        ComponentKind.Valve => 3,
        ComponentKind.Tetrode => 4,
        ComponentKind.Delay => 2,
        ComponentKind.Buffer => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}

public enum PinRole : byte
{
    Input,   // external level drives Node
    Output,  // Node level is exported
    Switch,  // level > 0 turns Component on
    Delay,   // 0 keeps the imprinted setting, otherwise sets it (clamped to 1..5)
}

public sealed class Pin
{
    public int Index;
    public PinRole Role;
    public string Name;          // optional, author-given
    public int Node = -1;        // Input / Output
    public int Component = -1;   // Switch / Delay

    public Pin() { }
    public static Pin Input(int index, int node) => new() { Index = index, Role = PinRole.Input, Node = node };
    public static Pin Output(int index, int node) => new() { Index = index, Role = PinRole.Output, Node = node };
    public static Pin Switch(int index, int component) => new() { Index = index, Role = PinRole.Switch, Component = component };
    public static Pin Delay(int index, int component) => new() { Index = index, Role = PinRole.Delay, Component = component };
}
