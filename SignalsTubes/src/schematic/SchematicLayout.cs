using Newtonsoft.Json.Linq;

namespace SignalsTubes.src.schematic;

/// <summary>
/// Places one schematic level like a hand-drawn diagram: signal flows left to right, inputs on the
/// left edge, outputs on the right, parts in columns by how far from the inputs they sit, wires as
/// orthogonal runs with a vertical trunk per net. Pure geometry; drawing happens elsewhere.
/// </summary>
public sealed class SchematicLayout
{
    public enum Side { Left, Right, Bottom, Top }

    public sealed class Port
    {
        public int Node; public Side Side; public double Offset;   // 0..1 along the side
        public string Name; public bool Dashed;                      // dashed: a setting fed from a node
        public double X, Y;                                         // absolute, filled by the layout
        public Element Owner;
    }

    public sealed class Element
    {
        public string Id, Kind, Label, Name, Description, Step;
        public int Param, Param2;
        public bool Soldered, Openable, IsPin, IsBox;
        public string PinRole;
        public int Column, Row;
        public double X, Y, W, H;
        public List<Port> Ports = new();
        public List<int> InputNets = new(), OutputNets = new();
    }

    public sealed class Net
    {
        public int Node;
        public List<(double x1, double y1, double x2, double y2)> Segments = new();
        public List<(double x, double y)> Junctions = new();
    }

    public const double ColumnWidth = 120, RowHeight = 70, Margin = 20;
    public List<Element> Elements = new();
    public List<Net> Nets = new();
    public double Width, Height;

    public static SchematicLayout From(JObject level)
    {
        var layout = new SchematicLayout();
        layout.Build(level);
        return layout;
    }

    private void Build(JObject level)
    {
        foreach (var p in level["pins"] as JArray ?? new JArray())
        {
            string role = (string)p["r"];
            int node = (int)p["node"];
            var e = new Element { Id = "p" + (int)p["i"], Kind = "pin", IsPin = true, PinRole = role, Param = (int)p["i"],
                Label = (string)p["name"] ?? "", W = 46, H = 22 };
            bool output = role == "output";
            e.Ports.Add(new Port { Node = node, Side = output ? Side.Left : Side.Right, Offset = .5 });
            if (output) e.InputNets.Add(node); else e.OutputNets.Add(node);
            if (node < 0 && p["part"] != null) e.Step = (string)p["part"];   // a parameter pin of this level's own part
            Elements.Add(e);
        }
        foreach (var p in level["parts"] as JArray ?? new JArray())
        {
            var e = new Element { Id = (string)p["id"], Kind = (string)p["kind"], Param = (int)p["param"], Param2 = (int?)p["param2"] ?? 0 };
            int[] n = ((JArray)p["nodes"]).Select(t => (int)t).ToArray();
            int paramNode = (int?)p["paramNode"] ?? -1;
            Shape(e, n, paramNode);
            Elements.Add(e);
        }
        foreach (var b in level["boxes"] as JArray ?? new JArray())
        {
            var e = new Element { Id = (string)b["id"], Kind = "box", IsBox = true, Name = (string)b["name"], Description = (string)b["desc"],
                Soldered = (bool)b["soldered"], Openable = (bool)b["openable"], Step = (string)b["step"], Label = (string)b["name"] };
            var pins = ((JArray)b["pins"]).Select(x => (index: (int)x["i"], role: (string)x["r"], name: (string)x["name"] ?? "", node: (int)x["node"])).ToList();
            var left = pins.Where(x => x.role != "output").ToList();
            var right = pins.Where(x => x.role == "output").ToList();
            int rows = Math.Max(1, Math.Max(left.Count, right.Count));
            e.W = 110; e.H = 24 + rows * 18;
            for (int i = 0; i < left.Count; i++) { e.Ports.Add(new Port { Node = left[i].node, Side = Side.Left, Offset = (i + 1.0) / (left.Count + 1), Name = PinLabel(left[i].index, left[i].name) }); e.InputNets.Add(left[i].node); }
            for (int i = 0; i < right.Count; i++) { e.Ports.Add(new Port { Node = right[i].node, Side = Side.Right, Offset = (i + 1.0) / (right.Count + 1), Name = PinLabel(right[i].index, right[i].name) }); e.OutputNets.Add(right[i].node); }
            Elements.Add(e);
        }
        Rank();
        Order();
        Place();
        Route();
    }

    private static string PinLabel(int index, string name) => name.Length == 0 ? (index + 1).ToString() : $"{index + 1} {name}";

