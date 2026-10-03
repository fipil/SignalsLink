using signals.src;
using signals.src.signalNetwork;
using SignalsTubes.src.circuit;
using SignalsTubes.src.programtube;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace SignalsMachines.src.craftingmachine;

/// <summary>Levels the machine is driven with this step, read from pins 1-5 or from the tube.</summary>
public readonly record struct MachineInputs(byte Clutch, byte Crystal, byte Strength, byte DoorWest, byte DoorEast)
{
    public byte this[int pin] => pin switch { 1 => Clutch, 2 => Crystal, 3 => Strength, 4 => DoorWest, _ => DoorEast };
}

/// <summary>
/// The controller of the machine. Eight Signals pins: 0 state (driven), 1 clutch, 2 crystal, 3 strength,
/// 4 west door, 5 east door, 6-7 reserve. A programmed tube in the socket takes over: the machine then
/// reads its inputs only from the tube's output pins, wires on those pins are ignored; the tube's own
/// input pins still read the wires (and the state pin).
/// </summary>
public class BECraftingMachine : BlockEntity
{
    public const int PinCount = 8, StatePin = 0, FirstInput = 1, LastInput = 5;

    private ItemStack tube;
    private CircuitProgram program;
    private CircuitSimulator sim;
    private SignalNetworkMod signalMod;
    private MeshData tubeMesh;
    private MachineInputs inputs;
    private byte state;
    private BEBehaviorMPConsumer mpc;
    private PlateRenderer plateRenderer;
    private float plateSpeed, syncedPlateSpeed, networkSpeed;
    private double lastPlateSync;
    /// <summary>Speed of the mechanical network at the axle; tests substitute it.</summary>
    public Func<float> SpeedSource;

