using SignalsTubes.src.schematic.routing;

namespace SignalsTubes.Tests;

public class OrthogonalRouterTests
{
    private static RouteRect Rect(double x, double y, double w = 50, double h = 40) => new(x, y, w, h);
    private static RoutePort Left(RouteRect r, double f = .5) => new(r.X, r.Y + r.H * f, Heading.Left);
    private static RoutePort Right(RouteRect r, double f = .5) => new(r.Right, r.Y + r.H * f, Heading.Right);
    private static RoutePort Bottom(RouteRect r, double f = .5) => new(r.X + r.W * f, r.Bottom, Heading.Down);

    /// <summary>Everything a correct routing must satisfy, checked for any input.</summary>
    private static void AssertValid(IReadOnlyList<RouteRect> obstacles, IReadOnlyList<RouteNet> nets, List<RoutedNet> routed)
    {
        Assert.Equal(nets.Count, routed.Count);
        foreach (var r in routed)
        {
            var net = nets.Single(n => n.Id == r.Id);
            Assert.True(r.Complete, $"net {r.Id} incomplete");
            foreach (var s in r.Segments)
            {
                Assert.True(Math.Abs(s.x1 - s.x2) < 1e-6 || Math.Abs(s.y1 - s.y2) < 1e-6, "segments are axis aligned");
                // never through a part (the port stubs touch their own part's edge, which Contains excludes)
                foreach (var o in obstacles)
                    foreach (var (x, y) in Samples(s))
                        Assert.False(o.Contains(x, y), $"net {r.Id} runs through obstacle at {o} near ({x:0.#},{y:0.#})");
            }
            // every port is connected to every other port of the net through the segments (T joins count)
            if (net.Ports.Count > 1)
            {
                var reached = new HashSet<int>();
                for (int k = 0; k < r.Segments.Count; k++) if (OnSegment(r.Segments[k], net.Ports[0].X, net.Ports[0].Y)) reached.Add(k);
                Assert.NotEmpty(reached);
                bool grew = true;
                while (grew)
                {
                    grew = false;
                    for (int k = 0; k < r.Segments.Count; k++)
                        if (!reached.Contains(k) && reached.Any(j => Touch(r.Segments[j], r.Segments[k]))) { reached.Add(k); grew = true; }
                }
                foreach (var p in net.Ports) Assert.True(reached.Any(k => OnSegment(r.Segments[k], p.X, p.Y)), $"port ({p.X},{p.Y}) of net {r.Id} is not connected");
            }
        }
        // runs of different nets never share a stretch (crossing at a point is fine)
        for (int i = 0; i < routed.Count; i++)
            for (int j = i + 1; j < routed.Count; j++)
                foreach (var a in routed[i].Segments)
                    foreach (var b in routed[j].Segments)
                        Assert.False(Overlap(a, b), $"nets {routed[i].Id} and {routed[j].Id} overlap on {a} / {b}");
    }

    private static IEnumerable<(double, double)> Samples((double x1, double y1, double x2, double y2) s)
    {
        int n = (int)Math.Max(2, Math.Ceiling(Math.Abs(s.x2 - s.x1) + Math.Abs(s.y2 - s.y1)));
        for (int k = 1; k < n; k++) { double t = (double)k / n; yield return (s.x1 + (s.x2 - s.x1) * t, s.y1 + (s.y2 - s.y1) * t); }
    }

    private static bool OnSegment((double x1, double y1, double x2, double y2) s, double x, double y) =>
        x >= Math.Min(s.x1, s.x2) - 1e-6 && x <= Math.Max(s.x1, s.x2) + 1e-6 && y >= Math.Min(s.y1, s.y2) - 1e-6 && y <= Math.Max(s.y1, s.y2) + 1e-6
        && (Math.Abs(s.x1 - s.x2) < 1e-6 ? Math.Abs(x - s.x1) < 1e-6 : Math.Abs(y - s.y1) < 1e-6);

    private static bool Touch((double x1, double y1, double x2, double y2) a, (double x1, double y1, double x2, double y2) b) =>
        OnSegment(a, b.x1, b.y1) || OnSegment(a, b.x2, b.y2) || OnSegment(b, a.x1, a.y1) || OnSegment(b, a.x2, a.y2);

    private static bool Overlap((double x1, double y1, double x2, double y2) a, (double x1, double y1, double x2, double y2) b)
    {
        bool ah = Math.Abs(a.y1 - a.y2) < 1e-6, bh = Math.Abs(b.y1 - b.y2) < 1e-6;
        if (ah != bh) return false;
        if (ah)
        {
            if (Math.Abs(a.y1 - b.y1) > 1e-6) return false;
            double lo = Math.Max(Math.Min(a.x1, a.x2), Math.Min(b.x1, b.x2)), hi = Math.Min(Math.Max(a.x1, a.x2), Math.Max(b.x1, b.x2));
            return hi - lo > 1e-6;
        }
        else
        {
            if (Math.Abs(a.x1 - b.x1) > 1e-6) return false;
            double lo = Math.Max(Math.Min(a.y1, a.y2), Math.Min(b.y1, b.y2)), hi = Math.Min(Math.Max(a.y1, a.y2), Math.Max(b.y1, b.y2));
            return hi - lo > 1e-6;
        }
    }

