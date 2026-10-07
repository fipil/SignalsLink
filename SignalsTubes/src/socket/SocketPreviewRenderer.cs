using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace SignalsTubes.src.socket;

/// <summary>Translucent picture of what a click would put into the socket: its pins and the tube, already turned.</summary>
public sealed class SocketPreviewRenderer : IRenderer
{
    public double RenderOrder => .5;
    public int RenderRange => 32;

    private readonly ICoreClientAPI capi;
    private readonly BlockPos pos;
    private readonly Matrixf model = new();
    private MultiTextureMeshRef mesh;   // block textures may sit on several atlas pages; a plain mesh ref binds only the first

    public SocketPreviewRenderer(ICoreClientAPI capi, BlockPos pos)
    {
        this.capi = capi; this.pos = pos;
        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "signalstubes-socket-preview");
    }

    public void SetMesh(MeshData data)
    {
        mesh?.Dispose();
        mesh = data == null ? null : capi.Render.UploadMultiTextureMesh(data);
    }

    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        if (mesh == null) return;
        var render = capi.Render;
        var camera = capi.World.Player.Entity.CameraPos;
        render.GlDisableCullFace();
        render.GlToggleBlend(true);
        var prog = render.PreparedStandardShader(pos.X, pos.Y, pos.Z);
        prog.ViewMatrix = render.CameraMatrixOriginf;
        prog.ProjectionMatrix = render.CurrentProjectionMatrix;
        prog.ModelMatrix = model.Identity().Translate(pos.X - camera.X, pos.Y - camera.Y, pos.Z - camera.Z).Values;
        prog.RgbaTint = new Vec4f(1, 1, 1, .75f);   // dense enough for the cap colours to read
        // depth writes stay on: without them the pins' sides blend over their own cap tops and the colours drown
        render.RenderMultiTextureMesh(mesh, "tex");
        prog.RgbaTint = new Vec4f(1, 1, 1, 1);
        prog.Stop();
    }

    public void Dispose()
    {
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        mesh?.Dispose();
        mesh = null;
    }
}
