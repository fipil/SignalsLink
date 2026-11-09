using System.Numerics;

namespace SignalsLink.src.signals.clock;

/// <summary>
/// The figures the lights form inside the bulb: for each group and each of its 16 values, the corners its
/// points stand in. A letter's position in the string is the point's id, so a transition moves the k-th
/// point from the k-th letter of the old figure to the k-th letter of the new one. Every such move runs
/// along an edge or a face diagonal of the cube, never through its inside, and four steps turn the figure
/// by a quarter about the vertical axis. Pure data, shared by the renderer and the tests.
/// </summary>
public static class ClockFigures
{
    public const int Groups = 3;
    public const int Values = 16;

    /// <summary>Points per group: phase 5, turn 4, beat 3.</summary>
    public static int PointCount(int group) => Table(group)[0].Length;

    public static int TotalPoints => 5 + 4 + 3;

    /// <summary>Corner names of a group's figure for a value 0-15.</summary>
    public static string Figure(int group, int value) => Table(group)[value & 0xF];

    private static readonly string[] PhaseTable =
    {
        "CDAFE", "DBFHA", "BGHAF", "GDAFH", "DABGF", "ACGEB", "CHEBG", "HABGE",
        "ABCHG", "BDHFC", "DEFCH", "EBCHF", "BCDEH", "CAEGD", "AFGDE", "FCDEG",
    };

    private static readonly string[] TurnTable =
    {
        "HFEC", "CBFA", "BFGE", "DCEB", "EGFD", "DCGB", "CGHF", "ADFC",
        "FHGA", "ADHC", "DHEG", "BAGD", "GEHB", "BAED", "AEFH", "CBHA",
    };

    private static readonly string[] BeatTable =
    {
        "FBC", "CDH", "DEF", "EBG", "GCD", "DAE", "AFG", "FCH",
        "HDA", "ABF", "BGH", "GDE", "EAB", "BCG", "CHE", "HAF",
    };

    private static string[] Table(int group) => group switch { 0 => PhaseTable, 1 => TurnTable, _ => BeatTable };

    /// <summary>
    /// Unit-cube corner: A-D the bottom ring from front-left clockwise seen from above, E-H the same ring on top.
    /// X grows to the right, Y up, Z from the front wall to the back.
    /// </summary>
    public static Vector3 Corner(char name) => name switch
    {
        'A' => new Vector3(0, 0, 0),
        'B' => new Vector3(1, 0, 0),
        'C' => new Vector3(1, 0, 1),
        'D' => new Vector3(0, 0, 1),
        'E' => new Vector3(0, 1, 0),
        'F' => new Vector3(1, 1, 0),
        'G' => new Vector3(1, 1, 1),
        'H' => new Vector3(0, 1, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Not a cube corner."),
    };

    /// <summary>Where a corner of a cube with this centre and edge lies.</summary>
    public static Vector3 Position(char corner, Vector3 center, float edge) =>
        center + (Corner(corner) - new Vector3(.5f, .5f, .5f)) * edge;
}
