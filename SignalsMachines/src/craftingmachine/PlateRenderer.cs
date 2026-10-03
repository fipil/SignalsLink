using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsMachines.src.craftingmachine;

/// <summary>
/// Draws the crafting plate turning about the machine's axis, the nine cells' contents turning with it,
/// and the finished product floating in the chamber. The plate turns at the speed the server reports
/// while the clutch is closed and brakes into the home position itself once it opens.
/// </summary>
public sealed class PlateRenderer : IRenderer
{
    /// <summary>Model units: plate top, cell pitch and the first cell's centre (row 0 = north, column 0 = west).</summary>
    public const float PlateTop = 19.732f / 16, CellPitch = 2.5f / 16, FirstCell = 5.5f / 16, CellScale = 2.2f / 16;
    public const float ProductScale = 0.125f;
    public const float ProductHeight = 22.6f / 16;   // bottom of the product: clear of items on the plate (top 21.93) and of the lowered crystal (25.3)
    /// <summary>The crystal's travel between lowered (as modelled) and raised, and how long the ride takes.</summary>
    public const float CrystalTravel = 3.5f / 16, CrystalSeconds = 1f;

    private readonly ICoreClientAPI capi;
    private readonly BlockPos pos;
    private readonly float blockRotation;
    private readonly MeshRef plate, crystal, doorWest, doorEast;
    private float crystalLift = 1;   // 0 = lowered, 1 = raised
    public bool CrystalDown;
    public readonly DoorMotion WestDoor = new(), EastDoor = new();
    private readonly MultiTextureMeshRef[] items = new MultiTextureMeshRef[BECraftingMachine.GridSlots + 1];
    private readonly bool[] flat = new bool[BECraftingMachine.GridSlots + 1];
    private MultiTextureMeshRef ghost;
    /// <summary>How far the product has materialised (0 = nothing shown, 1 = nearly solid).</summary>
    public float Materialised;
    private float ghostTime, ghostHold;
    private readonly Random random = new();
    private static readonly Vec4f TemporalTint = new(0.65f, 0.95f, 1f, 1f);
    private static readonly Vec4f NoTint = new(1, 1, 1, 1);
    private readonly Matrixf model = new();
    private float angle;
    private float speed;             // network units while driven
    private bool braking;
    private float brakeStart, brakeW, brakeDistance, brakeTime;
    public float Angle => angle;
    public bool Stopped => !braking && speed == 0;

    public double RenderOrder => 0.5;
    public int RenderRange => 24;

