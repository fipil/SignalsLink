namespace SignalsTubes.src.circuit;

/// <summary>
/// Removes what wiring layout adds: nodes joined by plain wires (0/0) are one node, parallel
/// links keep the lowest attenuation per direction, self links vanish. Exact for Signals levels.
/// </summary>
public static class CircuitSimplifier
{
    public static CircuitProgram Simplify(CircuitProgram p)
    {
        int[] parent = Enumerable.Range(0, p.NodeCount).ToArray();
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        foreach (var l in p.Links)
            if (l.Att == 0 && l.RevAtt == 0) parent[Find(l.A)] = Find(l.B);

        // New ids in order of first use: pins, components, links. Unused nodes drop out.
        var map = new Dictionary<int, int>();
        int Id(int old) { int root = Find(old); if (!map.TryGetValue(root, out int id)) map[root] = id = map.Count; return id; }

        var result = new CircuitProgram();
        foreach (var pin in p.Pins)
            result.Pins.Add(new Pin { Index = pin.Index, Role = pin.Role, Node = pin.Node >= 0 ? Id(pin.Node) : -1, Component = pin.Component });
        foreach (var c in p.Components)
            result.Components.Add(new Component(c.Kind, c.Param, c.Nodes.Select(Id).ToArray()) { State = c.State?.ToArray() });

        var links = new Dictionary<(int, int), Link>();
        foreach (var l in p.Links)
        {
            if (Find(l.A) == Find(l.B)) continue;
            int a = Id(l.A), b = Id(l.B);
            byte att = l.Att, rev = l.RevAtt;
            if (a > b) { (a, b) = (b, a); (att, rev) = (rev, att); }
            if (links.TryGetValue((a, b), out var kept))
            {
                kept.Att = Math.Min(kept.Att, att);
                kept.RevAtt = Math.Min(kept.RevAtt, rev);
            }
            else links[(a, b)] = new Link(a, b, att, rev);
        }
        result.Links.AddRange(links.Values);
        result.NodeCount = map.Count;
        return result;
    }
}
