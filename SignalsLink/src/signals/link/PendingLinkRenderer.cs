using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.link
{
    /// <summary>
    /// Renders the line being placed (from the pending anchor to the camera). Mirror of the
    /// Signals <c>PendingWireRenderer</c>.
    /// </summary>
    public class PendingLinkRenderer : IRenderer
    {
        public double RenderOrder => 0.5;
        public int RenderRange => 100;

        readonly PlacingLinksMod mod;
        readonly ICoreClientAPI capi;
        readonly BlockPos blockPos;
        readonly Vec3f posOffset;
        readonly LinkProfile profile;

        MeshRef linkMesh;
        int textureId = -1;
        Matrixf ModelMat = new Matrixf();

        public PendingLinkRenderer(ICoreClientAPI capi, PlacingLinksMod mod, BlockPos pos, Vec3f offset, byte kind)
        {
            this.capi = capi;
            this.mod = mod;
            this.blockPos = pos;
            this.posOffset = offset;
            this.profile = LinkProfile.For(kind);
            capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "signalslinkpendinglink");
        }

        public void Dispose()
        {
            capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
            linkMesh?.Dispose();
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (stage != EnumRenderStage.Opaque) return;

            IRenderAPI rpi = capi.Render;
            Vec3d camPos = capi.World.Player.Entity.CameraPos;

            if (textureId < 0) textureId = capi.Render.GetOrLoadTexture(profile.Texture);
            rpi.BindTexture2d(textureId);

            IStandardShaderProgram prog = rpi.PreparedStandardShader(0, 0, 0);
            prog.Use();
            prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;
            prog.ViewMatrix = rpi.CameraMatrixOriginf;

            Vec3d offset = blockPos.ToVec3d();
            // Rebuilds the mesh every frame (not ideal, but matches the Signals pending renderer).
            MeshData mesh = LinkMesh.MakeLinkMesh(posOffset, camPos.SubCopy(offset).ToVec3f(), profile);
            mesh.SetMode(EnumDrawMode.Triangles);
            linkMesh?.Dispose();
            linkMesh = capi.Render.UploadMesh(mesh);

            ModelMat = ModelMat.Identity().Translate(offset.X - camPos.X, offset.Y - camPos.Y, offset.Z - camPos.Z);
            prog.ModelMatrix = ModelMat.Values;
            rpi.RenderMesh(linkMesh);
            prog.Stop();
        }
    }
}