    [Fact]
    public void StraightConnectionBetweenFacingPortsIsOneLine()
    {
        var a = Rect(20, 20); var b = Rect(200, 20);
        var nets = new List<RouteNet> { new() { Id = 1, Ports = { Right(a), Left(b) } } };
        var routed = new OrthogonalRouter().Route(300, 100, new[] { a, b }, nets);
        AssertValid(new[] { a, b }, nets, routed);
        Assert.All(routed[0].Segments, s => Assert.Equal(s.y1, s.y2));   // one straight row, no detour
        Assert.Empty(routed[0].Junctions);
    }

    [Fact]
    public void AWireGoesAroundAPartInTheWay()
    {
        var a = Rect(20, 20); var wall = Rect(130, 0, 50, 80); var b = Rect(240, 20);
        var nets = new List<RouteNet> { new() { Id = 1, Ports = { Right(a), Left(b) } } };
        var routed = new OrthogonalRouter().Route(320, 140, new[] { a, wall, b }, nets);
        AssertValid(new[] { a, wall, b }, nets, routed);
        Assert.True(routed[0].Segments.Count >= 3);
    }

    [Fact]
    public void FeedbackToTheRightAndReaderAcrossAPartNeverCutThroughIt()
    {
        // input pin left, a toggle, an output pin right; the trigger shares the net with the right terminal
        var pin4 = Rect(0, 30, 46, 22); var toggle = Rect(120, 20, 60, 52); var pin6 = Rect(300, 30, 46, 22); var source = Rect(60, 120, 40, 40);
        var feedback = new RouteNet { Id = 1, Ports = { Right(pin4), new(toggle.X, toggle.Y + toggle.H * .22, Heading.Left), Right(toggle, .75), Left(pin6) } };
        var supply = new RouteNet { Id = 0, Ports = { Right(source), Left(toggle, .75) } };
        var obstacles = new[] { pin4, toggle, pin6, source };
        var routed = new OrthogonalRouter().Route(360, 180, obstacles, new[] { feedback, supply });
        AssertValid(obstacles, new[] { feedback, supply }, routed);
        Assert.Contains(routed, r => r.Id == 1 && r.Junctions.Count >= 1);
    }

    [Fact]
    public void CathodesOnOneSupplyNetStayOutOfTheTubes()
    {
        var source = Rect(20, 150, 40, 40); var t1 = Rect(120, 10, 56, 60); var t2 = Rect(120, 90, 56, 60);
        var net = new RouteNet { Id = 3, Ports = { Right(source), Bottom(t1), Bottom(t2) } };
        var obstacles = new[] { source, t1, t2 };
        var routed = new OrthogonalRouter().Route(220, 220, obstacles, new[] { net });
        AssertValid(obstacles, new[] { net }, routed);
    }

    [Fact]
    public void SameInputGivesSameOutput()
    {
        var (obstacles, nets) = RandomCircuit(7, 12, 9);
        var a = new OrthogonalRouter().Route(600, 400, obstacles, nets);
        var b = new OrthogonalRouter().Route(600, 400, obstacles, nets);
        Assert.Equal(a.Select(r => string.Join(";", r.Segments)), b.Select(r => string.Join(";", r.Segments)));
    }

    public static IEnumerable<object[]> Seeds() => Enumerable.Range(1, 40).Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(Seeds))]
    public void RandomCircuitsAreRoutedWithoutCuttingPartsOrOverlappingNets(int seed)
    {
        var (obstacles, nets) = RandomCircuit(seed, 6 + seed % 14, 4 + seed % 12);
        var routed = new OrthogonalRouter().Route(600, 400, obstacles, nets);
        AssertValid(obstacles, nets, routed);
    }

    // Parts on a loose grid so they never overlap; nets pick 2-4 random ports on their left/right/bottom edges.
    private static (List<RouteRect>, List<RouteNet>) RandomCircuit(int seed, int parts, int netCount)
    {
        var rnd = new Random(seed);
        var slots = Enumerable.Range(0, 20).OrderBy(_ => rnd.Next()).Take(parts).ToList();
        var obstacles = slots.Select(s => new RouteRect(20 + s % 5 * 115 + rnd.Next(0, 20), 20 + s / 5 * 95 + rnd.Next(0, 20), 40 + rnd.Next(0, 30), 30 + rnd.Next(0, 30))).ToList();
        var ports = new List<RoutePort>();
        foreach (var o in obstacles)
        {
            ports.Add(Left(o, .3)); ports.Add(Left(o, .7)); ports.Add(Right(o, .4)); ports.Add(Bottom(o, .5));
        }
        var free = ports.OrderBy(_ => rnd.Next()).ToList();
        var nets = new List<RouteNet>();
        for (int i = 0; i < netCount && free.Count >= 2; i++)
        {
            int take = Math.Min(free.Count, 2 + rnd.Next(0, 3));
            var net = new RouteNet { Id = i };
            net.Ports.AddRange(free.Take(take));
            free.RemoveRange(0, take);
            nets.Add(net);
        }
        return (obstacles, nets);
    }
}
