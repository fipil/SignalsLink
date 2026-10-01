using Newtonsoft.Json.Linq;
using SignalsTubes.src.circuit;

namespace SignalsTubes.src.schematic;

/// <summary>
/// One level of a schematic: what a program looks like at the top, inside an inlined tube (a group)
/// or inside a soldered tube (another program). Built on the server, which also decides what the
/// viewer may open, and sent to the client as JSON for layout and drawing.
/// </summary>
public static class SchematicLevel
{
    /// <summary>Who may look inside a soldered tube; null when nothing is known about it.</summary>
    public sealed record ReferenceInfo(string AuthorUid, bool LockView, string Name);

    public sealed class Context
    {
        public Func<string, CircuitProgram> Resolve;          // program by id
        public Func<string, ReferenceInfo> References;        // soldered tube by program id
        public string ViewerUid;
    }

    /// <summary>
    /// Path elements: "g:<index>" opens a group of the current program, "r:<id>" switches to a
    /// referenced program. Returns the level JSON, or a JSON with "locked": true.
    /// </summary>
    public static JObject Build(CircuitProgram root, string rootName, IReadOnlyList<string> path, Context ctx)
    {
        var program = root;
        int group = -1;
        var crumbs = new JArray { rootName ?? "" };
        foreach (var step in path)
        {
            if (step.StartsWith("g:") && int.TryParse(step[2..], out int g) && g >= 0 && g < program.Groups.Count && program.Groups[g].Ref == null)
            {
                group = g;
                crumbs.Add(program.Groups[g].Name ?? "");
            }
            else if (step.StartsWith("r:"))
            {
                string id = step[2..];
                var info = ctx.References?.Invoke(id);
                if (info != null && info.LockView && info.AuthorUid != ctx.ViewerUid)
                    return new JObject { ["locked"] = true, ["crumbs"] = crumbs, ["name"] = info.Name ?? "" };
                var next = ctx.Resolve?.Invoke(id);
                if (next == null) return new JObject { ["locked"] = true, ["crumbs"] = crumbs, ["name"] = info?.Name ?? "" };
                program = next;
                group = -1;
                crumbs.Add(info?.Name ?? "");
            }
            else break;
        }
        return Level(program, group, crumbs, ctx);
    }

    private static JObject Level(CircuitProgram p, int group, JArray crumbs, Context ctx)
    {
        var parts = new JArray();
        var nodes = new HashSet<int>();
        var nodeGroups = NodeGroups(p);
        for (int i = 0; i < p.Components.Count; i++)
        {
            var c = p.Components[i];
            if (c.Group != group || c.Kind == ComponentKind.Tube) continue;
            var o = new JObject { ["id"] = "c" + i, ["kind"] = c.Kind.ToString().ToLowerInvariant(), ["param"] = c.Param, ["nodes"] = new JArray(c.Nodes) };
            if (c.ParamNode >= 0) { o["paramNode"] = c.ParamNode; nodes.Add(c.ParamNode); }
            parts.Add(o);
            foreach (int n in c.Nodes) nodes.Add(n);
        }
        // Links with attenuation are resistors; a link belongs to the deepest group its two nodes share.
        for (int i = 0; i < p.Links.Count; i++)
        {
            var l = p.Links[i];
            if (l.Att == 0 && l.RevAtt == 0) continue;
            if (LinkGroup(p, l, nodeGroups) != group) continue;
            parts.Add(new JObject { ["id"] = "l" + i, ["kind"] = "resistor", ["param"] = l.Att, ["param2"] = l.RevAtt, ["nodes"] = new JArray(l.A, l.B) });
            nodes.Add(l.A); nodes.Add(l.B);
        }
        var boxes = new JArray();
        for (int g = 0; g < p.Groups.Count; g++)
        {
            var grp = p.Groups[g];
            if (grp.Parent != group) continue;
            bool openable = grp.Ref == null || ctx.References?.Invoke(grp.Ref) is not { } info || !info.LockView || info.AuthorUid == ctx.ViewerUid;
            boxes.Add(new JObject
            {
                ["id"] = "g" + g, ["name"] = grp.Name ?? "", ["desc"] = grp.Description ?? "", ["soldered"] = grp.Ref != null, ["openable"] = openable,
                ["step"] = grp.Ref == null ? "g:" + g : "r:" + grp.Ref,
                ["pins"] = new JArray(grp.Pins.Select(gp => PinJson(gp.Index, gp.Role, gp.Name, gp.Node)))
            });
            foreach (var gp in grp.Pins) nodes.Add(gp.Node);
        }
        var pins = new JArray();
        if (group < 0)
            foreach (var pin in p.Pins.OrderBy(x => x.Index))
            {
                var o = PinJson(pin.Index, pin.Role, pin.Name, pin.Node);
                if (pin.Component >= 0) o["part"] = "c" + pin.Component;
                pins.Add(o);
                if (pin.Node >= 0) nodes.Add(pin.Node);
            }
        else
            foreach (var gp in p.Groups[group].Pins.OrderBy(x => x.Index))
            {
                pins.Add(PinJson(gp.Index, gp.Role, gp.Name, gp.Node));
                nodes.Add(gp.Node);
            }
        return new JObject { ["locked"] = false, ["crumbs"] = crumbs, ["pins"] = pins, ["parts"] = parts, ["boxes"] = boxes, ["nodes"] = new JArray(nodes.OrderBy(n => n)) };
    }

    private static JObject PinJson(int index, PinRole role, string name, int node)
    {
        var o = new JObject { ["i"] = index, ["r"] = role.ToString().ToLowerInvariant(), ["node"] = node };
        if (!string.IsNullOrEmpty(name)) o["name"] = name;
        return o;
    }

    // Which groups touch each node, through their parts and their pins.
    private static Dictionary<int, HashSet<int>> NodeGroups(CircuitProgram p)
    {
        var map = new Dictionary<int, HashSet<int>>();
        void Touch(int node, int group) { if (!map.TryGetValue(node, out var set)) map[node] = set = new HashSet<int>(); set.Add(group); }
        foreach (var c in p.Components)
        {
            foreach (int n in c.Nodes) Touch(n, c.Group);
            if (c.ParamNode >= 0) Touch(c.ParamNode, c.Group);
        }
        // A group's pin node belongs to both sides of the boundary; the program's own pins to the root.
        for (int g = 0; g < p.Groups.Count; g++)
            foreach (var gp in p.Groups[g].Pins) { Touch(gp.Node, p.Groups[g].Parent); Touch(gp.Node, g); }
        foreach (var pin in p.Pins) if (pin.Node >= 0) Touch(pin.Node, -1);
        return map;
    }

    private static int LinkGroup(CircuitProgram p, Link l, Dictionary<int, HashSet<int>> nodeGroups)
    {
        if (!nodeGroups.TryGetValue(l.A, out var a) || !nodeGroups.TryGetValue(l.B, out var b)) return -1;
        var common = a.Intersect(b).Where(g => g >= 0).ToList();
        if (common.Count == 0) return -1;
        return common.OrderByDescending(g => Depth(p, g)).First();
    }

    private static int Depth(CircuitProgram p, int g)
    {
        int d = 0;
        while (g >= 0 && d < 64) { g = p.Groups[g].Parent; d++; }
        return d;
    }
}
