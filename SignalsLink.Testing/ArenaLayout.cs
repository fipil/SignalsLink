using Vintagestory.API.MathTools;

namespace SignalsLink.Testing;

public sealed record ArenaLocation(int X, int Y, int Z, int Dimension, int Version = 1)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public BlockPos Origin => new BlockPos(X, Y, Z, Dimension);
}

/// <summary>A small elevated platform; its entire volume must originally be air.</summary>
public sealed class ArenaLayout
{
    public const int Width = 9, Height = 5, Depth = 5;
    public ArenaLocation Location { get; }
    public ArenaLayout(ArenaLocation location) { Location = location; }
    public BlockPos At(int x, int y, int z) => Location.Origin.AddCopy(x, y, z);
    public BlockPos Power => At(0, 1, 2);
    public BlockPos Switch => At(2, 1, 2);
    public BlockPos Target => At(4, 1, 2);
    public BlockPos Chute => At(4, 2, 2);
    public BlockPos Source => At(4, 3, 2);
    public BlockPos Output => At(6, 1, 2);
    public IEnumerable<BlockPos> Cells()
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                for (int z = 0; z < Depth; z++) yield return At(x, y, z);
    }
    public bool Contains(BlockPos p) => p != null && p.dimension == Location.Dimension
        && p.X >= Location.X && p.X < Location.X + Width
        && p.Y >= Location.Y && p.Y < Location.Y + Height
        && p.Z >= Location.Z && p.Z < Location.Z + Depth;
    public string ExpectedCode(BlockPos p)
    {
        if (!Contains(p)) return null;
        if (p.Y == Location.Y) return "game:rock-granite";
        if (p.Equals(Power)) return "signals:blocksource";
        if (p.Equals(Switch)) return "signals:knifeswitch-north-down-off";
        if (p.Equals(Target) || p.Equals(Source)) return "game:chest-east";
        if (p.Equals(Chute)) return "signalslink:managedchute-north-up";
        if (p.Equals(Output)) return "signals:connection-down";
        return null;
    }
    public bool IsOwnedBlock(BlockPos p, string code) => Contains(p) && code != null
        && (code == ExpectedCode(p) || (p.Equals(Switch) && code == "signals:knifeswitch-north-down-on"));
}
