namespace SignalsTubes.src.schematic.routing;

public enum Heading { Left, Right, Up, Down }

/// <summary>An obstacle wires may not cross: a part's rectangle.</summary>
public readonly record struct RouteRect(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
    public bool Contains(double x, double y) => x > X && x < Right && y > Y && y < Bottom;
}

/// <summary>Where a wire leaves a part: the point on its edge and the direction it must leave in.</summary>
public readonly record struct RoutePort(double X, double Y, Heading Out);

public sealed class RouteNet
{
    public int Id;
    public List<RoutePort> Ports = new();
}

public sealed class RoutedNet
{
    public int Id;
    public List<(double x1, double y1, double x2, double y2)> Segments = new();
    public List<(double x, double y)> Junctions = new();
    public bool Complete = true;   // false when some port could not be reached
}

/// <summary>
/// Orthogonal connector routing on a grid, in the spirit of libavoid: parts are obstacles, every
/// net becomes a tree of horizontal and vertical runs found by Dijkstra with costs for length,
/// bends and crossings. A run never enters an obstacle and never lies on top of another net's run;
/// orthogonal crossings are allowed. The result is deterministic for a given input.
/// </summary>
public sealed class OrthogonalRouter
{
    public double Cell = 5;            // grid pitch in drawing units
    public double Margin = 4;          // obstacles grow by this much
    public int Border = 4;             // free cells around the drawing
    public int StepCost = 10, BendCost = 30, CrossCost = 20, NearCost = 25;

    private int cols, rows, ox, oy;    // grid size and origin offset (cells)
    private bool[,] inside, near;      // inside a part (never), within its margin (costly)
    private int[,] reserved;           // -1 = free, else the net whose port stub owns the cell
    private int[,] horizontalNet, verticalNet;   // -1 = free, else net id owning a run through the cell in that orientation

    private static readonly (int dx, int dy)[] Steps = { (-1, 0), (1, 0), (0, -1), (0, 1) };   // Left, Right, Up, Down

    public List<RoutedNet> Route(double width, double height, IReadOnlyList<RouteRect> obstacles, IReadOnlyList<RouteNet> nets)
    {
        ox = oy = Border;
        cols = (int)Math.Ceiling(width / Cell) + 2 * Border + 1;
        rows = (int)Math.Ceiling(height / Cell) + 2 * Border + 1;
        inside = new bool[cols, rows];
        near = new bool[cols, rows];
        reserved = new int[cols, rows];
        horizontalNet = new int[cols, rows];
        verticalNet = new int[cols, rows];
        for (int i = 0; i < cols; i++) for (int j = 0; j < rows; j++) { horizontalNet[i, j] = -1; verticalNet[i, j] = -1; reserved[i, j] = -1; }
        foreach (var r in obstacles)
            for (int i = 0; i < cols; i++) for (int j = 0; j < rows; j++)
            {
                if (r.Contains(X(i), Y(j))) inside[i, j] = true;
                else if (r.X - Margin < X(i) && X(i) < r.Right + Margin && r.Y - Margin < Y(j) && Y(j) < r.Bottom + Margin) near[i, j] = true;
            }
        // Port stubs are laid before any search, so no net can run across a cell another net's port needs.
        foreach (var net in nets)
            foreach (var p in net.Ports)
            {
                var (dx, dy) = Steps[(int)p.Out];
                var portCell = (Col(p.X), Row(p.Y));
                var start = (portCell.Item1 + dx, portCell.Item2 + dy);
                Reserve(net.Id, portCell); Reserve(net.Id, start);
                Reserve(net.Id, (start.Item1 + dx, start.Item2 + dy));   // room to turn in front of the stub
            }

        var result = new List<RoutedNet>();
        // Short nets first: they have the fewest alternatives.
        foreach (var net in nets.OrderBy(n => Span(n)).ThenBy(n => n.Id))
            result.Add(RouteNet(net));
        return result;
    }

    private void Reserve(int netId, (int, int) cell)
    {
        if (cell.Item1 < 0 || cell.Item2 < 0 || cell.Item1 >= cols || cell.Item2 >= rows) return;
        reserved[cell.Item1, cell.Item2] = netId;
    }

    private double X(int i) => (i - ox) * Cell;
    private double Y(int j) => (j - oy) * Cell;
    private int Col(double x) => (int)Math.Round(x / Cell) + ox;
    private int Row(double y) => (int)Math.Round(y / Cell) + oy;

