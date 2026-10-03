using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace SignalsMachines.src.craftingmachine;

/// <summary>Draws the crafting plate turning about the machine's axis at the plate speed the server reports.</summary>
public sealed class PlateRenderer : IRenderer
{
    private readonly ICoreClientAPI capi;
    private readonly BlockPos pos;
    private readonly MeshRef mesh;
    private readonly Matrixf model = new();
    private float angle;
    private float speed;             // network units while driven
    private bool braking;
    private float brakeStart, brakeW, brakeDistance, brakeTime;
    public float Angle => angle;
    public bool Stopped => !braking && speed == 0;

    /// <summary>Clutch closed: turn at this network speed (the mechanical renderer's scale).</summary>
    public void Drive(float networkSpeed) { speed = networkSpeed; braking = false; }

    /// <summary>Clutch opened: brake smoothly into the home position.</summary>
    public void Release()
    {
        if (braking || speed <= 0) { speed = 0; return; }
        brakeW = speed * 50f;
        brakeStart = angle;
        brakeDistance = PlateDrive.BrakingDistance(angle, brakeW);
        brakeTime = 0;
        braking = true;
        speed = 0;
    }

    public double RenderOrder => 0.5;
    public int RenderRange => 24;

    public PlateRenderer(ICoreClientAPI capi, BlockPos pos, MeshData plate)
    {
        this.capi = capi;
        this.pos = pos;
        mesh = capi.Render.UploadMesh(plate);
        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "craftingmachine-plate");
    }

    /// <summary>Advances the picture; separate from drawing so it can be tested.</summary>
    public void Advance(float dt)
    {
        if (braking)
        {
            brakeTime += dt;
            angle = PlateDrive.Brake(brakeStart, brakeW, brakeDistance, brakeTime);
            if (brakeTime >= 2 * brakeDistance / brakeW) { braking = false; angle = 0; }   // home, exactly
            else angle %= GameMath.TWOPI;
        }
        else angle = (angle + speed * dt * 50f) % GameMath.TWOPI;
    }

    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        Advance(dt);
        var render = capi.Render;
        var camera = capi.World.Player.Entity.CameraPos;
        render.GlDisableCullFace();
        render.GlToggleBlend(true);
        var prog = render.PreparedStandardShader(pos.X, pos.Y, pos.Z);
        prog.Tex2D = capi.BlockTextureAtlas.AtlasTextures[0].TextureId;
        prog.ModelMatrix = model.Identity()
            .Translate(pos.X - camera.X, pos.Y - camera.Y, pos.Z - camera.Z)
            .Translate(.5f, 0, .5f).RotateY(angle).Translate(-.5f, 0, -.5f)
            .Values;
        prog.ViewMatrix = render.CameraMatrixOriginf;
        prog.ProjectionMatrix = render.CurrentProjectionMatrix;
        render.RenderMesh(mesh);
        prog.Stop();
    }

    public void Dispose()
    {
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        mesh.Dispose();
    }
}
