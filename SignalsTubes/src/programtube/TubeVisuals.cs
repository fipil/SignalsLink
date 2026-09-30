using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsTubes.src.programtube;

/// <summary>
/// Look of a tube from its portable data only: contacts from the program's pins and a glyph from the
/// wiring fingerprint, so the same circuit shows the same rune wherever it was imprinted.
/// </summary>
public static class TubeVisuals
{
    public const string GlyphTexture = "tube-glyph";
    public const int GlyphGlow = 200;   // same as the Signals light bulb
    // 5 x 7 cells, mirrored about the middle column, so the rune reads the same from both sides.
    private const int Columns = 5, Rows = 7;
    private const float Cell = .6f, GlyphLeft = 6.5f, GlyphBottom = 6.4f, GlyphZ0 = 7.85f, GlyphZ1 = 8.15f;
    private const float TexU = 24, TexV = 0;   // turquoise area of the glyph texture

    /// <summary>Bit i = pin i present. From the program; blank tubes fall back to the pinCount attribute.</summary>
    public static int PinMask(ItemStack stack)
    {
        int mask = TubeProgram.PinMask(stack);
        return mask != 0 ? mask : (1 << Math.Clamp(stack.Attributes.GetInt("pinCount", 8), 1, 8)) - 1;
    }

    public static int PinCount(ItemStack stack) => System.Numerics.BitOperations.PopCount((uint)PinMask(stack));

    /// <summary>Empty for a blank tube.</summary>
    public static string Fingerprint(ItemStack stack) => TubeProgram.Fingerprint(stack) ?? "";

    public static string MeshKey(ItemStack stack, bool lit) => $"{PinMask(stack)}:{Fingerprint(stack)}:{(lit ? 1 : 0)}";

    public static Shape Build(Shape template, ItemStack stack, bool lit = false)
    {
        Shape shape = template.Clone();
        int pins = PinMask(stack);
        var parts = shape.Elements.Where(e => !e.Name.StartsWith("glyph_") && !e.Name.StartsWith("bulb_")
            && (!e.Name.StartsWith("tube_pin_") || (pins >> int.Parse(e.Name[9..]) & 1) != 0)).ToList();
        string fingerprint = Fingerprint(stack);
        if (fingerprint.Length > 0) parts.AddRange(Glyph(GlyphCells(Convert.FromHexString(fingerprint)), lit));
        parts.AddRange(shape.Elements.Where(e => e.Name.StartsWith("bulb_")));
        shape.Elements = parts.ToArray();
        return shape;
    }

    /// <summary>Lit cells as [row, column], row 0 at the top. The 21 free cells come from the first 21 hash bits.</summary>
    public static bool[,] GlyphCells(byte[] hash)
    {
        const int free = Columns / 2 + 1;
        var cells = new bool[Rows, Columns];
        int bit = 0;
        for (int row = 0; row < Rows; row++)
            for (int col = 0; col < free; col++, bit++)
                cells[row, col] = cells[row, Columns - 1 - col] = Bit(hash, bit);
        // Too sparse a rune reads as damage; borrow further bits until it has some body.
        for (int extra = Rows * free; Lit(cells) < 8 && extra < hash.Length * 8; extra++)
        {
            int cellIndex = extra % (Rows * free);
            int row = cellIndex / free, col = cellIndex % free;
            if (Bit(hash, extra)) cells[row, col] = cells[row, Columns - 1 - col] = true;
        }
        return cells;
    }

    private static bool Bit(byte[] hash, int i) => (hash[i / 8] >> i % 8 & 1) != 0;

    private static int Lit(bool[,] cells)
    {
        int n = 0;
        foreach (bool c in cells) if (c) n++;
        return n;
    }

    // One cuboid per horizontal run of lit cells.
    private static IEnumerable<ShapeElement> Glyph(bool[,] cells, bool lit)
    {
        for (int row = 0; row < Rows; row++)
        {
            float y0 = GlyphBottom + (Rows - 1 - row) * Cell;
            for (int col = 0; col < Columns;)
            {
                if (!cells[row, col]) { col++; continue; }
                int end = col;
                while (end < Columns && cells[row, end]) end++;
                yield return Cuboid($"glyph_{row}_{col}", new double[] { GlyphLeft + col * Cell, y0, GlyphZ0 },
                    new double[] { GlyphLeft + end * Cell, y0 + Cell, GlyphZ1 }, GlyphTexture, lit ? GlyphGlow : 0);
                col = end;
            }
        }
    }

    /// <summary>Appends a tube shape into another shape, scaled and moved. Glass stays last.</summary>
    public static void Append(Shape target, Shape tube, float scale, Vec3f offset)
    {
        var parts = tube.Elements.Select(e => e.Clone()).ToArray();
        foreach (var e in parts)
        {
            for (int i = 0; i < 3; i++)
            {
                e.From[i] = e.From[i] * scale + offset[i];
                e.To[i] = e.To[i] * scale + offset[i];
                if (e.RotationOrigin != null) e.RotationOrigin[i] = e.RotationOrigin[i] * scale + offset[i];
            }
            e.Name = "tube_" + e.Name;
        }
        foreach (var kv in tube.Textures) target.Textures[kv.Key] = kv.Value;
        foreach (var kv in tube.TextureSizes) target.TextureSizes[kv.Key] = kv.Value;  // else the glyph UV is clipped to 16
        target.Elements = target.Elements.Where(e => e.RenderPass != 3).Concat(parts.Where(e => e.RenderPass != 3))
            .Concat(target.Elements.Where(e => e.RenderPass == 3)).Concat(parts.Where(e => e.RenderPass == 3)).ToArray();
    }

    public static void Retexture(Shape shape, string from, string to)
    {
        foreach (var e in shape.Elements)
            foreach (var f in e.FacesResolved)
                if (f != null && f.Texture == from) f.Texture = to;
        if (shape.Textures.Remove(from, out var loc)) shape.Textures[to] = loc;
    }

    private static ShapeElement Cuboid(string name, double[] from, double[] to, string texture, int glow)
    {
        var e = new ShapeElement { Name = name, From = from, To = to, RenderPass = 0 };
        for (int side = 0; side < 6; side++)
        {
            double w = side == 1 || side == 3 ? to[2] - from[2] : to[0] - from[0];
            double h = side >= 4 ? to[2] - from[2] : to[1] - from[1];
            e.FacesResolved[side] = new ShapeElementFace { Texture = texture, Uv = new[] { TexU, TexV, TexU + (float)w, TexV + (float)h }, Enabled = true, Glow = glow };
        }
        return e;
    }
}
