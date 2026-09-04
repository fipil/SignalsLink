using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using signals.src.signalNetwork;

namespace SignalsLink.src.signals.hose
{
    /// <summary>
    /// Renders the hose connections — one static mesh per hose (uploaded once, redrawn each frame
    /// with no CPU work). When liquid pulses through a hose the valve triggers a short "wobble":
    /// the hanging part sways horizontally along the hose axis (a damped sine), like a garden hose
    /// with water surging through it. Only a capped number of hoses (<see cref="MaxWobblers"/>) can
    /// wobble at once and only those recompute their mesh per frame, so the effect is cheap
    /// regardless of how many hoses exist.
    /// </summary>
    public class HangingHosesRenderer : IRenderer
    {
        public double RenderOrder => 0.5;
        public int RenderRange => 100;

        // Wobble tuning.
        const float WobbleDuration = 1.2f;   // seconds until it settles back to rest
        const float WobblePeriod = 0.6f;     // seconds per full swing (target ↔ source)
        const float WobbleAmplitude = 0.12f; // block units at the deepest point
        const int MaxWobblers = 8;           // how many hoses may wobble simultaneously

        class HoseRender
        {
            public HoseConnection con;
            public Vec3d origin;     // world position of anchor 1 (mesh is built relative to it)
            public Vec3f p2local;    // anchor 2 relative to anchor 1
            public Vec3f swayDir;    // unit horizontal direction along the hose axis
            public MeshRef meshRef;
            public float wobbleT = -1f; // < 0 = at rest
        }

        readonly HoseNetworkMod mod;
        readonly ICoreClientAPI capi;
        readonly int chunksize;

        readonly Dictionary<HoseConnection, HoseRender> hoses = new Dictionary<HoseConnection, HoseRender>();
        readonly List<HoseRender> wobblers = new List<HoseRender>();
        bool dirty = true;

        int textureId = -1;
        readonly AssetLocation hoseTexName = new AssetLocation("signalslink:block/leather.png");
        readonly Matrixf ModelMat = new Matrixf();

        public HangingHosesRenderer(ICoreClientAPI capi, HoseNetworkMod mod)
        {
            this.capi = capi;
            this.mod = mod;
            this.chunksize = GlobalConstants.ChunkSize;
            capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "signalslinkhoses");
        }

        public void RequestFullRebuild() => dirty = true;

        public void RequestIncrementalRebuild(HoseNetworkData data) => dirty = true;

        /// <summary>
        /// Starts (or refreshes) the wobble on the hose segment attached to the given anchor.
        /// Called by a valve when liquid audibly pulses through its hose. Respects the wobble cap:
        /// beyond it, extra pulses are simply ignored (the hose doesn't wobble this time).
        /// </summary>
        /// <param name="other">
        /// Anchor at the far end of the segment that is actually flowing. A valve may have several
        /// hoses on one anchor, and only the one being pumped through should wobble. Null wobbles
        /// every hose on the anchor (the old behaviour, used when the flowing line is unknown).
        /// </param>
        public void TriggerWobble(NodePos anchor, NodePos other = null)
        {
            if (anchor == null || hoses.Count == 0) return;

            foreach (HoseRender h in hoses.Values)
            {
                if (h.con.pos1 != anchor && h.con.pos2 != anchor) continue;
                if (other != null && h.con.pos1 != other && h.con.pos2 != other) continue;

                if (h.wobbleT >= 0f) { h.wobbleT = 0f; continue; } // already wobbling → re-kick
                if (wobblers.Count >= MaxWobblers) continue;       // over budget → skip this one

                h.wobbleT = 0f;
                wobblers.Add(h);
            }
        }

        public void OnClientTick(float dt)
        {
            if (!dirty) return;
            dirty = false;
            Rebuild();
        }

