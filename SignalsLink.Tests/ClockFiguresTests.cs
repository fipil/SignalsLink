using System.Numerics;
using SignalsLink.src.signals.clock;
using Xunit;

namespace SignalsLink.Tests;

/// <summary>
/// The figure table is what makes the lights look like they turn: every step moves every point along an
/// edge or a face diagonal, nothing crosses the inside, no two points of a group meet, and four steps are
/// a quarter turn. These checks hold the table to that, so a retyped letter cannot slip through.
/// </summary>
public class ClockFiguresTests
{
    private static readonly Dictionary<char, char> QuarterTurn = new()
    {
        ['A'] = 'B', ['B'] = 'C', ['C'] = 'D', ['D'] = 'A',
        ['E'] = 'F', ['F'] = 'G', ['G'] = 'H', ['H'] = 'E',
    };

    public static IEnumerable<object[]> GroupsAndSteps()
    {
        for (int g = 0; g < ClockFigures.Groups; g++)
            for (int v = 0; v < ClockFigures.Values; v++)
                yield return new object[] { g, v };
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 4)]
    [InlineData(2, 3)]
    public void Each_group_has_its_point_count_in_every_figure(int group, int points)
    {
        Assert.Equal(points, ClockFigures.PointCount(group));
        for (int v = 0; v < ClockFigures.Values; v++)
        {
            string figure = ClockFigures.Figure(group, v);
            Assert.Equal(points, figure.Length);
            Assert.Equal(points, figure.Distinct().Count());
        }
    }

    [Theory]
    [MemberData(nameof(GroupsAndSteps))]
    public void Every_point_moves_along_an_edge_or_a_face_diagonal(int group, int value)
    {
        string from = ClockFigures.Figure(group, value), to = ClockFigures.Figure(group, (value + 1) % 16);
        for (int k = 0; k < from.Length; k++)
        {
            int changed = Changed(from[k], to[k]);
            Assert.True(changed is 1 or 2, $"group {group} {value}->{(value + 1) % 16} point {k}: {from[k]}->{to[k]} changes {changed} coordinates");
        }
    }

    [Theory]
    [MemberData(nameof(GroupsAndSteps))]
    public void No_two_points_of_a_group_meet_on_the_way(int group, int value)
    {
        string from = ClockFigures.Figure(group, value), to = ClockFigures.Figure(group, (value + 1) % 16);
        for (int i = 0; i < from.Length; i++)
            for (int j = i + 1; j < from.Length; j++)
                Assert.False(Meet(from[i], to[i], from[j], to[j]), $"group {group} {value}->{(value + 1) % 16}: points {i} and {j} meet");
    }

    [Theory]
    [MemberData(nameof(GroupsAndSteps))]
    public void Four_steps_turn_the_figure_by_a_quarter_point_by_point(int group, int value)
    {
        string turned = new(ClockFigures.Figure(group, value).Select(c => QuarterTurn[c]).ToArray());
        Assert.Equal(turned, ClockFigures.Figure(group, (value + 4) % 16));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void A_group_shows_16_different_sets_of_corners(int group)
    {
        var sets = Enumerable.Range(0, 16).Select(v => string.Concat(ClockFigures.Figure(group, v).OrderBy(c => c))).Distinct();
        Assert.Equal(16, sets.Count());
    }

    [Fact]
    public void The_first_step_of_the_beat_is_three_face_diagonals()
    {
        Assert.Equal("FBC", ClockFigures.Figure(2, 0));
        Assert.Equal("CDH", ClockFigures.Figure(2, 1));
        Assert.Equal(2, Changed('F', 'C'));
        Assert.Equal(2, Changed('B', 'D'));
        Assert.Equal(2, Changed('C', 'H'));
    }

    [Fact]
    public void Corners_sit_on_a_cube_around_the_centre()
    {
        var center = new Vector3(.5f, 7 / 16f, .5f);
        Assert.Equal(center + new Vector3(-.25f, -.25f, -.25f), ClockFigures.Position('A', center, .5f));
        Assert.Equal(center + new Vector3(.25f, .25f, .25f), ClockFigures.Position('G', center, .5f));
    }

    private static int Changed(char a, char b)
    {
        Vector3 p = ClockFigures.Corner(a), q = ClockFigures.Corner(b);
        return (p.X != q.X ? 1 : 0) + (p.Y != q.Y ? 1 : 0) + (p.Z != q.Z ? 1 : 0);
    }

    // Two linear glides coincide when p + t*d = 0 for one t in [0, 1] in all three coordinates.
    private static bool Meet(char a1, char b1, char a2, char b2)
    {
        var p = ClockFigures.Corner(a1) - ClockFigures.Corner(a2);
        var d = ClockFigures.Corner(b1) - ClockFigures.Corner(b2) - p;
        float? t = null;
        foreach (var (pi, di) in new[] { (p.X, d.X), (p.Y, d.Y), (p.Z, d.Z) })
        {
            if (di == 0) { if (pi != 0) return false; continue; }
            float ti = -pi / di;
            if (t == null) t = ti;
            else if (Math.Abs(ti - t.Value) > 1e-6f) return false;
        }
        return t == null || (t >= 0 && t <= 1);
    }
}
