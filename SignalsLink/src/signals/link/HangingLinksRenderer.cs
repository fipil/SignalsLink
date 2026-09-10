using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using signals.src.signalNetwork;

namespace SignalsLink.src.signals.link
{
    /// <summary>
    /// Renders the link connections — one static mesh per line (uploaded once, redrawn each frame
    /// with no CPU work). When liquid pulses through a hose the valve triggers a short "wobble":
    /// the hanging part sways horizontally along the line axis (a damped sine), like a garden hose
    /// with water surging through it. Only a capped number of links (<see cref="MaxWobblers"/>) can
    /// wobble at once and only those recompute their mesh per frame, so the effect is cheap
    /// regardless of how many links exist.
    /// </summary>
    public class HangingLinksRenderer : IRenderer
    {
        public double RenderOrder => 0.5;
        public int RenderRange => 100;

        // Wobble tuning.
        const float WobbleDuration = 1.2f;   // seconds until it settles back to rest
        const float WobblePeriod = 0.6f;     // seconds per full swing (target ↔ source)
        const float WobbleAmplitude = 0.12f; // block units at the deepest point
        const int MaxWobblers = 8;           // how many links may wobble simultaneously

        class LinkRender
        {
            public LinkConnection con;
            public Vec3d origin;     // world position of anchor 1 (mesh is built relative to it)
            public Vec3f p2local;    // anchor 2 relative to anchor 1
            public Vec3f p1ExitDir;  // outward valve axis at anchor 1, if it is a valve
            public Vec3f p2ExitDir;  // outward valve axis at anchor 2, if it is a valve
            public Vec3f swayDir;    // unit horizontal direction along the line axis
            public LinkProfile profile;
            public MeshRef meshRef;
            public float wobbleT = -1f; // < 0 = at rest
        }

        readonly LinkNetworkMod mod;
        readonly ICoreClientAPI capi;
        readonly int chunksize;

        readonly Dictionary<LinkConnection, LinkRender> links = new Dictionary<LinkConnection, LinkRender>();
        readonly List<LinkRender> wobblers = new List<LinkRender>();
        bool dirty = true;

        // One texture per kind, bound once per group when rendering.
        readonly int[] textureIds = new int[LinkKind.Count];
        readonly Matrixf ModelMat = new Matrixf();

        public HangingLinksRenderer(ICoreClientAPI capi, LinkNetworkMod mod)
        {
            this.capi = capi;
            this.mod = mod;
            this.chunksize = GlobalConstants.ChunkSize;
            capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "signalslinklinks");
        }

        public void RequestFullRebuild() => dirty = true;

        public void RequestIncrementalRebuild(LinkNetworkData data) => dirty = true;

        /// <summary>
        /// Starts (or refreshes) the wobble on the segment attached to the given anchor.
        /// Called by a valve when liquid audibly pulses through its hose. Respects the wobble cap:
        /// beyond it, extra pulses are simply ignored (the line doesn't wobble this time).
        /// </summary>
        /// <param name="other">
        /// Anchor at the far end of the segment that is actually flowing. A valve may have several
        /// links on one anchor, and only the one being pumped through should wobble. Null wobbles
        /// every line on the anchor (the old behaviour, used when the flowing line is unknown).
        /// </param>
        public void TriggerWobble(NodePos anchor, NodePos other = null)
        {
            if (anchor == null || links.Count == 0) return;

            foreach (LinkRender h in links.Values)
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

            foreach (LinkConnection con in mod.data.connections)
            {
                ILinkAnchor a1 = accessor.GetBlock(con.pos1.blockPos) as ILinkAnchor;
                ILinkAnchor a2 = accessor.GetBlock(con.pos2.blockPos) as ILinkAnchor;
                if (a1 == null || a2 == null) continue;

                Vec3f a1p = a1.GetLinkAnchorPosInBlock(con.pos1);
                Vec3f a2p = a2.GetLinkAnchorPosInBlock(con.pos2);

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

                Vec3f p1ExitDir = GetAnchorExitDirection(a1, con.pos1);
                Vec3f p2ExitDir = GetAnchorExitDirection(a2, con.pos2);
                LinkProfile profile = LinkProfile.For(con.kind);
                MeshData m = LinkMesh.MakeLinkMesh(new Vec3f(0, 0, 0), p2local, p1ExitDir, p2ExitDir, profile);
                m.SetMode(EnumDrawMode.Triangles);

                links[con] = new LinkRender
                {
                    con = con,
                    origin = origin,
                    p2local = p2local,
                    p1ExitDir = p1ExitDir,
                    p2ExitDir = p2ExitDir,
                    swayDir = swayDir,
                    profile = profile,
                    meshRef = capi.Render.UploadMesh(m)
                };
            }
        }

