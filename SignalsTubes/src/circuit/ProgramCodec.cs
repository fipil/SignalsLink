using Newtonsoft.Json.Linq;

namespace SignalsTubes.src.circuit;

/// <summary>
/// Compact JSON form of a program, kept in the tube's item attributes. Versioned by "v".
/// Load validates everything, so a broken or foreign program fails here and not in the simulator.
/// </summary>
public static class ProgramCodec
{
    public const string AttributeKey = "circuit";

    private static readonly string[] KindNames = { "source", "switch", "toggle", "valve", "tetrode", "delay", "buffer", "tube" };
    private static readonly string[] RoleNames = { "in", "out", "switch", "delay" };

    public static string ToJson(CircuitProgram p)
    {
        var root = new JObject
        {
            ["v"] = CircuitProgram.CurrentFormat,
            ["n"] = p.NodeCount,
            ["links"] = new JArray(p.Links.Select(l => new JArray(l.A, l.B, l.Att, l.RevAtt))),
            ["parts"] = new JArray(p.Components.Select(c =>
            {
                var o = new JObject { ["k"] = KindNames[(int)c.Kind], ["n"] = new JArray(c.Nodes) };
                if (c.Param != 0) o["p"] = c.Param;
                if (c.State is { Length: > 0 } && c.State.Any(b => b != 0)) o["s"] = new JArray(c.State.Select(b => (int)b));  // byte[] alone would become one base64 token
                if (c.ParamNode >= 0) o["pn"] = c.ParamNode;
                if (c.Ref != null) o["ref"] = c.Ref;
                if (c.Group >= 0) o["g"] = c.Group;
                return o;
            })),
            ["groups"] = new JArray(p.Groups.Select(g =>
            {
                var o = new JObject { ["name"] = g.Name ?? "", ["pins"] = new JArray(g.Pins.Select(gp =>
                {
                    var po = new JObject { ["i"] = gp.Index, ["r"] = RoleNames[(int)gp.Role], ["n"] = gp.Node };
                    if (!string.IsNullOrEmpty(gp.Name)) po["name"] = gp.Name;
                    return po;
                })) };
                if (!string.IsNullOrEmpty(g.Description)) o["desc"] = g.Description;
                if (g.Parent >= 0) o["parent"] = g.Parent;
                if (g.Ref != null) o["ref"] = g.Ref;
                return o;
            })),
            ["pins"] = new JArray(p.Pins.Select(pin =>
            {
                var o = new JObject { ["i"] = pin.Index, ["r"] = RoleNames[(int)pin.Role] };
                if (pin.Node >= 0) o["n"] = pin.Node; else o["c"] = pin.Component;
                if (!string.IsNullOrEmpty(pin.Name)) o["name"] = pin.Name;
                return o;
            }))
        };
        // JsonConvert, not JToken.ToString(Formatting): that overload is missing in the game's Newtonsoft build.
        return Newtonsoft.Json.JsonConvert.SerializeObject(root, Newtonsoft.Json.Formatting.None);
    }