    // Port layout per part kind. Left = reads, right = drives, bottom = cathode / trigger.
    private static void Shape(Element e, int[] n, int paramNode)
    {
        void In(int node, double off, string name = null, bool dashed = false) { e.Ports.Add(new Port { Node = node, Side = Side.Left, Offset = off, Name = name, Dashed = dashed }); e.InputNets.Add(node); }
        void Out(int node, double off, string name = null) { e.Ports.Add(new Port { Node = node, Side = Side.Right, Offset = off, Name = name }); e.OutputNets.Add(node); }
        void Bottom(int node, double off, string name = null) { e.Ports.Add(new Port { Node = node, Side = Side.Bottom, Offset = off, Name = name }); e.InputNets.Add(node); }
        switch (e.Kind)
        {
            case "source": e.W = 40; e.H = 40; Out(n[0], .5); break;
            case "switch": e.W = 56; e.H = 30; In(n[0], .5); Out(n[1], .5); if (paramNode >= 0) In(paramNode, .9, null, true); break;
            case "toggle": e.W = 60; e.H = 52; In(n[0], .22, "T"); In(n[1], .75); Out(n[2], .75); break;
            case "valve": e.W = 56; e.H = 60; In(n[0], .5, "g"); Bottom(n[1], .5, "k"); Out(n[2], .3, "a"); break;
            case "tetrode": e.W = 56; e.H = 68; In(n[0], .4, "g"); In(n[3], .7, "s"); Bottom(n[1], .5, "k"); Out(n[2], .25, "a"); break;
            case "delay": e.W = 60; e.H = 40; In(n[0], .5); Out(n[1], .5); if (paramNode >= 0) In(paramNode, .9, null, true); break;
            case "buffer": e.W = 44; e.H = 30; In(n[0], .5); Out(n[1], .5); break;
            case "resistor": e.W = 56; e.H = 20; In(n[0], .5); Out(n[1], .5); break;
            default: e.W = 50; e.H = 30; for (int i = 0; i < n.Length; i++) In(n[i], (i + 1.0) / (n.Length + 1)); break;
        }
    }

    // Longest path from the inputs; bidirectional parts (switch, resistor) flip when the flow comes from the right.
    private void Rank()
    {
        var netRank = new Dictionary<int, int>();
        var parts = Elements.Where(e => !e.IsPin).ToList();
        foreach (var e in Elements.Where(e => e.IsPin && e.PinRole != "output"))
            foreach (int net in e.OutputNets) netRank[net] = 0;
        int cap = parts.Count + 2;
        for (int pass = 0; pass < cap; pass++)
        {
            bool changed = false;
            foreach (var e in parts)
            {
                int r = e.InputNets.Count == 0 ? 0 : e.InputNets.Select(net => netRank.TryGetValue(net, out int v) ? v : 0).DefaultIfEmpty(0).Max();
                if (e.InputNets.Count > 0 && e.InputNets.All(net => !netRank.ContainsKey(net))) r = 0;
                if (e.Column != r && r < cap) { e.Column = r; changed = true; }
                foreach (int net in e.OutputNets)
                    if (!netRank.TryGetValue(net, out int v) || v < e.Column + 1) { if (e.Column + 1 < cap) { netRank[net] = e.Column + 1; changed = true; } }
            }
            if (!changed) break;
        }
        foreach (var e in parts.Where(e => e.Kind is "switch" or "resistor"))
        {
            int a = netRank.TryGetValue(e.InputNets[0], out int va) ? va : -1, b = netRank.TryGetValue(e.OutputNets[0], out int vb) ? vb : -1;
            if (b >= 0 && (a < 0 || b < a))
            {
                var pa = e.Ports.First(p => p.Side == Side.Left && !p.Dashed); var pb = e.Ports.First(p => p.Side == Side.Right);
                pa.Side = Side.Right; pb.Side = Side.Left;
                (e.InputNets[0], e.OutputNets[0]) = (e.OutputNets[0], e.InputNets[0]);
                e.Column = Math.Max(0, b);
            }
        }
        int last = parts.Count == 0 ? 0 : parts.Max(e => e.Column) + 1;
        foreach (var e in Elements.Where(e => e.IsPin))
            e.Column = e.PinRole == "output" ? last + 1 : -1;
        foreach (var e in parts) e.Column += 1;   // leave column 0 to the input pins
        foreach (var e in Elements.Where(e => e.IsPin && e.PinRole != "output")) e.Column = 0;
        foreach (var e in Elements.Where(e => e.IsPin && e.PinRole == "output")) e.Column = last + 1;
    }

    // Rows: a few barycenter sweeps so wires cross as little as this simple scheme allows.
    private void Order()
    {
        var columns = Elements.GroupBy(e => e.Column).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();
        foreach (var col in columns) for (int i = 0; i < col.Count; i++) col[i].Row = i;
        var rowOfNet = new Dictionary<int, double>();
        var readers = new HashSet<int>(Elements.SelectMany(e => e.InputNets));
        bool DeadEnd(Element e) => !e.IsPin && e.OutputNets.Count > 0 && e.OutputNets.All(n => !readers.Contains(n));
        for (int sweep = 0; sweep < 3; sweep++)
        {
            foreach (var col in columns)
            {
                var keyed = col.Select(e =>
                {
                    var feeds = e.InputNets.Where(rowOfNet.ContainsKey).Select(n => rowOfNet[n]).ToList();
                    // parts fed by nothing (sources) sink to the bottom, out of the way of through wires
                    double key = feeds.Count == 0 ? (e.InputNets.Count == 0 && !e.IsPin ? 1000 + e.Row : e.Row) : feeds.Average();
                    if (DeadEnd(e)) key += 500;
                    return (e, key);
                }).OrderBy(x => x.key).ThenBy(x => x.e.Row).ToList();
                for (int i = 0; i < keyed.Count; i++) keyed[i].e.Row = i;
                foreach (var e in col) foreach (int n in e.OutputNets) rowOfNet[n] = e.Row;
            }
        }
    }