        private static Vec3f GetAnchorExitDirection(ILinkAnchor owner, NodePos anchor)
        {
            if (owner is BlockLinkEndpointBase endpoint)
            {
                string sideCode = endpoint.Variant?["side"];
                BlockFacing hostFace = sideCode != null ? BlockFacing.FromCode(sideCode) : null;
                if (hostFace == BlockFacing.DOWN && endpoint.FloorMountExitsSideways)
                {
                    string orientationCode = endpoint.Variant?["orientation"];
                    BlockFacing orientation = orientationCode != null ? BlockFacing.FromCode(orientationCode) : null;
                    if (orientation != null)
                    {
                        Vec3i outward = orientation.Opposite.Normali;
                        return new Vec3f(outward.X, outward.Y, outward.Z);
                    }
                }
                if (hostFace != null)
                {
                    Vec3i outward = hostFace.Opposite.Normali;
                    return new Vec3f(outward.X, outward.Y, outward.Z);
                }
            }

            Vec3f position = owner.GetLinkAnchorPosInBlock(anchor);
            float offsetX = position.X - 0.5f;
            float offsetY = position.Y - 0.5f;
            float offsetZ = position.Z - 0.5f;
            float absX = Math.Abs(offsetX);
            float absY = Math.Abs(offsetY);
            float absZ = Math.Abs(offsetZ);

            if (absX >= absY && absX >= absZ) return new Vec3f(offsetX >= 0f ? 1f : -1f, 0f, 0f);
            if (absY >= absZ) return new Vec3f(0f, offsetY >= 0f ? 1f : -1f, 0f);
            return new Vec3f(0f, 0f, offsetZ >= 0f ? 1f : -1f);
        }

        void UpdateLinkMesh(LinkRender h, float swayAmount)
        {
            MeshData m = LinkMesh.MakeLinkMesh(new Vec3f(0, 0, 0), h.p2local, h.p1ExitDir, h.p2ExitDir, h.swayDir, swayAmount, h.profile);
            m.SetMode(EnumDrawMode.Triangles);
            capi.Render.UpdateMesh(h.meshRef, m);
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (stage != EnumRenderStage.Opaque || links.Count == 0) return;

            // Advance the (few) wobbling links; only these recompute their mesh.
            for (int i = wobblers.Count - 1; i >= 0; i--)
            {
                LinkRender h = wobblers[i];
                h.wobbleT += deltaTime;
                if (h.wobbleT >= WobbleDuration)
                {
                    h.wobbleT = -1f;
                    UpdateLinkMesh(h, 0f); // settle back to rest
                    wobblers.RemoveAt(i);
                }
                else
                {
                    float amt = WobbleAmplitude
                        * (float)Math.Sin(h.wobbleT / WobblePeriod * Math.PI * 2.0)
                        * (1f - h.wobbleT / WobbleDuration); // damping
                    UpdateLinkMesh(h, amt);
                }
            }

            IRenderAPI rpi = capi.Render;
            Vec3d camPos = capi.World.Player.Entity.CameraPos;

            rpi.GLEnableDepthTest();
            rpi.GlEnableCullFace();

            IStandardShaderProgram prog = rpi.PreparedStandardShader(0, 0, 0);
            prog.Use();

            prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;
            prog.ViewMatrix = rpi.CameraMatrixOriginf;

            float maxRenderDistance = RenderRange + chunksize;
            float maxRenderDistanceSq = maxRenderDistance * maxRenderDistance;

            // Grouped by kind: each texture is bound once per frame rather than per line.
            for (byte kind = 0; kind < LinkKind.Count; kind++)
            {
                bool bound = false;

                foreach (LinkRender h in links.Values)
                {
                    if (h.con.kind != kind) continue;

                    double cx = h.origin.X - camPos.X;
                    double cy = h.origin.Y - camPos.Y;
                    double cz = h.origin.Z - camPos.Z;
                    if (cx * cx + cy * cy + cz * cz > maxRenderDistanceSq) continue;

                    if (!bound)
                    {
                        if (textureIds[kind] <= 0) textureIds[kind] = capi.Render.GetOrLoadTexture(LinkProfile.For(kind).Texture);
                        rpi.BindTexture2d(textureIds[kind]);
                        bound = true;
                    }

                    prog.ModelMatrix = ModelMat.Identity().Translate(cx, cy, cz).Values;
                    rpi.RenderMesh(h.meshRef);
                }
            }

            prog.Stop();
        }

        void DisposeMeshes()
        {
            foreach (LinkRender h in links.Values) h.meshRef?.Dispose();
            links.Clear();
            wobblers.Clear();
        }

        public void Dispose()
        {
            capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
            DisposeMeshes();
        }
    }
}
