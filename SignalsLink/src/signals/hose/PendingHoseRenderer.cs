using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.hose
{
    /// <summary>
    /// Renders the hose being placed (from the pending anchor to the camera). Mirror of the
    /// Signals <c>PendingWireRenderer</c>.
    /// </summary>
    public class PendingHoseRenderer : IRenderer
    {
        public double RenderOrder => 0.5;
        public int RenderRange => 100;

        readonly PlacingHosesMod mod;
        readonly ICoreClientAPI capi;
        readonly BlockPos blockPos;
        readonly Vec3f posOffset;

        MeshRef hoseMesh;
        readonly AssetLocation hoseTexName = new AssetLocation("signalslink:block/leather.png");
        int textureId = -1;
        Matrixf ModelMat = new Matrixf();

        public PendingHoseRenderer(ICoreClientAPI capi, PlacingHosesMod mod, BlockPos pos, Vec3f offset)
        {
            this.capi = capi;
            this.mod = mod;
            this.blockPos = pos;
            this.posOffset = offset;
            capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "signalslinkpendinghose");
        }

        public void Dispose()
        {
            capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
            hoseMesh?.Dispose();
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (stage != EnumRenderStage.Opaque) return;

            IRenderAPI rpi = capi.Render;
            Vec3d camPos = capi.World.Player.Entity.CameraPos;

            if (textureId < 0) textureId = capi.Render.GetOrLoadTexture(hoseTexName);
            rpi.BindTexture2d(textureId);

            IStandardShaderProgram prog = rpi.PreparedStandardShader(0, 0, 0);
            prog.Use();
            prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;
            prog.ViewMatrix = rpi.CameraMatrixOriginf;

            Vec3d offset = blockPos.ToVec3d();
            // Rebuilds the mesh every frame (not ideal, but matches the Signals pending renderer).
            MeshData mesh = HoseMesh.MakeHoseMesh(posOffset, camPos.SubCopy(offset).ToVec3f());
            mesh.SetMode(EnumDrawMode.Triangles);
            hoseMesh?.Dispose();
            hoseMesh = capi.Render.UploadMesh(mesh);

            ModelMat = ModelMat.Identity().Translate(offset.X - camPos.X, offset.Y - camPos.Y, offset.Z - camPos.Z);
            prog.ModelMatrix = ModelMat.Values;
            rpi.RenderMesh(hoseMesh);
            prog.Stop();
        }
    }
}