    private void Place()
    {
        var columns = Elements.GroupBy(e => e.Column).OrderBy(g => g.Key).ToList();
        double x = Margin;
        double maxY = 0;
        var rail = new List<Element>();   // sources sit on a rail below the diagram, like a power supply
        foreach (var col in columns)
        {
            double colWidth = col.Max(e => e.W);
            double y = Margin;
            foreach (var e in col.OrderBy(e => e.Row))
            {
                e.X = Math.Round((x + (colWidth - e.W) / 2) / 5) * 5;   // on the router grid, so leads come out straight
                if (!e.IsPin && e.InputNets.Count == 0) { rail.Add(e); continue; }
                e.Y = Math.Round(y / 5) * 5;
                y += Math.Max(RowHeight, e.H + 24);
            }
            maxY = Math.Max(maxY, y);
            x += Math.Max(ColumnWidth, colWidth + 50);
        }
        foreach (var e in rail) e.Y = Math.Round(maxY / 5) * 5;
        if (rail.Count > 0) maxY += rail.Max(e => e.H) + 24;
        Width = x + Margin;
        Height = maxY + Margin;
        foreach (var e in Elements)
            foreach (var p in e.Ports)
            {
                p.Owner = e;
                (p.X, p.Y) = p.Side switch
                {
                    Side.Left => (e.X, e.Y + e.H * p.Offset),
                    Side.Right => (e.X + e.W, e.Y + e.H * p.Offset),
                    Side.Bottom => (e.X + e.W * p.Offset, e.Y + e.H),
                    _ => (e.X + e.W * p.Offset, e.Y)
                };
                // snap the free coordinate of each port to the router grid; the symbol draws its lead to this point
                if (p.Side is Side.Left or Side.Right) p.Y = Math.Round(p.Y / 5) * 5; else p.X = Math.Round(p.X / 5) * 5;
            }
    }

    // Wires: an obstacle-avoiding orthogonal router over all parts and pins; see routing/OrthogonalRouter.
    private void Route()
    {
        var obstacles = Elements.Select(e => new routing.RouteRect(e.X, e.Y, e.W, e.H)).ToList();
        var nets = Elements.SelectMany(e => e.Ports).Where(p => p.Node >= 0).GroupBy(p => p.Node)
            .Select(g => new routing.RouteNet { Id = g.Key, Ports = g.Select(p => new routing.RoutePort(p.X, p.Y, HeadingOf(p.Side))).ToList() })
            .ToList();
        var router = new routing.OrthogonalRouter();
        foreach (var r in router.Route(Width, Height, obstacles, nets))
            Nets.Add(new Net { Node = r.Id, Segments = r.Segments, Junctions = r.Junctions });
        // Runs may use the free border outside the drawing: shift everything back into view.
        double minX = Math.Min(0, Nets.SelectMany(n => n.Segments).Select(s => Math.Min(s.x1, s.x2)).DefaultIfEmpty(0).Min());
        double minY = Math.Min(0, Nets.SelectMany(n => n.Segments).Select(s => Math.Min(s.y1, s.y2)).DefaultIfEmpty(0).Min());
        double maxX = Math.Max(Width, Nets.SelectMany(n => n.Segments).Select(s => Math.Max(s.x1, s.x2)).DefaultIfEmpty(0).Max());
        double maxY = Math.Max(Height, Nets.SelectMany(n => n.Segments).Select(s => Math.Max(s.y1, s.y2)).DefaultIfEmpty(0).Max());
        double dx = Margin - minX, dy = Margin - minY;
        foreach (var e in Elements)
        {
            e.X += dx; e.Y += dy;
            foreach (var p in e.Ports) { p.X += dx; p.Y += dy; }
        }
        foreach (var n in Nets)
        {
            for (int i = 0; i < n.Segments.Count; i++) { var s = n.Segments[i]; n.Segments[i] = (s.x1 + dx, s.y1 + dy, s.x2 + dx, s.y2 + dy); }
            for (int i = 0; i < n.Junctions.Count; i++) { var j = n.Junctions[i]; n.Junctions[i] = (j.x + dx, j.y + dy); }
        }
        Width = maxX - minX + 2 * Margin;
        Height = maxY - minY + 2 * Margin;
    }

    private static routing.Heading HeadingOf(Side side) => side switch
    {
        Side.Left => routing.Heading.Left,
        Side.Right => routing.Heading.Right,
        Side.Bottom => routing.Heading.Down,
        _ => routing.Heading.Up
    };
}
