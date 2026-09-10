using System.Text.Json;
using SignalsLink.src.signals.yard;
using Vintagestory.API.MathTools;

namespace SignalsLink.Tests;

public class YardSignGeometryTests
{
    [Theory]
    [InlineData("north")]
    [InlineData("east")]
    [InlineData("south")]
    [InlineData("west")]
    public void Text_is_centered_just_outside_the_rotated_board(string side)
    {
        using var model = Read("shapes/block/yard/yardsign.json");
        using var block = Read("blocktypes/yardsign.json");
        var board = model.RootElement.GetProperty("elements").EnumerateArray()
            .Single(e => e.GetProperty("name").GetString() == "board");
        float[] from = Numbers(board.GetProperty("from"));
        float[] to = Numbers(board.GetProperty("to"));
        var attributes = block.RootElement.GetProperty("attributes");
        var facing = BlockFacing.FromCode(side);
        var text = YardSignRenderer.TextCenter(facing,
            attributes.GetProperty("textVoxelFront").GetSingle(),
            attributes.GetProperty("textVoxelCenterY").GetSingle());
        float angle = block.RootElement.GetProperty("shapebytype").GetProperty("*-" + side)
            .GetProperty("rotateY").GetSingle() * MathF.PI / 180;
        float x = (from[0] + to[0]) / 2 - 8;
        float z = from[2] - 8;
        float faceX = (8 + x * MathF.Cos(angle) + z * MathF.Sin(angle)) / 16;
        float faceZ = (8 - x * MathF.Sin(angle) + z * MathF.Cos(angle)) / 16;
        float dx = text.X - faceX, dz = text.Z - faceZ;
        Assert.InRange(dx * facing.Normali.X + dz * facing.Normali.Z, 0.0005f, 0.003f);
        Assert.Equal(0, dx * facing.Normali.Z - dz * facing.Normali.X, 5);
        Assert.Equal((from[1] + to[1]) / 32, text.Y, 5);
        Assert.True(attributes.GetProperty("textVoxelWidth").GetSingle() < to[0] - from[0]);
        Assert.True(attributes.GetProperty("textVoxelHeight").GetSingle() < to[1] - from[1]);
    }

    [Fact]
    public void Foot_is_six_by_six_and_post_meets_board()
    {
        using var model = Read("shapes/block/yard/yardsign.json");
        var elements = model.RootElement.GetProperty("elements").EnumerateArray()
            .ToDictionary(e => e.GetProperty("name").GetString());
        float[] footFrom = Numbers(elements["foot"].GetProperty("from"));
        float[] footTo = Numbers(elements["foot"].GetProperty("to"));
        Assert.Equal(6, footTo[0] - footFrom[0]);
        Assert.Equal(6, footTo[2] - footFrom[2]);
        Assert.Equal(footTo[1], Numbers(elements["post"].GetProperty("from"))[1]);
        Assert.Equal(Numbers(elements["board"].GetProperty("to"))[2],
            Numbers(elements["post"].GetProperty("from"))[2]);
    }

    private static float[] Numbers(JsonElement element) => element.EnumerateArray().Select(n => n.GetSingle()).ToArray();

    private static JsonDocument Read(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SignalsLink.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName, "SignalsLink", "assets", "signalslink", relative)));
    }
}