    public bool HasTube => tube != null;
    public ItemStack Tube => tube;
    /// <summary>A programmed tube sits in the socket and drives the inputs.</summary>
    public bool TubeControls => program != null;
    public MachineInputs Inputs => inputs;
    public byte State => state;
    /// <summary>Plate speed in network units (clutch, inertia).</summary>
    public float PlateSpeed => plateSpeed;
    public float NetworkSpeed => networkSpeed;
    /// <summary>The plate turns fast enough for the machine to work.</summary>
    public bool PlateRunning => PlateDrive.Running(plateSpeed, networkSpeed);

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        tube?.ResolveBlockOrItem(api.World);
        LoadProgram();
        RebuildMesh();
        mpc = GetBehavior<BEBehaviorMPConsumer>();
        SpeedSource ??= () => mpc?.Network == null ? 0 : mpc.TrueSpeed;
        if (api.Side == EnumAppSide.Server)
        {
            signalMod = api.ModLoader?.GetModSystem<SignalNetworkMod>();
            signalMod?.RegisterSignalTickListener(OnSignalTick);
            RegisterGameTickListener(OnMechanicsTick, 100);
        }
        else if (api is ICoreClientAPI capi && Block is BlockCraftingMachine)   // not when the block is already gone
        {
            plateRenderer = new PlateRenderer(capi, Pos, PlateMesh(capi));
            UpdatePlatePicture();
        }
    }

    public override void OnBlockUnloaded() { base.OnBlockUnloaded(); signalMod?.DisposeSignalTickListener(OnSignalTick); plateRenderer?.Dispose(); }
    public override void OnBlockRemoved() { base.OnBlockRemoved(); signalMod?.DisposeSignalTickListener(OnSignalTick); plateRenderer?.Dispose(); }

    // ---- mechanics: clutch and plate

    /// <summary>Server, every 0.1 s: the plate follows the network speed through the clutch, with inertia.</summary>
    public void OnMechanicsTick(float dt)
    {
        networkSpeed = SpeedSource();
        float target = inputs.Clutch > 0 ? networkSpeed : 0;
        plateSpeed = PlateDrive.Step(plateSpeed, target, dt);
        // clients integrate the angle themselves; send the speed when it moved, or once a second while turning
        double now = Api.World.ElapsedMilliseconds;
        if (Math.Abs(plateSpeed - syncedPlateSpeed) > .005f || (plateSpeed > 0 && now - lastPlateSync > 1000))
        {
            syncedPlateSpeed = plateSpeed;
            lastPlateSync = now;
            MarkDirty();
        }
    }

    private void LoadProgram()
    {
        program = tube == null || Api?.Side != EnumAppSide.Server ? null : TubeProgram.Get(tube, Api);
        sim = program == null ? null : new CircuitSimulator(program, id => ProgramStore.Of(Api)?.Get(id));
    }

    // ---- signals

    private void OnSignalTick()
    {
        var beh = GetBehavior<BEBehaviorSignalConnector>();
        if (beh == null) return;
        byte Node(int i) => beh.GetNodeAt(new NodePos(Pos, i))?.value ?? 0;
        var next = sim == null ? Read(Node) : RunTube(Node);
        if (next != inputs) { inputs = next; MarkDirty(); }
        beh.UpdateSource(new NodePos(Pos, StatePin), state);
    }

    public static bool IsMachineInput(int pin) => pin >= FirstInput && pin <= LastInput;

    /// <summary>Inputs straight from the pins (wire control).</summary>
    public static MachineInputs Read(System.Func<int, byte> node) => new(node(1), node(2), node(3), node(4), node(5));

    /// <summary>One tube step: its input pins read the state and reserve pins, its output pins become the machine's inputs.</summary>
    private MachineInputs RunTube(System.Func<int, byte> node)
    {
        foreach (var pin in program.Pins)
            if (pin.Role != PinRole.Output) sim.SetInput(pin.Index, IsMachineInput(pin.Index) ? (byte)0 : node(pin.Index));
        sim.Step();
        byte Out(int i) => program.Pins.Any(p => p.Index == i && p.Role == PinRole.Output) ? sim.GetOutput(i) : (byte)0;
        return new(Out(1), Out(2), Out(3), Out(4), Out(5));
    }

    // ---- socket

    public bool Interact(IPlayer player)
    {
        var hand = player.InventoryManager.ActiveHotbarSlot;
        bool insert = tube == null && hand.Itemstack?.Collectible is ItemProgramTube;
        bool remove = tube != null && hand.Empty;
        if (!insert && !remove) return false;
        // Only the server transfers items. Creative also consumes this one stack, so removing
        // the tube cannot duplicate a player's programmed item.
        if (Api.Side == EnumAppSide.Client) return true;
        if (insert)
        {
            tube = hand.TakeOut(1);
            hand.MarkDirty();
        }
        else
        {
            ItemStack taken = tube;
            tube = null;
            if (!player.InventoryManager.TryGiveItemstack(taken, true))
                Api.World.SpawnItemEntity(taken, Pos.ToVec3d().Add(.5, .5, .5));
        }
        LoadProgram();
        MarkDirty(true);
        Api.World.PlaySoundAt(new AssetLocation("signalsmachines:sounds/tube-click"), Pos.X + .5, Pos.Y + .15, Pos.Z + .5, null, false, 12, .65f);
        return true;
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        if (tube != null) tree.SetItemstack("programTube", tube);
        else tree.RemoveAttribute("programTube");
        tree.SetBytes("inputs", new[] { inputs.Clutch, inputs.Crystal, inputs.Strength, inputs.DoorWest, inputs.DoorEast });
        tree.SetInt("state", state);
        tree.SetBool("tubeControls", program != null);
        tree.SetFloat("plateSpeed", plateSpeed);
        tree.SetFloat("networkSpeed", networkSpeed);
    }

    private bool tubeControlsSynced;   // client copy of TubeControls

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        tube = tree.GetItemstack("programTube");
        tube?.ResolveBlockOrItem(worldForResolving);
        var b = tree.GetBytes("inputs", new byte[5]);
        inputs = new MachineInputs(b[0], b[1], b[2], b[3], b[4]);
        state = (byte)tree.GetInt("state");
        tubeControlsSynced = tree.GetBool("tubeControls");
        plateSpeed = tree.GetFloat("plateSpeed");
        networkSpeed = tree.GetFloat("networkSpeed");
        UpdatePlatePicture();
        if (Api is ICoreClientAPI)
        {
            RebuildMesh();
            MarkDirty(true);
        }
        else if (Api != null) LoadProgram();
    }

    public override void GetBlockInfo(IPlayer forPlayer, System.Text.StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        bool byTube = Api?.Side == EnumAppSide.Server ? TubeControls : tubeControlsSynced;
        dsc.AppendLine(Lang.Get(byTube ? "signalsmachines:machine-control-tube" : "signalsmachines:machine-control-wires"));
        var block = Block as BlockCraftingMachine;
        string Side(int pin) => block == null ? "?" : Lang.Get("signalsmachines:side-short-" + block.DoorSide(pin).Code);
        dsc.AppendLine(Lang.Get("signalsmachines:machine-inputs", inputs.Clutch, inputs.Crystal, inputs.Strength, Side(4), inputs.DoorWest, Side(5), inputs.DoorEast));
        dsc.AppendLine(Lang.Get("signalsmachines:machine-state", state));
        dsc.AppendLine(Lang.Get("signalsmachines:machine-drive", (int)(networkSpeed * 100), (int)(plateSpeed * 100)));
        if (tube != null) dsc.Append(ItemProgramTube.FullInfo(tube, Api.World));
    }

    // ---- mesh

    private void RebuildMesh()
    {
        tubeMesh = null;
        if (Api is not ICoreClientAPI capi || tube?.Collectible is not ItemProgramTube item || Block is not BlockCraftingMachine block) return;
        MeshData mesh = item.BuildMesh(capi, tube, capi.Tesselator.GetTextureSource(Block));
        mesh.Scale(new Vec3f(), .5f, .5f, .5f);
        mesh.Translate(4f / 16, 1f / 16, 8f / 16);
        mesh.Rotate(new Vec3f(.5f, .5f, .5f), 0, block.RotationRadians, 0);
        tubeMesh = mesh;
    }

    // The client turns the plate at the synced speed while the clutch is closed and brakes it into the
    // home position itself once it opens, so the picture always comes to rest aligned with the grid.
    private void UpdatePlatePicture()
    {
        if (plateRenderer == null) return;
        if (inputs.Clutch > 0 && networkSpeed > PlateDrive.Still) plateRenderer.Drive(plateSpeed);
        else plateRenderer.Release();
    }

    private MeshData PlateMesh(ICoreClientAPI capi)
    {
        var shape = capi.Assets.Get(new AssetLocation("signalsmachines", "shapes/block/craftingmachine-plate.json")).ToObject<Shape>();
        capi.Tesselator.TesselateShape(Block, shape, out MeshData mesh, new Vec3f(0, ((BlockCraftingMachine)Block).RotationDegrees, 0));
        return mesh;
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        var mesh = tubeMesh;
        if (mesh != null) mesher.AddMeshData(mesh.Clone());
        return false; // Keep the ordinary block mesh (axle and plate are rendered separately).
    }

    public override void OnBlockBroken(IPlayer byPlayer = null)
    {
        if (Api.Side == EnumAppSide.Server && tube != null)
        {
            Api.World.SpawnItemEntity(tube, Pos.ToVec3d().Add(.5, .5, .5));
            tube = null;
        }
        base.OnBlockBroken(byPlayer);
    }
}