    public PlateRenderer(ICoreClientAPI capi, BlockPos pos, MeshData plateMesh, MeshData crystalMesh, MeshData doorWestMesh, MeshData doorEastMesh, float blockRotationRad)
    {
        this.capi = capi;
        this.pos = pos;
        blockRotation = blockRotationRad;
        plate = capi.Render.UploadMesh(plateMesh);
        crystal = capi.Render.UploadMesh(crystalMesh);
        doorWest = capi.Render.UploadMesh(doorWestMesh);
        doorEast = capi.Render.UploadMesh(doorEastMesh);
        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "craftingmachine-plate");
    }

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

    /// <summary>What lies in a cell (0-8) or floats as the product (9); null empties it.</summary>
    public void SetStack(int slot, ItemStack stack)
    {
        items[slot]?.Dispose();
        items[slot] = null;
        if (stack?.Collectible == null) return;
        MeshData mesh;
        if (stack.Class == EnumItemClass.Block) capi.Tesselator.TesselateBlock(stack.Block, out mesh);
        else capi.Tesselator.TesselateItem(stack.Item, out mesh);
        flat[slot] = stack.Class == EnumItemClass.Item && stack.Item.Shape == null;   // a plain texture item lies flat; shaped items stand as modelled
        items[slot] = capi.Render.UploadMultiTextureMesh(mesh);
    }

    /// <summary>What is being made: shown as a translucent ghost while Materialised is above zero; null removes it.</summary>
    public void SetGhost(ItemStack stack)
    {
        ghost?.Dispose();
        ghost = null;
        if (stack?.Collectible == null) return;
        MeshData mesh;
        if (stack.Class == EnumItemClass.Block) capi.Tesselator.TesselateBlock(stack.Block, out mesh);
        else capi.Tesselator.TesselateItem(stack.Item, out mesh);
        ghost = capi.Render.UploadMultiTextureMesh(mesh);
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
        crystalLift = GameMath.Clamp(crystalLift + (CrystalDown ? -dt : dt) / CrystalSeconds, 0, 1);
        WestDoor.Advance(dt);
        EastDoor.Advance(dt);
    }

    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        Advance(dt);
        var render = capi.Render;
        var camera = capi.World.Player.Entity.CameraPos;
        render.GlDisableCullFace();
        render.GlToggleBlend(true);
        var prog = render.PreparedStandardShader(pos.X, pos.Y, pos.Z);
        prog.ViewMatrix = render.CameraMatrixOriginf;
        prog.ProjectionMatrix = render.CurrentProjectionMatrix;

        // everything on the plate shares its turn: block rotation plus the running angle
        void Turn() => model.Identity()
            .Translate(pos.X - camera.X, pos.Y - camera.Y, pos.Z - camera.Z)
            .Translate(.5f, 0, .5f).RotateY(angle + blockRotation).Translate(-.5f, 0, -.5f);

        Turn();
        prog.Tex2D = capi.BlockTextureAtlas.AtlasTextures[0].TextureId;
        prog.ModelMatrix = model.Values;
        render.RenderMesh(plate);

        for (int i = 0; i < BECraftingMachine.GridSlots; i++)
        {
            if (items[i] == null) continue;
            Turn();
            float cx = FirstCell + CellPitch * (i % BECraftingMachine.GridSize), cz = FirstCell + CellPitch * (i / BECraftingMachine.GridSize);
            model.Translate(cx - CellScale / 2, PlateTop, cz - CellScale / 2).Scale(CellScale, CellScale, CellScale);
            if (flat[i]) model.Translate(.5f, 0.02f, .5f).RotateXDeg(90).Translate(-.5f, -.5f, -.5f);   // items lie on the plate
            prog.ModelMatrix = model.Values;
            render.RenderMultiTextureMesh(items[i], "tex");
        }

        // the crystal rides up and down on the machine's axis, turned with the block only
        model.Identity().Translate(pos.X - camera.X, pos.Y - camera.Y + crystalLift * CrystalTravel, pos.Z - camera.Z)
            .Translate(.5f, 0, .5f).RotateY(blockRotation).Translate(-.5f, 0, -.5f);
        prog.Tex2D = capi.BlockTextureAtlas.AtlasTextures[0].TextureId;
        prog.ModelMatrix = model.Values;
        render.RenderMesh(crystal);

        // the doors: pushed out along their normal (model west = -x, east = +x) and dropped, turned with the block
        foreach (var (door, mesh, nx) in new[] { (WestDoor, doorWest, -1f), (EastDoor, doorEast, 1f) })
        {
            var (outward, down) = door.Offsets;
            model.Identity().Translate(pos.X - camera.X, pos.Y - camera.Y, pos.Z - camera.Z)
                .Translate(.5f, 0, .5f).RotateY(blockRotation).Translate(-.5f, 0, -.5f)
                .Translate(nx * outward, -down, 0);
            prog.ModelMatrix = model.Values;
            render.RenderMesh(mesh);
        }

        // the product floats in the middle of the chamber and does not turn with the plate
        void Float(float scale, float bob) => model.Identity().Translate(pos.X - camera.X, pos.Y - camera.Y + bob, pos.Z - camera.Z)
            .Translate(.5f, ProductHeight, .5f).RotateY(blockRotation).Translate(-scale / 2, 0, -scale / 2)
            .Scale(scale, scale, scale);

        if (items[BECraftingMachine.ProductSlot] != null)
        {
            Float(ProductScale, 0);
            prog.ModelMatrix = model.Values;
            render.RenderMultiTextureMesh(items[BECraftingMachine.ProductSlot], "tex");
        }
        else if (ghost != null && Materialised > 0)
        {
            // the ghost: translucent in the temporal hue, flickering, shimmering, denser as the work goes on
            float alpha = GhostAlpha(dt);
            if (alpha > 0)
            {
                float shimmer = 1 + 0.04f * GameMath.Sin(ghostTime * 17);
                Float(ProductScale * shimmer, 0.01f * GameMath.Sin(ghostTime * 5));
                prog.ModelMatrix = model.Values;
                prog.RgbaTint = new Vec4f(TemporalTint.X, TemporalTint.Y, TemporalTint.Z, alpha);
                prog.ExtraGlow = 120;
                prog.RgbaGlowIn = new Vec4f(0.4f, 0.95f, 1f, 0.5f);
                render.RenderMultiTextureMesh(ghost, "tex");
                prog.RgbaTint = NoTint;
                prog.ExtraGlow = 0;
            }
        }
        prog.Stop();
    }

    private bool blackout;

    // flicker: short random blackouts over a slow shimmer; the base density follows Materialised
    private float GhostAlpha(float dt)
    {
        ghostTime += dt;
        ghostHold -= dt;
        if (ghostHold <= 0)
        {
            blackout = !blackout && random.NextDouble() < 0.3;
            ghostHold = blackout ? 0.03f + (float)random.NextDouble() * 0.08f : 0.15f + (float)random.NextDouble() * 0.6f;
        }
        if (blackout) return 0;
        float wave = 0.75f + 0.25f * GameMath.Sin(ghostTime * 11) * GameMath.Sin(ghostTime * 4.3f);
        return (0.15f + 0.6f * Math.Min(1, Materialised)) * wave;
    }

    public void Dispose()
    {
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        plate.Dispose();
        crystal.Dispose();
        doorWest.Dispose();
        doorEast.Dispose();
        foreach (var item in items) item?.Dispose();
        ghost?.Dispose();
    }
}