    public static CircuitProgram FromJson(string json)
    {
        JObject root;
        try { root = JObject.Parse(json); }
        catch (Exception e) { throw new FormatException("Program is not valid JSON.", e); }

        int version = Int(root, "v");
        if (version != CircuitProgram.CurrentFormat) throw new FormatException($"Unsupported program format {version}.");
        var p = new CircuitProgram { NodeCount = Int(root, "n") };
        if (p.NodeCount < 0) throw new FormatException("Negative node count.");

        foreach (var l in Arr(root, "links"))
        {
            if (l is not JArray a || a.Count != 4) throw new FormatException("Link must be [a, b, att, revAtt].");
            p.Links.Add(new Link(Node(p, a[0]), Node(p, a[1]), Level(a[2]), Level(a[3])));
        }
        foreach (var t in Arr(root, "parts"))
        {
            var o = t as JObject ?? throw new FormatException("Part must be an object.");
            int kindIndex = Array.IndexOf(KindNames, (string)o["k"]);
            if (kindIndex < 0) throw new FormatException($"Unknown part kind '{o["k"]}'.");
            var kind = (ComponentKind)kindIndex;
            var nodes = (o["n"] as JArray ?? throw new FormatException("Part nodes missing.")).Select(n => Node(p, n)).ToArray();
            if (Component.Arity(kind) >= 0 && nodes.Length != Component.Arity(kind)) throw new FormatException($"Part '{KindNames[kindIndex]}' needs {Component.Arity(kind)} nodes.");
            var c = new Component(kind, o["p"] == null ? (byte)0 : Level(o["p"]), nodes);
            if (kind == ComponentKind.Delay && c.Param >= CircuitProgram.DelaySlots) throw new FormatException("Delay setting out of range.");
            if (o["s"] is JArray s) c.State = s.Select(Level).ToArray();
            if (o["pn"] != null)
            {
                if (kind is not (ComponentKind.Switch or ComponentKind.Delay)) throw new FormatException("Only a switch or delay takes its setting from a node.");
                c.ParamNode = Node(p, o["pn"]);
            }
            if (kind == ComponentKind.Tube)
            {
                c.Ref = (string)o["ref"] ?? throw new FormatException("Tube part without a program reference.");
                if (nodes.Length == 0 || nodes.Length > CircuitProgram.MaxPins) throw new FormatException("Tube part with an impossible pin count.");
            }
            if (o["g"] != null) c.Group = Int(o, "g");
            p.Components.Add(c);
        }
        if (root["groups"] is JArray groups)
            foreach (var t in groups)
            {
                var o = t as JObject ?? throw new FormatException("Group must be an object.");
                var g = new Group { Name = (string)o["name"] ?? "", Description = (string)o["desc"], Parent = o["parent"] == null ? -1 : Int(o, "parent"), Ref = (string)o["ref"] };
                foreach (var pt in o["pins"] as JArray ?? new JArray())
                {
                    var po = pt as JObject ?? throw new FormatException("Group pin must be an object.");
                    int roleIndex = Array.IndexOf(RoleNames, (string)po["r"]);
                    if (roleIndex < 0) throw new FormatException($"Unknown pin role '{po["r"]}'.");
                    g.Pins.Add(new GroupPin { Index = Int(po, "i"), Role = (PinRole)roleIndex, Name = (string)po["name"], Node = Node(p, po["n"] ?? throw new FormatException("Group pin node missing.")) });
                }
                p.Groups.Add(g);
            }
        foreach (var c in p.Components)
            if (c.Group >= p.Groups.Count) throw new FormatException("Part group out of range.");
        foreach (var g in p.Groups)
            if (g.Parent >= p.Groups.Count) throw new FormatException("Group parent out of range.");
        var seen = new bool[CircuitProgram.MaxPins];
        foreach (var t in Arr(root, "pins"))
        {
            var o = t as JObject ?? throw new FormatException("Pin must be an object.");
            var pin = new Pin { Index = Int(o, "i") };
            if (pin.Index < 0 || pin.Index >= CircuitProgram.MaxPins) throw new FormatException($"Pin index {pin.Index} out of range.");
            if (seen[pin.Index]) throw new FormatException($"Pin {pin.Index} used twice.");
            seen[pin.Index] = true;
            int roleIndex = Array.IndexOf(RoleNames, (string)o["r"]);
            if (roleIndex < 0) throw new FormatException($"Unknown pin role '{o["r"]}'.");
            pin.Role = (PinRole)roleIndex;
            pin.Name = (string)o["name"];
            if (pin.Role is PinRole.Input or PinRole.Output)
                pin.Node = Node(p, o["n"] ?? throw new FormatException("Pin node missing."));
            else
            {
                pin.Component = Int(o, "c");
                if (pin.Component < 0 || pin.Component >= p.Components.Count) throw new FormatException("Pin component out of range.");
                var expected = pin.Role == PinRole.Switch ? ComponentKind.Switch : ComponentKind.Delay;
                if (p.Components[pin.Component].Kind != expected) throw new FormatException($"Pin {pin.Index} does not point at a {expected}.");
            }
            p.Pins.Add(pin);
        }
        return p;
    }

    private static int Int(JObject o, string key) =>
        o[key] is JValue { Type: JTokenType.Integer } v ? (int)v : throw new FormatException($"'{key}' missing or not an integer.");

    private static JArray Arr(JObject o, string key) =>
        o[key] as JArray ?? throw new FormatException($"'{key}' missing or not an array.");

    private static int Node(CircuitProgram p, JToken t)
    {
        if (t is not JValue { Type: JTokenType.Integer } v) throw new FormatException("Node id must be an integer.");
        int n = (int)v;
        if (n < 0 || n >= p.NodeCount) throw new FormatException($"Node {n} out of range.");
        return n;
    }

    private static byte Level(JToken t)
    {
        if (t is not JValue { Type: JTokenType.Integer } v) throw new FormatException("Level must be an integer.");
        int n = (int)v;
        if (n < 0 || n > CircuitProgram.MaxLevel) throw new FormatException($"Level {n} out of range.");
        return (byte)n;
    }
}
