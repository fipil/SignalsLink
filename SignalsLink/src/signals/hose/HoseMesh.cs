using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace SignalsLink.src.signals.hose
{
    /// <summary>
    /// Builds the sagging catenary mesh of a hose between two anchor points. Mirror of the
    /// Signals <c>WireMesh</c>, but thicker and with a deeper sag (leather hose look).
    /// </summary>
    static class HoseMesh
    {
        // Thicker than a wire (wire uses 0.015).
        const float Thickness = 0.04f;
        // Smaller catenary "a" => deeper sag (wire uses 2.0).
        const float CatenaryA = 0.5f;

        // https://en.wikipedia.org/wiki/Catenary
        static float Catenary(float x, float d = 1, float a = CatenaryA)
        {
            return a * ((float)Math.Cosh((x - (d / 2)) / a) - (float)Math.Cosh((d / 2) / a));
        }

        static Vec3f CrossProduct(Vec3f v1, Vec3f v2)
        {
            float x = v1.Y * v2.Z - v2.Y * v1.Z;
            float y = (v1.X * v2.Z - v2.X * v1.Z) * -1;
            float z = v1.X * v2.Y - v2.X * v1.Y;
            var rtn = new Vec3f(x, y, z);
            rtn.Normalize();
            return rtn;
        }

        static public MeshData MakeHoseMesh(Vec3f pos1, Vec3f pos2) => MakeHoseMesh(pos1, pos2, null, 0f);

        /// <summary>
        /// Builds the hose mesh, optionally swaying the hanging part horizontally along
        /// <paramref name="swayDir"/> by <paramref name="swayAmount"/> (signed). The offset is
        /// weighted by each sample's sag depth, so the anchored ends stay put and the lowest point
        /// swings the most — the "water pulsing through a garden hose" wobble.
        /// </summary>
        static public MeshData MakeHoseMesh(Vec3f pos1, Vec3f pos2, Vec3f swayDir, float swayAmount)
        {
            float t = Thickness;

            // Extend both ends by half the hose thickness along the chord, so the hose pokes a
            // little way INTO each anchor and fills the anchor's hole even on steep side
            // connections (otherwise the hole is half-empty and looks wrong).
            float clen = pos2.DistanceTo(pos1);
            if (clen > 1e-6f)
            {
                Vec3f u = (pos2 - pos1) * (1f / clen);
                pos1 = pos1 - u * (t * 0.5f);
                pos2 = pos2 + u * (t * 0.5f);
            }

            Vec3f dPos = pos2 - pos1;
            float dist = pos2.DistanceTo(pos1);

            int nSec = (int)Math.Floor(dist * 2);
            nSec = nSec > 5 ? nSec : 5;

            MeshData mesh = new MeshData(4, 6);
            mesh.SetMode(EnumDrawMode.Triangles);

            MeshData mesh_top = new MeshData(4, 6);
            mesh_top.SetMode(EnumDrawMode.Triangles);
            MeshData mesh_bot = new MeshData(4, 6);
            mesh_bot.SetMode(EnumDrawMode.Triangles);
            MeshData mesh_side = new MeshData(4, 6);
            mesh_side.SetMode(EnumDrawMode.Triangles);
            MeshData mesh_side2 = new MeshData(4, 6);
            mesh_side2.SetMode(EnumDrawMode.Triangles);

            // Out-of-plane translation vector.
            Vec3f b = new Vec3f(-dPos.Z, 0, dPos.X).Normalize();
            if (dPos.Z == 0 && dPos.X == 0)
            {
                b = new Vec3f(1, 0, 0);
            }

            mesh_top.Flags.Fill(0);
            mesh_bot.Flags.Fill(0);
            mesh_side.Flags.Fill(0);
            mesh_side2.Flags.Fill(0);

            Vec3f[] positions = new Vec3f[nSec + 1];
            float minDy = 0f; // deepest (most negative) sag, for weighting the sway
            float[] dyArr = new float[nSec + 1];
            for (int j = 0; j <= nSec; j++)
            {
                float x = dPos.X / nSec * j;
                float y = dPos.Y / nSec * j;
                float z = dPos.Z / nSec * j;
                float l = (float)Math.Sqrt(x * x + y * y + z * z);
                float dy = Catenary(l / dist, 1, CatenaryA);
                dyArr[j] = dy;
                if (dy < minDy) minDy = dy;
                positions[j] = new Vec3f(x, y + dy, z);
            }

            // Sway the hanging part horizontally, weighted by sag depth (ends fixed, middle most).
            if (swayDir != null && swayAmount != 0f && minDy < 0f)
            {
                for (int j = 0; j <= nSec; j++)
                {
                    float w = dyArr[j] / minDy; // 0 at the anchors, 1 at the deepest point
                    positions[j].X += swayDir.X * swayAmount * w;
                    positions[j].Z += swayDir.Z * swayAmount * w;
                }
            }

            Vec3f pos, pos_next, pos_before, direction, a;

            for (int j = 0; j <= nSec; j++)
            {
                pos = pos1 + positions[j];
                pos_next = j < nSec ? positions[j + 1] : positions[j];
                pos_before = j > 0 ? positions[j - 1] : positions[j];
                direction = (pos_next + pos_before).Normalize();

                a = CrossProduct(direction, b * -1);

                float du = dist / nSec;
                int color = 1;
                float uv_v = 3f / 16;

                mesh_top.AddVertex((pos - b * t + a * t).X, (pos - b * t + a * t).Y, (pos - b * t + a * t).Z, j * du, 0, color);
                mesh_top.AddVertex((pos + b * t + a * t).X, (pos + b * t + a * t).Y, (pos + b * t + a * t).Z, j * du, uv_v, color);

                mesh_bot.AddVertex((pos - b * t - a * t).X, (pos - b * t - a * t).Y, (pos - b * t - a * t).Z, j * du, 0, color);
                mesh_bot.AddVertex((pos + b * t - a * t).X, (pos + b * t - a * t).Y, (pos + b * t - a * t).Z, j * du, uv_v, color);

                mesh_side.AddVertex((pos - b * t + a * t).X, (pos - b * t + a * t).Y, (pos - b * t + a * t).Z, j * du, uv_v, color);
                mesh_side.AddVertex((pos - b * t - a * t).X, (pos - b * t - a * t).Y, (pos - b * t - a * t).Z, j * du, 0, color);

                mesh_side2.AddVertex((pos + b * t + a * t).X, (pos + b * t + a * t).Y, (pos + b * t + a * t).Z, j * du, uv_v, color);
                mesh_side2.AddVertex((pos + b * t - a * t).X, (pos + b * t - a * t).Y, (pos + b * t - a * t).Z, j * du, 0, color);

                mesh_top.Flags[2 * j] = VertexFlags.PackNormal(new Vec3f(0, 1, 0));
                mesh_top.Flags[2 * j + 1] = VertexFlags.PackNormal(new Vec3f(0, 1, 0));
                mesh_bot.Flags[2 * j] = VertexFlags.PackNormal(new Vec3f(0, -1, 0));
                mesh_bot.Flags[2 * j + 1] = VertexFlags.PackNormal(new Vec3f(0, -1, 0));
                mesh_side.Flags[2 * j] = VertexFlags.PackNormal(-b.X, -b.Y, -b.Z);
                mesh_side.Flags[2 * j + 1] = VertexFlags.PackNormal(-b.X, -b.Y, -b.Z);
                mesh_side2.Flags[2 * j] = VertexFlags.PackNormal(b);
                mesh_side2.Flags[2 * j + 1] = VertexFlags.PackNormal(b);
            }

            for (int j = 0; j < nSec; j++)
            {
                int offset = 2 * j;

                mesh_top.AddIndex(offset); mesh_top.AddIndex(offset + 3); mesh_top.AddIndex(offset + 2);
                mesh_top.AddIndex(offset); mesh_top.AddIndex(offset + 1); mesh_top.AddIndex(offset + 3);

                mesh_bot.AddIndex(offset); mesh_bot.AddIndex(offset + 3); mesh_bot.AddIndex(offset + 1);
                mesh_bot.AddIndex(offset); mesh_bot.AddIndex(offset + 2); mesh_bot.AddIndex(offset + 3);

                mesh_side.AddIndex(offset); mesh_side.AddIndex(offset + 3); mesh_side.AddIndex(offset + 1);
                mesh_side.AddIndex(offset); mesh_side.AddIndex(offset + 2); mesh_side.AddIndex(offset + 3);

                mesh_side2.AddIndex(offset); mesh_side2.AddIndex(offset + 3); mesh_side2.AddIndex(offset + 2);
                mesh_side2.AddIndex(offset); mesh_side2.AddIndex(offset + 1); mesh_side2.AddIndex(offset + 3);
            }

            mesh.AddMeshData(mesh_top);
            mesh.AddMeshData(mesh_bot);
            mesh.AddMeshData(mesh_side);
            mesh.AddMeshData(mesh_side2);
            mesh.Rgba.Fill((byte)255);

            return mesh;
        }
    }
}