    private static double Span(RouteNet n) =>
        n.Ports.Count < 2 ? 0 : n.Ports.Max(p => p.X) - n.Ports.Min(p => p.X) + n.Ports.Max(p => p.Y) - n.Ports.Min(p => p.Y);

    private RoutedNet RouteNet(RouteNet net)
    {
        var routed = new RoutedNet { Id = net.Id };
        if (net.Ports.Count == 0) return routed;
        // Grid cells and the run orientation used by this net, for junctions and for later paths to join.
        var tree = new Dictionary<(int, int), int>();   // cell -> bit mask of headings used by this net at that cell
        var pending = net.Ports.Select(p => (port: p, cell: Snap(p))).ToList();
        var first = pending[0];
        pending.RemoveAt(0);
        Stub(routed, first.port, first.cell);
        Claim(net.Id, tree, first.cell, first.cell, first.port.Out);
        while (pending.Count > 0)
        {
            // nearest pending port to the tree so far
            int best = 0; double bestDist = double.MaxValue;
            for (int k = 0; k < pending.Count; k++)
            {
                double d = tree.Keys.Min(c => Math.Abs(c.Item1 - pending[k].cell.Item1) + Math.Abs(c.Item2 - pending[k].cell.Item2));
                if (d < bestDist) { bestDist = d; best = k; }
            }
            var (port, cell) = pending[best];
            pending.RemoveAt(best);
            Stub(routed, port, cell);
            var path = Search(net.Id, cell, port.Out, tree);
            if (path == null) { routed.Complete = false; Claim(net.Id, tree, cell, cell, port.Out); continue; }
            Lay(net.Id, routed, tree, path);
        }
        foreach (var (c, mask) in tree)
            if (System.Numerics.BitOperations.PopCount((uint)mask) >= 3) routed.Junctions.Add((X(c.Item1), Y(c.Item2)));
        Merge(routed);
        return routed;
    }

    private (int, int) Snap(RoutePort p)
    {
        // The port's own cell lies on the part's edge; the wire starts one cell outward, in the leaving direction.
        var (dx, dy) = Steps[(int)p.Out];
        return (Col(p.X) + dx, Row(p.Y) + dy);
    }

    // Straight piece from the exact port point to its first grid cell.
    private void Stub(RoutedNet routed, RoutePort p, (int, int) cell)
    {
        double cx = X(cell.Item1), cy = Y(cell.Item2);
        if (p.Out is Heading.Left or Heading.Right) routed.Segments.Add((p.X, p.Y, cx, p.Y));
        else routed.Segments.Add((p.X, p.Y, p.X, cy));
        if (Math.Abs(p.X - cx) > 1e-9 && Math.Abs(p.Y - cy) > 1e-9)
            routed.Segments.Add(p.Out is Heading.Left or Heading.Right ? (cx, p.Y, cx, cy) : (p.X, cy, cx, cy));
    }

    private void Claim(int netId, Dictionary<(int, int), int> tree, (int, int) from, (int, int) to, Heading heading)
    {
        int mask = 1 << (int)heading;
        tree[from] = (tree.TryGetValue(from, out int m) ? m : 0) | mask;
        if (heading is Heading.Left or Heading.Right) horizontalNet[from.Item1, from.Item2] = netId; else verticalNet[from.Item1, from.Item2] = netId;
    }

