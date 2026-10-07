using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace SignalsTubes.src.socket;

/// <summary>
/// Translucent picture of what a click would put into the socket: its pins and the tube, already turned, and a
/// name tag per pin lying flat on the plate's plane right outside its pin, pointing away from the socket. The tags
/// are part of the world (perspective, distance), so which pin a tag belongs to is never in doubt.
/// </summary>
public sealed class SocketPreviewRenderer : IRenderer
{
    public double RenderOrder => .5;
    public int RenderRange => 32;

    private const float TagWidth = .14f;      // across the strip, in blocks (a pin is 2/16 wide)
    private const float TagGap = .125f / 16;  // between the socket's edge and its tags
    private const float TagLift = 1 / 16f + .003f;   // just above the plate

    private readonly ICoreClientAPI capi;
    private readonly BlockPos pos;
    private readonly Matrixf model = new(), turn = new();
    private MultiTextureMeshRef mesh;   // block textures may sit on several atlas pages; a plain mesh ref binds only the first
    private MeshRef quad;               // a unit square in the XZ plane, text running +x, text top at -z
    private Vec3f rotation = new();     // the previewed variant's shape rotation
    private readonly List<(int pin, LoadedTexture tex)> labels = new();

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

    /// <summary>The pins' name tags, for the socket variant with this shape rotation.</summary>
    public void SetLabels(Vec3f variantRotation, IEnumerable<(int pin, string text)> items)
    {
        ClearLabels();
        rotation = variantRotation;
        var font = new CairoFont(18, GuiStyle.StandardFontName, ColorUtil.WhiteArgbDouble);
        foreach (var (pin, text) in items)
        {
            var bg = new TextBackground { FillColor = GuiStyle.DialogLightBgColor, Padding = 3, Radius = GuiStyle.ElementBGRadius };
            labels.Add((pin, capi.Gui.TextTexture.GenUnscaledTextTexture(text, font, bg)));
        }
        quad ??= capi.Render.UploadMesh(UnitQuad());
    }

    private static MeshData UnitQuad()
    {
        var m = new MeshData(4, 12);
        m.AddVertex(0, 0, 0, 0, 0, -1); m.AddVertex(1, 0, 0, 1, 0, -1); m.AddVertex(1, 0, 1, 1, 1, -1); m.AddVertex(0, 0, 1, 0, 1, -1);
        foreach (int i in new[] { 0, 1, 2, 0, 2, 3 }) m.AddIndex(i);
        foreach (int i in new[] { 0, 2, 1, 0, 3, 2 }) m.AddIndex(i);   // both faces: a wall socket's plate may be seen from either side
        return m;
    }

    private void ClearLabels()
    {
        foreach (var (_, tex) in labels) tex.Dispose();
        labels.Clear();
    }

    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        if (mesh == null && labels.Count == 0) return;
        var render = capi.Render;
        var camera = capi.World.Player.Entity.CameraPos;
        render.GlDisableCullFace();
        render.GlToggleBlend(true);
        var prog = render.PreparedStandardShader(pos.X, pos.Y, pos.Z);
        prog.ViewMatrix = render.CameraMatrixOriginf;
        prog.ProjectionMatrix = render.CurrentProjectionMatrix;
        if (mesh != null)
        {
            prog.ModelMatrix = model.Identity().Translate(pos.X - camera.X, pos.Y - camera.Y, pos.Z - camera.Z).Values;
            prog.RgbaTint = new Vec4f(1, 1, 1, .6f);    // see-through; depth writes keep the cap colours readable
            // depth writes stay on: without them the pins' sides blend over their own cap tops and the colours drown
            render.RenderMultiTextureMesh(mesh, "tex");
            prog.RgbaTint = new Vec4f(1, 1, 1, 1);
        }
        RenderLabels(prog, camera);
        prog.Stop();
    }

    // Each tag is a strip on the plate from its pin's outer face outwards, as long as its text needs. Its text reads
    // along the strip; of the two ways it can lie, the one whose top points away from the camera is used, like a
    // sheet on a table seen from this side. Everything is built in the socket's own frame and turned like its shape.
    private void RenderLabels(IStandardShaderProgram prog, Vec3d camera)
    {
        if (labels.Count == 0) return;
        prog.NormalShaded = 0;
        // the tags are a reading aid, not scenery: they show through whatever stands next to the socket
        capi.Render.GLDisableDepthTest();
        turn.Identity().Translate(.5f, .5f, .5f).RotateXDeg(rotation.X).RotateYDeg(rotation.Y).RotateZDeg(rotation.Z).Translate(-.5f, -.5f, -.5f);
        foreach (var (pin, tex) in labels)
        {
            float aspect = (float)tex.Width / tex.Height;
            // first lay it one way, see whether its top points at the camera, and if so turn it round
            var (origin, right, up, length) = SocketOrientation.TagStrip(pin, aspect, false, TagWidth, TagGap, TagLift);
            var upWorld = turn.TransformVector(new Vec4f(up.X, up.Y, up.Z, 0));
            var mid = turn.TransformVector(new Vec4f(origin.X + right.X * length / 2, origin.Y, origin.Z + right.Z * length / 2, 1));
            var toCamera = new Vec3f((float)(camera.X - pos.X) - mid.X, (float)(camera.Y - pos.Y) - mid.Y, (float)(camera.Z - pos.Z) - mid.Z);
            if (upWorld.X * toCamera.X + upWorld.Y * toCamera.Y + upWorld.Z * toCamera.Z > 0)
                (origin, right, up, length) = SocketOrientation.TagStrip(pin, aspect, true, TagWidth, TagGap, TagLift);
            // columns: the quad's x -> right * length, y -> normal, z -> -up * width
            var local = new float[16];
            local[0] = right.X * length; local[1] = 0; local[2] = right.Z * length; local[3] = 0;
            local[4] = 0; local[5] = 1; local[6] = 0; local[7] = 0;
            local[8] = -up.X * TagWidth; local[9] = 0; local[10] = -up.Z * TagWidth; local[11] = 0;
            local[12] = origin.X; local[13] = origin.Y; local[14] = origin.Z; local[15] = 1;
            prog.Tex2D = tex.TextureId;
            prog.ModelMatrix = model.Identity().Translate(pos.X - camera.X, pos.Y - camera.Y, pos.Z - camera.Z).Mul(turn.Values).Mul(local).Values;
            capi.Render.RenderMesh(quad);
        }
        capi.Render.GLEnableDepthTest();
        prog.NormalShaded = 1;
    }

    public void Dispose()
    {
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        mesh?.Dispose();
        mesh = null;
        quad?.Dispose();
        quad = null;
        ClearLabels();
    }
}
