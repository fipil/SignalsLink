using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsTubes.src.imprint;

/// <summary>Sagging square-section cable between two points, same construction as the Signals wires but thicker.</summary>
public static class CableMesh
{
    public const float PlugCable = .05f, ProbeLead = .02f;
    private const float CatenaryA = 2f;   // over a normalised span, as the Signals wires: a slight sag whatever the length

    public static MeshData Make(Vec3f from, Vec3f to, float Thickness)
    {
        Vec3f d = to - from;
        float dist = Math.Max(d.Length(), .01f);
        int sections = Math.Max(5, (int)(dist * 2));
        Vec3f side = d.Z == 0 && d.X == 0 ? new Vec3f(1, 0, 0) : new Vec3f(-d.Z, 0, d.X).Normalize();

        var points = new Vec3f[sections + 1];
        for (int j = 0; j <= sections; j++)
        {
            float f = (float)j / sections;
            float sag = CatenaryA * ((float)Math.Cosh((f - .5f) / CatenaryA) - (float)Math.Cosh(.5f / CatenaryA));
            points[j] = new Vec3f(from.X + d.X * f, from.Y + d.Y * f + sag, from.Z + d.Z * f);
        }

        var mesh = new MeshData(4 * (sections + 1) * 2, 4 * sections * 6);
        mesh.SetMode(EnumDrawMode.Triangles);
        // Four strips: top, bottom, both sides. Each strip is a pair of vertices per point.
        Vec3f[] normals = { new(0, 1, 0), new(0, -1, 0), side * -1, side };
        for (int strip = 0; strip < 4; strip++)
        {
            int first = mesh.VerticesCount;
            for (int j = 0; j <= sections; j++)
            {
                Vec3f dir = (points[Math.Min(j + 1, sections)] - points[Math.Max(j - 1, 0)]).Normalize();
                Vec3f up = Cross(dir, side * -1);
                Vec3f p = points[j];
                Vec3f a, b;
                switch (strip)
                {
                    case 0: a = p - side * Thickness + up * Thickness; b = p + side * Thickness + up * Thickness; break;
                    case 1: a = p + side * Thickness - up * Thickness; b = p - side * Thickness - up * Thickness; break;
                    case 2: a = p - side * Thickness - up * Thickness; b = p - side * Thickness + up * Thickness; break;
                    default: a = p + side * Thickness + up * Thickness; b = p + side * Thickness - up * Thickness; break;
                }
                float u = j * dist / sections;
                mesh.AddVertex(a.X, a.Y, a.Z, u, 0, -1);
                mesh.AddVertex(b.X, b.Y, b.Z, u, 2f / 16, -1);
                int flag = VertexFlags.PackNormal(normals[strip]);
                mesh.Flags[mesh.VerticesCount - 2] = flag;
                mesh.Flags[mesh.VerticesCount - 1] = flag;
            }
            for (int j = 0; j < sections; j++)
            {
                int o = first + 2 * j;
                mesh.AddIndices(new[] { o, o + 3, o + 2, o, o + 1, o + 3 });
            }
        }
        return mesh;
    }

    private static Vec3f Cross(Vec3f v, Vec3f w) =>
        new Vec3f(v.Y * w.Z - w.Y * v.Z, -(v.X * w.Z - w.X * v.Z), v.X * w.Y - w.X * v.Y).Normalize();
}
