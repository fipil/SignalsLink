using System.Reflection;
using System.Text.Json;
using SignalsLink.src.signals.yard;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace SignalsLink.Tests;

public class YardTileMeshTests
{
    public static IEnumerable<object[]> Borders()
    {
        for (int mask = 0; mask < 256; mask++)
        {
            yield return new object[] { mask, false };
            yield return new object[] { mask, true };
        }
    }

    // Test the shipped model, including vertical walls at shared plate boundaries.
    // A test of the neighbour index arithmetic alone never exercises this geometry.
    [Theory]
    [MemberData(nameof(Borders))]
    public void Every_plate_stays_whole_for_all_border_combinations(int open, bool culledAndReordered)
    {
        var (mesh, owners) = Model(culledAndReordered);
        float[] original = (float[])mesh.xyz.Clone();
        var method = typeof(BlockYardTile).GetMethod("FlattenInnerKerbs", BindingFlags.NonPublic | BindingFlags.Static)!;
        object[] args = { mesh, (open & 1) == 0, (open & 4) == 0, (open & 8) == 0, (open & 2) == 0,
            (open & 16) == 0, (open & 32) == 0, (open & 64) == 0, (open & 128) == 0 };
        method.Invoke(null, args);

        var borders = new Dictionary<string, int>
        {
            ["base"] = 0, ["plate_center"] = 0,
            ["plate_n"] = 1, ["plate_e"] = 2, ["plate_s"] = 4, ["plate_w"] = 8,
            ["plate_nw"] = 9 | 16, ["plate_ne"] = 3 | 32, ["plate_sw"] = 12 | 64, ["plate_se"] = 6 | 128
        };
        for (int v = 0; v < mesh.VerticesCount; v++)
        {
            int at = v * 3;
            float expectedY = original[at + 1];
            if (expectedY > 1 && (borders[owners[v]] & open) == 0) expectedY = 1;
            Assert.True(Math.Abs(mesh.xyz[at + 1] - expectedY) < 0.00001f,
                $"open={open}, element={owners[v]}, vertex={v}: expected height {expectedY}, got {mesh.xyz[at + 1]}");
            Assert.Equal(original[at], mesh.xyz[at]);
            Assert.Equal(original[at + 2], mesh.xyz[at + 2]);
        }
    }

    private static (MeshData mesh, List<string> owners) Model(bool culledAndReordered)
    {
        using var json = ReadModel();
        var xyz = new List<float>();
        var faces = new List<byte>();
        var owners = new List<string>();
        var elements = json.RootElement.GetProperty("elements").EnumerateArray().ToArray();
        if (culledAndReordered) Array.Reverse(elements);
        foreach (var element in elements)
        {
            var from = element.GetProperty("from").EnumerateArray().Select(n => n.GetSingle() / 16f).ToArray();
            var to = element.GetProperty("to").EnumerateArray().Select(n => n.GetSingle() / 16f).ToArray();
            foreach (var face in element.GetProperty("faces").EnumerateObject())
            {
                if (culledAndReordered && face.Name == "up") continue;
                var facing = BlockFacing.FromCode(face.Name);
                faces.Add(facing.MeshDataIndex);
                for (int v = 0; v < 4; v++)
                {
                    owners.Add(element.GetProperty("name").GetString());
                    for (int axis = 0; axis < 3; axis++)
                        xyz.Add(CubeMeshUtil.CubeVertices[facing.Index * 12 + v * 3 + axis] < 0 ? from[axis] : to[axis]);
                }
            }
        }
        return (new MeshData { xyz = xyz.ToArray(), VerticesCount = owners.Count,
            XyzFaces = faces.ToArray(), XyzFacesCount = faces.Count }, owners);
    }
    [Fact]
    public void Vertical_texture_scale_matches_the_physical_face_dimensions()
    {
        using var json = ReadModel();
        foreach (var element in json.RootElement.GetProperty("elements").EnumerateArray())
        {
            var from = element.GetProperty("from").EnumerateArray().Select(n => n.GetSingle()).ToArray();
            var to = element.GetProperty("to").EnumerateArray().Select(n => n.GetSingle()).ToArray();
            foreach (var face in element.GetProperty("faces").EnumerateObject())
            {
                if (face.Name is "up" or "down") continue;
                var uv = face.Value.GetProperty("uv").EnumerateArray().Select(n => n.GetSingle()).ToArray();
                int widthAxis = face.Name is "north" or "south" ? 0 : 2;
                Assert.Equal(to[widthAxis] - from[widthAxis], Math.Abs(uv[2] - uv[0]), 4);
                Assert.Equal(to[1] - from[1], Math.Abs(uv[3] - uv[1]), 4);
            }
        }
    }

    private static JsonDocument ReadModel()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SignalsLink.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName,
            "SignalsLink", "assets", "signalslink", "shapes", "block", "yard", "yardtile.json")));
    }
}
