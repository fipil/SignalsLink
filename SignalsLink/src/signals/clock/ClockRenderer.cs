using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.clock;

/// <summary>
/// Draws the lights of every loaded clock: one small glowing cube per point, placed each frame from the
/// figure table and the glide phase. The bulb's glass is part of the block shape and is drawn by the chunk
/// after this opaque stage, so the lights show through it. One renderer and one mesh serve all clocks.
/// </summary>
public sealed class ClockRenderer : ModSystem, IRenderer
{
    // The bulb's inside in block units, for the unrotated shape: glass from (5.2, 5, 5.2) to (10.8, 10.6, 10.8) voxels.
    public static readonly Vector3 Center = new(8 / 16f, 7.8f / 16, 8 / 16f);
    /// <summary>Edge of each group's cube: phase well inside the glass, turn in the middle, beat in the core.</summary>
    public static readonly float[] Edge = { 3.9f / 16, 2.5f / 16, 1.25f / 16 };
    /// <summary>Side of a light, in blocks.</summary>
    public static readonly float[] Size = { .5f / 16, .42f / 16, .35f / 16 };
    private static readonly Vec4f[] Tint =
    {
        new(.25f, .4f, 1f, 1f),      // phase: deep blue
        new(.2f, .9f, .65f, 1f),     // turn: blue-green towards emerald
        new(.6f, 1f, 1f, 1f),        // beat: light temporal turquoise
    };

    public double RenderOrder => .5;
    public int RenderRange => 48;

    private ICoreClientAPI capi;
    private readonly HashSet<BEClock> clocks = new();
    private MultiTextureMeshRef mesh;
    private bool meshFailed;
    private readonly Matrixf model = new();

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        api.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "signalslink-clock");
    }

    public void Add(BEClock clock) => clocks.Add(clock);
    public void Remove(BEClock clock) => clocks.Remove(clock);

    // Real milliseconds per step of the day, infinite while time stands still.
    private static double MsPerStep(IGameCalendar cal)
    {
        double speed = cal.SpeedOfTime * cal.CalendarSpeedMul;   // game seconds per real second
        if (speed <= 0) return double.PositiveInfinity;
        return cal.HoursPerDay * 3600.0 * 1000 / speed / ClockTime.StepsPerDay;
    }

    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        if (clocks.Count == 0) return;
        if (mesh == null && !LoadMesh()) return;
        var cal = capi.World.Calendar;
        int step = ClockTime.StepOf(cal.HourOfDay, cal.HoursPerDay);
        long now = capi.ElapsedMilliseconds;
        double msPerStep = MsPerStep(cal);
        var render = capi.Render;
        var camera = capi.World.Player.Entity.CameraPos;
        var accessor = capi.World.BlockAccessor;
        foreach (var clock in clocks.ToArray())
        {
            var pos = clock.Pos;
            // An entity the game dropped without OnBlockRemoved (a block that went missing from the assets,
            // a replaced block) must not keep its lights burning: only a live clock at its position is drawn.
            if (pos == null || clock.Block is not BlockClock || !ReferenceEquals(accessor.GetBlockEntity(pos), clock))
            {
                clocks.Remove(clock);
                continue;
            }
            if (camera.SquareDistanceTo(pos.X + .5, pos.Y + .5, pos.Z + .5) > RenderRange * RenderRange) continue;
            clock.Lights.Update(step, now, msPerStep);
            var prog = render.PreparedStandardShader(pos.X, pos.Y, pos.Z);
            prog.ViewMatrix = render.CameraMatrixOriginf;
            prog.ProjectionMatrix = render.CurrentProjectionMatrix;
            prog.ExtraGlow = 255;
            // the variant's shape rotation, so the lights turn with a clock on a wall or ceiling
            var shape = clock.Block?.Shape;
            float rx = shape?.rotateX ?? 0, ry = shape?.rotateY ?? 0, rz = shape?.rotateZ ?? 0;
            for (int g = 0; g < ClockFigures.Groups; g++)
            {
                prog.RgbaTint = Tint[g];
                prog.RgbaGlowIn = new Vec4f(Tint[g].X, Tint[g].Y, Tint[g].Z, .6f);
                float scale = Size[g] * 16;   // the mesh is a one-voxel cube about the block centre
                for (int k = 0; k < ClockFigures.PointCount(g); k++)
                {
                    var p = clock.Lights.Point(g, k, now, Center, Edge[g]);
                    prog.ModelMatrix = model.Identity()
                        .Translate(pos.X - camera.X, pos.Y - camera.Y, pos.Z - camera.Z)
                        .Translate(.5f, .5f, .5f).RotateXDeg(rx).RotateYDeg(ry).RotateZDeg(rz).Translate(-.5f, -.5f, -.5f)
                        .Translate(p.X, p.Y, p.Z).Scale(scale, scale, scale).Translate(-.5f, -.5f, -.5f).Values;
                    render.RenderMultiTextureMesh(mesh, "tex");
                }
            }
            prog.ExtraGlow = 0;
            prog.RgbaTint = new Vec4f(1, 1, 1, 1);
            prog.Stop();
        }
    }

    // One voxel of glowing light, textured from the clock block's atlas page so any block texture page works.
    private bool LoadMesh()
    {
        if (meshFailed) return false;
        var block = capi.World.GetBlock(new AssetLocation("signalslink", "clock-north-down"));
        var shape = capi.Assets.TryGet(new AssetLocation("signalslink", "shapes/block/clock/light.json"))?.ToObject<Shape>();
        if (block == null || shape == null)
        {
            capi.Logger.Error("[signalslink] Clock light shape or block missing; the clock lights are not drawn.");
            meshFailed = true;
            return false;
        }
        capi.Tesselator.TesselateShape(block, shape, out MeshData data);
        mesh = capi.Render.UploadMultiTextureMesh(data);
        return true;
    }

    public override void Dispose()
    {
        mesh?.Dispose();
        mesh = null;
        clocks.Clear();
        capi?.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        base.Dispose();
    }
}