        void Rebuild()
        {
            IBlockAccessor accessor = capi?.World?.BlockAccessor;
            if (accessor == null || mod?.data == null) return;

            DisposeMeshes();

            foreach (HoseConnection con in mod.data.connections)
            {
                IHoseAnchor a1 = accessor.GetBlock(con.pos1.blockPos) as IHoseAnchor;
                IHoseAnchor a2 = accessor.GetBlock(con.pos2.blockPos) as IHoseAnchor;
                if (a1 == null || a2 == null) continue;

                Vec3f a1p = a1.GetHoseAnchorPosInBlock(con.pos1);
                Vec3f a2p = a2.GetHoseAnchorPosInBlock(con.pos2);

                BlockPos b1 = con.pos1.blockPos;
                BlockPos b2 = con.pos2.blockPos;

                Vec3d origin = new Vec3d(b1.X + a1p.X, b1.Y + a1p.Y, b1.Z + a1p.Z);
                Vec3f p2local = new Vec3f(
                    (b2.X - b1.X) + (a2p.X - a1p.X),
                    (b2.Y - b1.Y) + (a2p.Y - a1p.Y),
                    (b2.Z - b1.Z) + (a2p.Z - a1p.Z));

                Vec3f swayDir = new Vec3f(p2local.X, 0, p2local.Z);
                if (swayDir.X == 0 && swayDir.Z == 0) swayDir = new Vec3f(1, 0, 0);
                else swayDir.Normalize();

                MeshData m = HoseMesh.MakeHoseMesh(new Vec3f(0, 0, 0), p2local);
                m.SetMode(EnumDrawMode.Triangles);

                hoses[con] = new HoseRender
                {
                    con = con,
                    origin = origin,
                    p2local = p2local,
                    swayDir = swayDir,
                    meshRef = capi.Render.UploadMesh(m)
                };
            }
        }

        void UpdateHoseMesh(HoseRender h, float swayAmount)
        {
            MeshData m = HoseMesh.MakeHoseMesh(new Vec3f(0, 0, 0), h.p2local, h.swayDir, swayAmount);
            m.SetMode(EnumDrawMode.Triangles);
            capi.Render.UpdateMesh(h.meshRef, m);
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (stage != EnumRenderStage.Opaque || hoses.Count == 0) return;

            // Advance the (few) wobbling hoses; only these recompute their mesh.
            for (int i = wobblers.Count - 1; i >= 0; i--)
            {
                HoseRender h = wobblers[i];
                h.wobbleT += deltaTime;
                if (h.wobbleT >= WobbleDuration)
                {
                    h.wobbleT = -1f;
                    UpdateHoseMesh(h, 0f); // settle back to rest
                    wobblers.RemoveAt(i);
                }
                else
                {
                    float amt = WobbleAmplitude
                        * (float)Math.Sin(h.wobbleT / WobblePeriod * Math.PI * 2.0)
                        * (1f - h.wobbleT / WobbleDuration); // damping
                    UpdateHoseMesh(h, amt);
                }
            }

            IRenderAPI rpi = capi.Render;
            Vec3d camPos = capi.World.Player.Entity.CameraPos;

            rpi.GLEnableDepthTest();
            rpi.GlEnableCullFace();

            IStandardShaderProgram prog = rpi.PreparedStandardShader(0, 0, 0);
            prog.Use();

            if (textureId < 0) textureId = capi.Render.GetOrLoadTexture(hoseTexName);
            rpi.BindTexture2d(textureId);

            prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;
            prog.ViewMatrix = rpi.CameraMatrixOriginf;

            float maxRenderDistance = RenderRange + chunksize;
            float maxRenderDistanceSq = maxRenderDistance * maxRenderDistance;

            foreach (HoseRender h in hoses.Values)
            {
                double cx = h.origin.X - camPos.X;
                double cy = h.origin.Y - camPos.Y;
                double cz = h.origin.Z - camPos.Z;
                if (cx * cx + cy * cy + cz * cz > maxRenderDistanceSq) continue;

                prog.ModelMatrix = ModelMat.Identity().Translate(cx, cy, cz).Values;
                rpi.RenderMesh(h.meshRef);
            }

            prog.Stop();
        }

        void DisposeMeshes()
        {
            foreach (HoseRender h in hoses.Values) h.meshRef?.Dispose();
            hoses.Clear();
            wobblers.Clear();
        }

        public void Dispose()
        {
            capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
            DisposeMeshes();
        }
    }
}