    // Dijkstra over (cell, heading) from the port's first cell to any cell of the net's tree.
    private List<(int, int)> Search(int netId, (int, int) start, Heading startHeading, Dictionary<(int, int), int> tree)
    {
        var dist = new Dictionary<((int, int), int), int>();
        var prev = new Dictionary<((int, int), int), ((int, int), int)>();
        var queue = new PriorityQueue<((int, int) cell, int heading), int>();
        var s = (start, (int)startHeading);
        dist[s] = 0;
        queue.Enqueue(s, 0);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            int d = dist[cur];
            if (tree.ContainsKey(cur.cell) && cur.cell != start)
            {
                var path = new List<(int, int)>();
                var at = cur;
                while (true)
                {
                    path.Add(at.cell);
                    if (!prev.TryGetValue(at, out var p)) break;
                    at = p;
                }
                path.Reverse();
                return path;
            }
            bool forwardOpen = cur.cell != start || Enterable(netId, (start.Item1 + Steps[(int)startHeading].dx, start.Item2 + Steps[(int)startHeading].dy), (int)startHeading, tree);
            for (int h = 0; h < 4; h++)
            {
                // leave the part the way the port faces; only when that cell is taken may the wire turn right at the stub
                if (cur.cell == start && h != (int)startHeading && (forwardOpen || h == Opposite((int)startHeading))) continue;
                if (h == Opposite(cur.heading)) continue;                            // no doubling back
                var next = (cur.cell.Item1 + Steps[h].dx, cur.cell.Item2 + Steps[h].dy);
                if (next.Item1 < 0 || next.Item2 < 0 || next.Item1 >= cols || next.Item2 >= rows) continue;
                bool joins = tree.ContainsKey(next);
                if (inside[next.Item1, next.Item2] && !joins) continue;
                int owner = reserved[next.Item1, next.Item2];
                if (owner >= 0 && owner != netId) continue;                           // another net's port stub
                int cost = d + StepCost + (near[next.Item1, next.Item2] ? NearCost : 0);
                bool horizontal = h is 0 or 1;
                int alongOwner = horizontal ? horizontalNet[next.Item1, next.Item2] : verticalNet[next.Item1, next.Item2];
                int acrossOwner = horizontal ? verticalNet[next.Item1, next.Item2] : horizontalNet[next.Item1, next.Item2];
                if (alongOwner >= 0 && alongOwner != netId) continue;                // never lie on another net's run
                if (acrossOwner >= 0 && acrossOwner != netId) cost += CrossCost;
                if (h != cur.heading) cost += BendCost;
                var key = (next, h);
                if (dist.TryGetValue(key, out int old) && old <= cost) continue;
                dist[key] = cost;
                prev[key] = cur;
                queue.Enqueue(key, cost);
            }
        }
        return null;
    }

    private bool Enterable(int netId, (int, int) next, int h, Dictionary<(int, int), int> tree)
    {
        if (next.Item1 < 0 || next.Item2 < 0 || next.Item1 >= cols || next.Item2 >= rows) return false;
        if (inside[next.Item1, next.Item2] && !tree.ContainsKey(next)) return false;
        int owner = reserved[next.Item1, next.Item2];
        if (owner >= 0 && owner != netId) return false;
        int along = h is 0 or 1 ? horizontalNet[next.Item1, next.Item2] : verticalNet[next.Item1, next.Item2];
        return along < 0 || along == netId;
    }

    private static int Opposite(int heading) => heading switch { 0 => 1, 1 => 0, 2 => 3, _ => 2 };

    private void Lay(int netId, RoutedNet routed, Dictionary<(int, int), int> tree, List<(int, int)> path)
    {
        for (int k = 0; k + 1 < path.Count; k++)
        {
            var a = path[k]; var b = path[k + 1];
            var heading = Heading.Right;
            if (b.Item1 < a.Item1) heading = Heading.Left; else if (b.Item2 < a.Item2) heading = Heading.Up; else if (b.Item2 > a.Item2) heading = Heading.Down;
            Claim(netId, tree, a, b, heading);
            Claim(netId, tree, b, a, (Heading)Opposite((int)heading));
            routed.Segments.Add((X(a.Item1), Y(a.Item2), X(b.Item1), Y(b.Item2)));
        }
    }

    // Collinear consecutive pieces become one segment.
    private static void Merge(RoutedNet routed)
    {
        var merged = new List<(double x1, double y1, double x2, double y2)>();
        foreach (var s in routed.Segments)
        {
            if (merged.Count > 0)
            {
                var l = merged[^1];
                bool sameRow = Eq(l.y1, l.y2) && Eq(s.y1, s.y2) && Eq(l.y1, s.y1) && Eq(l.x2, s.x1);
                bool sameCol = Eq(l.x1, l.x2) && Eq(s.x1, s.x2) && Eq(l.x1, s.x1) && Eq(l.y2, s.y1);
                if (sameRow || sameCol) { merged[^1] = (l.x1, l.y1, s.x2, s.y2); continue; }
            }
            if (!(Eq(s.x1, s.x2) && Eq(s.y1, s.y2))) merged.Add(s);
        }
        routed.Segments = merged;
    }

    private static bool Eq(double a, double b) => Math.Abs(a - b) < 1e-6;
}
