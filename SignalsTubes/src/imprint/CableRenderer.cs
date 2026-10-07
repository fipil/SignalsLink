using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsTubes.src.imprint;

/// <summary>
/// Draws the imprinter cable from its anchor to a moving or fixed end. The mesh is rebuilt only when
/// the far end moved. One instance per cable.
/// </summary>
public sealed class CableRenderer : IRenderer
{
    public double RenderOrder => .5;
    public int RenderRange => 64;

    private readonly ICoreClientAPI capi;
    private readonly Vec3d anchor;
    private readonly Func<Vec3d> farEnd;
    private readonly float thickness, sag;
    private readonly AssetLocation texture;
    private readonly Matrixf modelMat = new();
    private MeshRef mesh;
    private Vec3d lastEnd;
    public static readonly AssetLocation WireTexture = new("signals:item/wire.png");
    public static readonly AssetLocation RedTexture = new("signalstubes:block/cable-red.png");
    private static readonly AssetLocation Fallback = new("game:block/metal/plate/lead.png");

    public CableRenderer(ICoreClientAPI capi, Vec3d anchor, Func<Vec3d> farEnd, float thickness, AssetLocation texture, float sag = 1f)
    {
        this.capi = capi; this.anchor = anchor; this.farEnd = farEnd; this.thickness = thickness; this.texture = texture; this.sag = sag;
        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "signalstubes-cable");
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        Vec3d end = farEnd();
        if (end == null) return;
        if (mesh == null || lastEnd == null || end.SquareDistanceTo(lastEnd) > .0001)
        {
            mesh?.Dispose();
            mesh = capi.Render.UploadMesh(CableMesh.Make(new Vec3f(), end.SubCopy(anchor).ToVec3f(), thickness, sag));
            lastEnd = end.Clone();
        }
        var rpi = capi.Render;
        Vec3d cam = capi.World.Player.Entity.CameraPos;
        int tex = capi.Assets.Exists(texture.Clone().WithPathPrefixOnce("textures/")) ? rpi.GetOrLoadTexture(texture) : rpi.GetOrLoadTexture(Fallback);
        rpi.BindTexture2d(tex);
        var pos = anchor.AsBlockPos;
        var prog = rpi.PreparedStandardShader(pos.X, pos.Y, pos.Z);
        prog.Use();
        prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;
        prog.ViewMatrix = rpi.CameraMatrixOriginf;
        prog.ModelMatrix = modelMat.Identity().Translate(anchor.X - cam.X, anchor.Y - cam.Y, anchor.Z - cam.Z).Values;
        rpi.RenderMesh(mesh);
        prog.Stop();
    }

    public void Dispose()
    {
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        mesh?.Dispose();
        mesh = null;
    }
}
