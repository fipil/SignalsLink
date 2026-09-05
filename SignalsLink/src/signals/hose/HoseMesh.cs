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

        static public MeshData MakeHoseMesh(Vec3f pos1, Vec3f pos2) => MakeHoseMesh(pos1, pos2, null, null, null, 0f);

        static public MeshData MakeHoseMesh(Vec3f pos1, Vec3f pos2, Vec3f p1ExitDir, Vec3f p2ExitDir)
            => MakeHoseMesh(pos1, pos2, p1ExitDir, p2ExitDir, null, 0f);

        /// <summary>
        /// Builds the hose mesh, optionally swaying the hanging part horizontally along
        /// <paramref name="swayDir"/> by <paramref name="swayAmount"/> (signed). The offset is
        /// weighted by each sample's sag depth, so the anchored ends stay put and the lowest point
        /// swings the most — the "water pulsing through a garden hose" wobble.
        /// </summary>
        static public MeshData MakeHoseMesh(Vec3f pos1, Vec3f pos2, Vec3f swayDir, float swayAmount)
            => MakeHoseMesh(pos1, pos2, null, null, swayDir, swayAmount);

        /// <summary>
        /// Builds a hanging hose with optional valve lead-ins. A lead-in leaves a valve along its
        /// mounting axis, then turns diagonally toward the hanging middle. Texture coordinates are
        /// based on distance travelled along the resulting path, rather than its end-to-end chord.
        /// </summary>
        static public MeshData MakeHoseMesh(Vec3f pos1, Vec3f pos2, Vec3f p1ExitDir, Vec3f p2ExitDir, Vec3f swayDir, float swayAmount)
        {
            float t = Thickness;
            float dist = pos2.DistanceTo(pos1);
            Vec3f chord = dist > 1e-6f ? (pos2 - pos1) * (1f / dist) : new Vec3f(0, 0, 1);

            // Lead-in segments are short enough to read as the hose leaving the valve, not as a
            // rigid pipe. Their count grows with the turn needed to meet the hanging middle.
            const float LeadSegmentLength = 0.06f;
            bool useP1Lead = p1ExitDir != null && dist > 0.9f;
            bool useP2Lead = p2ExitDir != null && dist > 0.9f;

            List<Vec3f> p1Lead = useP1Lead ? BuildValveLead(pos1, p1ExitDir, chord, LeadSegmentLength) : null;
            List<Vec3f> p2Lead = useP2Lead ? BuildValveLead(pos2, p2ExitDir, chord * -1f, LeadSegmentLength) : null;
            Vec3f middleStart = p1Lead != null ? p1Lead[p1Lead.Count - 1] : pos1;
            Vec3f middleEnd = p2Lead != null ? p2Lead[p2Lead.Count - 1] : pos2;

            float middleDist = middleEnd.DistanceTo(middleStart);
            int nSec = (int)Math.Floor(middleDist * 2);
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

            // Preferred seam axis. It is projected onto the plane perpendicular to each local
            // segment below: at a sharp valve bend it can otherwise become parallel to the first
            // segment and collapse the hose's square cross-section into a flat line.
            Vec3f preferredB = new Vec3f(-chord.Z, 0, chord.X).Normalize();
            if (chord.Z == 0 && chord.X == 0)
            {
                preferredB = new Vec3f(1, 0, 0);
            }

            mesh_top.Flags.Fill(0);
            mesh_bot.Flags.Fill(0);
            mesh_side.Flags.Fill(0);
            mesh_side2.Flags.Fill(0);

            List<Vec3f> positions = new List<Vec3f>();
            List<float> dyArr = new List<float>();
            if (p1Lead != null)
            {
                // The final lead point is also the first catenary sample below.
                for (int j = 0; j < p1Lead.Count - 1; j++) AddFixedPoint(positions, dyArr, p1Lead[j]);
            }

            float minDy = 0f; // deepest (most negative) sag, for weighting the sway
            for (int j = 0; j <= nSec; j++)
            {
                Vec3f dPos = middleEnd - middleStart;
                float x = dPos.X / nSec * j;
                float y = dPos.Y / nSec * j;
                float z = dPos.Z / nSec * j;
                float l = (float)Math.Sqrt(x * x + y * y + z * z);
                float dy = middleDist > 1e-6f ? Catenary(l / middleDist, 1, CatenaryA) : 0f;
                dyArr.Add(dy);
                if (dy < minDy) minDy = dy;
                positions.Add(middleStart + new Vec3f(x, y + dy, z));
            }

            if (p2Lead != null)
            {
                // The last catenary sample is the outermost point of this lead. Walk its remaining
                // points back toward the valve so the whole trajectory stays continuous.
                for (int j = p2Lead.Count - 2; j >= 0; j--) AddFixedPoint(positions, dyArr, p2Lead[j]);
            }

            int pointCount = positions.Count;
            // Let the hose enter its attachment holes by half its thickness. This matters most
            // for very short valve-to-valve connections, where an exactly flush endpoint reads
            // as a flattened cap instead of a hose disappearing into the anchor.
            if (pointCount > 1)
            {
                Vec3f firstDirection = (positions[1] - positions[0]).Normalize();
                Vec3f lastDirection = (positions[pointCount - 1] - positions[pointCount - 2]).Normalize();
                positions[0] -= firstDirection * (t * 0.5f);
                positions[pointCount - 1] += lastDirection * (t * 0.5f);
            }

            float[] pathLength = new float[pointCount];
            for (int j = 1; j < pointCount; j++) pathLength[j] = pathLength[j - 1] + positions[j].DistanceTo(positions[j - 1]);

            // Sway the hanging part horizontally, weighted by sag depth (ends fixed, middle most).
            if (swayDir != null && swayAmount != 0f && minDy < 0f)
            {
                for (int j = 0; j < pointCount; j++)
                {
                    float w = dyArr[j] / minDy; // 0 at the anchors, 1 at the deepest point
                    positions[j].X += swayDir.X * swayAmount * w;
                    positions[j].Z += swayDir.Z * swayAmount * w;
                }
            }

            Vec3f pos, pos_next, pos_before, direction, a, b;

            for (int j = 0; j < pointCount; j++)
            {
                pos = positions[j];
                pos_next = j < pointCount - 1 ? positions[j + 1] : positions[j];
                pos_before = j > 0 ? positions[j - 1] : positions[j];
                direction = (pos_next - pos_before).Normalize();

                b = GetCrossSectionAxis(direction, preferredB);
                a = CrossProduct(direction, b * -1);

                // 2× makes the texture tile twice as often along the length → half the lengthwise
                // stretch (the leather pattern reads finer instead of being pulled long).
                float u = pathLength[j] * 2f;
                int color = 1;
                float uv_v = 3f / 16;

                // The leather texture has a horizontal seam across its middle. ONE side face
                // (mesh_side) straddles that middle, so the seam runs lengthwise along the hose.
                // Every other face samples the seam-free top band (0..uv_v) — plain brown.
                const float SeamCenter = 0.5f; // seam is in the middle of the texture
                float seamLo = SeamCenter - uv_v * 0.5f;
                float seamHi = SeamCenter + uv_v * 0.5f;

                mesh_top.AddVertex((pos - b * t + a * t).X, (pos - b * t + a * t).Y, (pos - b * t + a * t).Z, u, 0, color);
                mesh_top.AddVertex((pos + b * t + a * t).X, (pos + b * t + a * t).Y, (pos + b * t + a * t).Z, u, uv_v, color);

                mesh_bot.AddVertex((pos - b * t - a * t).X, (pos - b * t - a * t).Y, (pos - b * t - a * t).Z, u, 0, color);
                mesh_bot.AddVertex((pos + b * t - a * t).X, (pos + b * t - a * t).Y, (pos + b * t - a * t).Z, u, uv_v, color);

                mesh_side.AddVertex((pos - b * t + a * t).X, (pos - b * t + a * t).Y, (pos - b * t + a * t).Z, u, seamHi, color);
                mesh_side.AddVertex((pos - b * t - a * t).X, (pos - b * t - a * t).Y, (pos - b * t - a * t).Z, u, seamLo, color);

                mesh_side2.AddVertex((pos + b * t + a * t).X, (pos + b * t + a * t).Y, (pos + b * t + a * t).Z, u, uv_v, color);
                mesh_side2.AddVertex((pos + b * t - a * t).X, (pos + b * t - a * t).Y, (pos + b * t - a * t).Z, u, 0, color);

                mesh_top.Flags[2 * j] = VertexFlags.PackNormal(new Vec3f(0, 1, 0));
                mesh_top.Flags[2 * j + 1] = VertexFlags.PackNormal(new Vec3f(0, 1, 0));
                mesh_bot.Flags[2 * j] = VertexFlags.PackNormal(new Vec3f(0, -1, 0));
                mesh_bot.Flags[2 * j + 1] = VertexFlags.PackNormal(new Vec3f(0, -1, 0));
                mesh_side.Flags[2 * j] = VertexFlags.PackNormal(-b.X, -b.Y, -b.Z);
                mesh_side.Flags[2 * j + 1] = VertexFlags.PackNormal(-b.X, -b.Y, -b.Z);
                mesh_side2.Flags[2 * j] = VertexFlags.PackNormal(b);
                mesh_side2.Flags[2 * j + 1] = VertexFlags.PackNormal(b);
            }

            for (int j = 0; j < pointCount - 1; j++)
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

        private static Vec3f GetCrossSectionAxis(Vec3f direction, Vec3f preferredAxis)
        {
            float projection = direction.X * preferredAxis.X + direction.Y * preferredAxis.Y + direction.Z * preferredAxis.Z;
            Vec3f perpendicular = preferredAxis - direction * projection;
            float lengthSquared = perpendicular.X * perpendicular.X + perpendicular.Y * perpendicular.Y + perpendicular.Z * perpendicular.Z;
            if (lengthSquared > 1e-6f) return perpendicular * (1f / (float)Math.Sqrt(lengthSquared));

            // The preferred seam axis is parallel to this segment. Pick a world axis that isn't.
            Vec3f fallback = Math.Abs(direction.Y) < 0.9f ? new Vec3f(0, 1, 0) : new Vec3f(1, 0, 0);
            return CrossProduct(direction, fallback);
        }

        private static List<Vec3f> BuildValveLead(Vec3f start, Vec3f exitDir, Vec3f targetDir, float segmentLength)
        {
            float dot = exitDir.X * targetDir.X + exitDir.Y * targetDir.Y + exitDir.Z * targetDir.Z;
            dot = Math.Max(-1f, Math.Min(1f, dot));
            float angle = (float)(Math.Acos(dot) * 180.0 / Math.PI);
            int segments = angle < 45f ? 1 : angle < 90f ? 2 : 3;

            List<Vec3f> points = new List<Vec3f> { start };
            Vec3f point = start;
            for (int segment = 0; segment < segments; segment++)
            {
                // The first segment leaves straight along the valve axis. Later segments turn an
                // equal portion toward the hose direction; the catenary completes the final turn.
                float turn = (float)segment / segments;
                Vec3f direction = (exitDir * (1f - turn) + targetDir * turn).Normalize();
                point += direction * segmentLength;
                points.Add(point);
            }

            return points;
        }

        private static void AddFixedPoint(List<Vec3f> positions, List<float> dyArr, Vec3f point)
        {
            positions.Add(point);
            dyArr.Add(0f);
        }
    }
}
