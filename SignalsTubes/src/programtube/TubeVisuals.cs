using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace SignalsMachines.src.programtube;

/// <summary>Visual identity is derived from portable program data, never world position or process hash codes.</summary>
public static class TubeVisuals
{
    public static int PinCount(ItemStack stack) => Math.Clamp(stack.Attributes.GetInt("pinCount", 8), 1, 8);

    public static string Fingerprint(ItemStack stack)
    {
        string json = stack.Attributes.GetTreeAttribute("program")?.ToJsonToken() ?? "{}";
        string canonical = Newtonsoft.Json.JsonConvert.SerializeObject(Canonicalize(JToken.Parse(json)), Newtonsoft.Json.Formatting.None);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stack.Attributes.GetString("programId", "blank") + "\n" + canonical)));
    }

    private static JToken Canonicalize(JToken token) => token switch
    {
        JObject obj => new JObject(obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new JProperty(p.Name, Canonicalize(p.Value)))),
        JArray arr => new JArray(arr.Select(Canonicalize)),
        _ => token.DeepClone()
    };

    public static Shape Build(Shape template, ItemStack stack)
    {
        Shape shape = template.Clone();
        int pins = PinCount(stack);
        var parts = shape.Elements.Where(e => !e.Name.StartsWith("component_") && !e.Name.StartsWith("bulb_")
            && (!e.Name.StartsWith("tube_pin_") || int.Parse(e.Name[9..]) < pins)).ToList();
        byte[] hash = Convert.FromHexString(Fingerprint(stack));
        string[] colors = { "tube-red", "tube-blue", "tube-yellow", "tube-dark" };
        int count = 6 + hash[0] % 5;
        for (int i = 0; i < count; i++)
        {
            int slot = i / 2;
            double x = 5.8 + slot % 2 * 2.2, y = 3.6 + slot / 2 * 2.2;
            double width = .7 + hash[i + 1] % 9 / 10.0, height = .6 + hash[i + 5] % 8 / 10.0;
            double depth = .4 + hash[i + 10] % 5 / 10.0;
            parts.Add(Cuboid("component_" + i, new[] { x, y, i % 2 == 0 ? 7.5 - depth : 8.5 },
                new[] { x + width, y + height, i % 2 == 0 ? 7.5 : 8.5 + depth }, colors[hash[i + 15] % colors.Length]));
        }
        parts.AddRange(shape.Elements.Where(e => e.Name.StartsWith("bulb_")));
        shape.Elements = parts.ToArray();
        return shape;
    }

    private static ShapeElement Cuboid(string name, double[] from, double[] to, string texture)
    {
        var e = new ShapeElement { Name = name, From = from, To = to, RenderPass = 0 };
        for (int side = 0; side < 6; side++)
        {
            double w = side == 1 || side == 3 ? to[2] - from[2] : to[0] - from[0];
            double h = side >= 4 ? to[2] - from[2] : to[1] - from[1];
            e.FacesResolved[side] = new ShapeElementFace { Texture = texture, Uv = new[] { 0f, 0f, (float)w, (float)h }, Enabled = true };
        }
        return e;
    }
}
