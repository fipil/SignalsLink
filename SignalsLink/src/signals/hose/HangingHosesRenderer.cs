using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.hose
{
    /// <summary>
    /// Renders the hose connections. Mirror of the Signals <c>HangingWiresRenderer</c>, but
    /// simplified: meshes are grouped per chunk (to keep vertex coordinates small) and fully
    /// rebuilt whenever the data changes. Hose counts are modest, so this is cheap enough.
    /// </summary>
    public class HangingHosesRenderer : IRenderer
    {
        public double RenderOrder => 0.5;
        public int RenderRange => 100;

        readonly HoseNetworkMod mod;
        readonly ICoreClientAPI capi;
        readonly int chunksize;

        readonly Dictionary<Vec3i, MeshRef> meshRefs = new Dictionary<Vec3i, MeshRef>();
        bool dirty = true;

        int textureId = -1;
        readonly AssetLocation hoseTexName = new AssetLocation("signalslink:block/oak-dark.png");
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

        Vec3i GetChunkPos(BlockPos pos)
        {
            return new Vec3i(
                (int)Math.Floor((double)pos.X / chunksize),
                (int)Math.Floor((double)pos.Y / chunksize),
                (int)Math.Floor((double)pos.Z / chunksize));
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

            Dictionary<Vec3i, MeshData> perChunk = new Dictionary<Vec3i, MeshData>();

            foreach (HoseConnection con in mod.data.connections)
            {
                IHoseAnchor a1 = accessor.GetBlock(con.pos1.blockPos) as IHoseAnchor;
                IHoseAnchor a2 = accessor.GetBlock(con.pos2.blockPos) as IHoseAnchor;
                if (a1 == null || a2 == null) continue;

                Vec3i chunk = GetChunkPos(con.pos1.blockPos);

                Vec3f p1 = con.pos1.blockPos.ToVec3f()
                    .AddCopy(-chunk.X * chunksize, -chunk.Y * chunksize, -chunk.Z * chunksize)
                    + a1.GetHoseAnchorPosInBlock(con.pos1);
                Vec3f p2 = con.pos2.blockPos.ToVec3f()
                    .AddCopy(-chunk.X * chunksize, -chunk.Y * chunksize, -chunk.Z * chunksize)
                    + a2.GetHoseAnchorPosInBlock(con.pos2);

                MeshData m = HoseMesh.MakeHoseMesh(p1, p2);
                if (perChunk.TryGetValue(chunk, out MeshData existing)) existing.AddMeshData(m);
                else perChunk[chunk] = m;
            }

            foreach (KeyValuePair<Vec3i, MeshData> kv in perChunk)
            {
                kv.Value.SetMode(EnumDrawMode.Triangles);
                meshRefs[kv.Key] = capi.Render.UploadMesh(kv.Value);
            }
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (stage != EnumRenderStage.Opaque || meshRefs.Count == 0) return;

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

            foreach (KeyValuePair<Vec3i, MeshRef> mesh in meshRefs)
            {
                double ox = mesh.Key.X * chunksize;
                double oy = mesh.Key.Y * chunksize;
                double oz = mesh.Key.Z * chunksize;
                double cx = ox + chunksize * 0.5 - camPos.X;
                double cy = oy + chunksize * 0.5 - camPos.Y;
                double cz = oz + chunksize * 0.5 - camPos.Z;
                if (cx * cx + cy * cy + cz * cz > maxRenderDistanceSq) continue;

                prog.ModelMatrix = ModelMat.Identity().Translate(ox - camPos.X, oy - camPos.Y, oz - camPos.Z).Values;
                rpi.RenderMesh(mesh.Value);
            }

            prog.Stop();
        }

        void DisposeMeshes()
        {
            foreach (MeshRef mr in meshRefs.Values) mr?.Dispose();
            meshRefs.Clear();
        }

        public void Dispose()
        {
            capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
            DisposeMeshes();
        }
    }
}
