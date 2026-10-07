using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace SignalsTubes.src.socket;

/// <summary>
/// Translucent picture of what a click would put into the socket: its pins and the tube, already turned, and a
/// name tag over each pin (drawn as the game draws player name tags: projected to the screen in the ortho stage).
/// </summary>
public sealed class SocketPreviewRenderer : IRenderer
{
    public double RenderOrder => .5;
    public int RenderRange => 32;

    private readonly ICoreClientAPI capi;
    private readonly BlockPos pos;
    private readonly Matrixf model = new();
    private MultiTextureMeshRef mesh;   // block textures may sit on several atlas pages; a plain mesh ref binds only the first
    private readonly List<(Vec3d at, Vec3d outward, bool sideways, LoadedTexture tex)> labels = new();

    public SocketPreviewRenderer(ICoreClientAPI capi, BlockPos pos)
    {
        this.capi = capi; this.pos = pos;
        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "signalstubes-socket-preview");
        capi.Event.RegisterRenderer(this, EnumRenderStage.Ortho, "signalstubes-socket-labels");
    }

    public void SetMesh(MeshData data)
    {
        mesh?.Dispose();
        mesh = data == null ? null : capi.Render.UploadMultiTextureMesh(data);
    }

    /// <summary>
    /// Tags beside the pins. `at` is the point just outside a pin, `outward` the world direction away from the socket
    /// there; a `sideways` tag hangs off that point by its inner edge, the others stand above or below it.
    /// </summary>
    public void SetLabels(IEnumerable<(Vec3d at, Vec3d outward, bool sideways, string text)> items)
    {
        ClearLabels();
        var font = new CairoFont(18, GuiStyle.StandardFontName, ColorUtil.WhiteArgbDouble);
        foreach (var (at, outward, sideways, text) in items)
        {
            var bg = new TextBackground { FillColor = GuiStyle.DialogLightBgColor, Padding = 3, Radius = GuiStyle.ElementBGRadius };
            labels.Add((at, outward, sideways, capi.Gui.TextTexture.GenUnscaledTextTexture(text, font, bg)));
        }
    }

    private void ClearLabels()
    {
        foreach (var (_, _, _, tex) in labels) tex.Dispose();
        labels.Clear();
    }

    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        if (stage == EnumRenderStage.Ortho) { RenderLabels(); return; }
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

    // The tags shrink with distance as the game's name tags do. Each hangs just outside the socket at its pin and
    // grows the way the socket's side points there: the outward direction is projected onto the screen, and a
    // sideways tag puts its inner edge on the point and extends the way that direction goes (left or right); a
    // front/back tag stands above the point when the direction goes up on screen, else below. Far tags draw first.
    private void RenderLabels()
    {
        if (labels.Count == 0) return;
        var render = capi.Render;
        const float gap = 3;
        var placed = new List<(double depth, float x, float y, float w, float h, int tex)>();
        foreach (var (at, outward, sideways, tex) in labels)
        {
            var screen = MatrixToolsd.Project(at, render.PerspectiveProjectionMat, render.PerspectiveViewMat, render.FrameWidth, render.FrameHeight);
            var tip = MatrixToolsd.Project(at.AddCopy(outward.X * .1, outward.Y * .1, outward.Z * .1), render.PerspectiveProjectionMat, render.PerspectiveViewMat, render.FrameWidth, render.FrameHeight);
            if (screen.Z < 0 || tip.Z < 0) continue;   // behind the camera
            float scale = Math.Min(1f, 4f / Math.Max(1f, (float)screen.Z));
            float w = scale * tex.Width, h = scale * tex.Height;
            double dx = tip.X - screen.X, dy = tip.Y - screen.Y;   // y up on screen
            float x, y;   // top left, screen y downwards
            if (sideways)
            {
                x = dx < 0 ? (float)screen.X - w - gap : (float)screen.X + gap;
                y = render.FrameHeight - (float)screen.Y - h / 2;
            }
            else
            {
                x = (float)screen.X - w / 2;
                y = dy > 0 ? render.FrameHeight - (float)screen.Y - h - gap : render.FrameHeight - (float)screen.Y + gap;
            }
            placed.Add((screen.Z, x, y, w, h, tex.TextureId));
        }
        foreach (var l in placed.OrderByDescending(l => l.depth)) render.Render2DTexture(l.tex, l.x, l.y, l.w, l.h, 20);
    }

    public void Dispose()
    {
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Ortho);
        mesh?.Dispose();
        mesh = null;
        ClearLabels();
    }
}
