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
    public const float ProductHeight = (19.732f + 3.5f) / 16, ProductScale = 0.25f;

    private readonly ICoreClientAPI capi;
    private readonly BlockPos pos;
    private readonly float blockRotation;
    private readonly MeshRef plate;
    private readonly MultiTextureMeshRef[] items = new MultiTextureMeshRef[BECraftingMachine.GridSlots + 1];
    private readonly bool[] flat = new bool[BECraftingMachine.GridSlots + 1];
    private readonly Matrixf model = new();
    private float angle;
    private float speed;             // network units while driven
    private bool braking;
    private float brakeStart, brakeW, brakeDistance, brakeTime;
    public float Angle => angle;
    public bool Stopped => !braking && speed == 0;

    public double RenderOrder => 0.5;
    public int RenderRange => 24;

    public PlateRenderer(ICoreClientAPI capi, BlockPos pos, MeshData plateMesh, float blockRotationRad)
    {
        this.capi = capi;
        this.pos = pos;
        blockRotation = blockRotationRad;
        plate = capi.Render.UploadMesh(plateMesh);
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

        if (items[BECraftingMachine.ProductSlot] != null)
        {
            // the product floats in the middle of the chamber and does not turn with the plate
            model.Identity().Translate(pos.X - camera.X, pos.Y - camera.Y, pos.Z - camera.Z)
                .Translate(.5f, ProductHeight, .5f).RotateY(blockRotation).Translate(-ProductScale / 2, 0, -ProductScale / 2)
                .Scale(ProductScale, ProductScale, ProductScale);
            prog.ModelMatrix = model.Values;
            render.RenderMultiTextureMesh(items[BECraftingMachine.ProductSlot], "tex");
        }
        prog.Stop();
    }

    public void Dispose()
    {
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        plate.Dispose();
        foreach (var item in items) item?.Dispose();
    }
}
