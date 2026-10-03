using signals.src;
using signals.src.signalNetwork;
using SignalsTubes.src.circuit;
using SignalsTubes.src.programtube;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;
using SignalsLink.src.signals.managedchute.transporting;

namespace SignalsMachines.src.craftingmachine;

/// <summary>One of the nine cells of the plate: anything goes in, chutes may take it back out.</summary>
public class ItemSlotGridCell : ItemSlot
{
    public ItemSlotGridCell(InventoryBase inventory) : base(inventory) { }
}

/// <summary>The finished product floating in the chamber: only ever taken out.</summary>
public class ItemSlotProduct : ItemSlot
{
    public ItemSlotProduct(InventoryBase inventory) : base(inventory) { }
    public override bool CanHold(ItemSlot source) => false;
    public override bool CanTakeFrom(ItemSlot source, EnumMergePriority priority = EnumMergePriority.AutoMerge) => false;
}

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
public class BECraftingMachine : BlockEntityContainer, ISidedAutomation
{
    public const int PinCount = 8, StatePin = 0, FirstInput = 1, LastInput = 5;
    public const int GridSize = 3, GridSlots = 9, ProductSlot = 9;

    private readonly InventoryGeneric inventory;
    public override InventoryBase Inventory => inventory;
    public override string InventoryClassName => "craftingmachine";
    /// <summary>Called on the client whenever the slots changed, so the picture can be rebuilt.</summary>
    public event Action SlotsChanged;

    public BECraftingMachine()
    {
        inventory = new InventoryGeneric(GridSlots + 1, null, null, (id, inv) => id == ProductSlot ? new ItemSlotProduct(inv) : new ItemSlotGridCell(inv));
        inventory.SlotModified += _ => { FindRecipe(); MarkDirty(); SlotsChanged?.Invoke(); };
    }

    // ---- doors: automation comes in only through a face whose door is open

    /// <summary>Door on pin 4 (west in the model) or 5 (east) is open: its input level is above zero.</summary>
    public bool DoorOpen(int pin) => inputs[pin] > 0;

    /// <summary>The crystal is down: the machine casts block light (BlockCraftingMachine.GetLightHsv).</summary>
    public bool Lit => inputs.Crystal > 0;

    // Lighting up: exchanging the block for itself makes the world place GetLightHsv anew (the lantern's way).
    // Going dark the exchange sees no light on either side, so the light must be taken away by hand.
    private void Relight()
    {
        if (Api == null) return;
        if (Lit) Api.World.BlockAccessor.ExchangeBlock(Block.Id, Pos);
        else Api.World.BlockAccessor.RemoveBlockLight(BlockCraftingMachine.CrystalLight, Pos);
    }

    private readonly long[] doorOpenedAt = new long[2];   // server: when pins 4/5 last went high

    /// <summary>The door has finished its ride down; closing shuts access the moment the pin drops.</summary>
    public bool DoorFullyOpen(int pin) =>
        DoorOpen(pin) && Api.World.ElapsedMilliseconds - doorOpenedAt[pin - 4] >= (DoorMotion.PushSeconds + DoorMotion.OpenSlideSeconds) * 1000;

    /// <summary>Nothing lands on or leaves a turning plate: clutch open and the plate at rest.</summary>
    public bool PlateStill => inputs.Clutch == 0 && plateSpeed == 0;

    /// <summary>Only the chamber (the upper block) has doors; the pedestal lets nothing through.</summary>
    public bool AllowsAutomation(BlockPos touched, BlockFacing face)
    {
        if (Block is not BlockCraftingMachine block || touched.Y != Pos.Y + 1 || !PlateStill) return false;
        return (DoorFullyOpen(4) && face == block.DoorSide(4)) || (DoorFullyOpen(5) && face == block.DoorSide(5));
    }

    /// <summary>Cells that hold something; the strength the recipe will ask for.</summary>
    public int OccupiedCells => Enumerable.Range(0, GridSlots).Count(i => !inventory[i].Empty);

    // ---- placer: the machine works with the class of whoever placed it

    private string placerUid, placerName;
    private HashSet<string> placerTraits;     // null = no class chosen = every trait recipe allowed (as the game does)
    public string PlacerName => placerName;

    /// <summary>Remembers the player and a snapshot of their traits; refreshed whenever they use the machine.</summary>
    public void SetPlacer(IPlayer player)
    {
        if (player == null) return;
        placerUid = player.PlayerUID;
        placerName = player.PlayerName;
        placerTraits = TraitsOf(player);
        FindRecipe();
        MarkDirty();
    }

    /// <summary>Class traits plus extra traits, the same way CharacterSystem.HasTrait reads them; null without a class.</summary>
    private HashSet<string> TraitsOf(IPlayer player)
    {
        var attrs = player.Entity?.WatchedAttributes;
        string classCode = attrs?.GetString("characterClass");
        if (classCode == null) return null;
        var traits = new HashSet<string>();
        var characters = Api?.ModLoader?.GetModSystem<CharacterSystem>();
        if (characters != null && characters.characterClassesByCode.TryGetValue(classCode, out var cls) && cls.Traits != null) traits.UnionWith(cls.Traits);
        var extra = attrs.GetStringArray("extraTraits");
        if (extra != null) traits.UnionWith(extra);
        return traits;
    }

    /// <summary>The recipe the cells form right now, or null.</summary>
    public MachineRecipes.Match Recipe { get; private set; }

    private ItemSlot[] GridCells() => Enumerable.Range(0, GridSlots).Select(i => (ItemSlot)inventory[i]).ToArray();

    private void FindRecipe()
    {
        if (Api?.Side != EnumAppSide.Server || Api.World?.GridRecipes == null) return;
        // no class on record: the game lets such a player make every trait recipe, so does the machine
        System.Func<string, bool> hasTrait = placerTraits == null ? _ => true : placerTraits.Contains;
        Recipe = MachineRecipes.Find(Api.World, GridCells(), hasTrait);
        recipeText = Recipe == null ? "" : ProductText(Recipe);
    }

    /// <summary>"3x Linen" - what one cycle would make; computed only when the cells change.</summary>
    private string ProductText(MachineRecipes.Match match)
    {
        var output = match.Recipe.Output?.ResolvedItemStack;
        if (output == null) return match.Recipe.Name?.ToString() ?? "?";
        int count = output.StackSize * MachineRecipes.Cycles(match, GridCells(), GridSize);
        string name;
        try { name = output.GetName(); } catch (ArgumentNullException) { name = output.Collectible.Code.ToShortString(); }   // no language tables outside the game
        return count + "x " + name;
    }

    private string recipeText = "";

    private ItemStack tube;
    private CircuitProgram program;
    private CircuitSimulator sim;
    private SignalNetworkMod signalMod;
    private MeshData tubeMesh;
    private MachineInputs inputs;
    private byte state;
    private readonly MachineProcess process = new();
    public MachineProcess Process => process;
    /// <summary>Temporal stability taken from players per 0.1 s: a whisper while crafting, a blast through an open door (contract 23, 23a).</summary>
    public const float CraftDrainPerTick = 0.0005f, LeakDrainPerTick = 0.01f, CraftDrainRange = 2f, LeakRange = 8f;
    private BEBehaviorMPConsumer mpc;
    private PlateRenderer plateRenderer;
    private MachineEffects effects;
    private int overloadSerial;
    /// <summary>Counts overloads; the client plays the bang when it changes.</summary>
    public int OverloadSerial => overloadSerial;
    /// <summary>Client: the current angle of the plate picture, for effects aimed at the cells.</summary>
    public float PlateAngle => plateRenderer?.Angle ?? 0;
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
        FindRecipe();
        if (api.Side == EnumAppSide.Server)
        {
            signalMod = api.ModLoader?.GetModSystem<SignalNetworkMod>();
            signalMod?.RegisterSignalTickListener(OnSignalTick);
            RegisterGameTickListener(OnMechanicsTick, 100);
        }
        else if (api is ICoreClientAPI capi && Block is BlockCraftingMachine)   // not when the block is already gone
        {
            plateRenderer = new PlateRenderer(capi, Pos, PlateMesh(capi), PartMesh(capi, "crystal"), PartMesh(capi, "door-west"), PartMesh(capi, "door-east"), ((BlockCraftingMachine)Block).RotationRadians);
            SlotsChanged += RefreshPlateItems;
            RefreshPlateItems();
            UpdatePlatePicture();
            effects = new MachineEffects(capi, this);
            RegisterGameTickListener(dt => effects.Tick(dt), 50);
        }
    }

    public override void OnBlockUnloaded() { base.OnBlockUnloaded(); signalMod?.DisposeSignalTickListener(OnSignalTick); plateRenderer?.Dispose(); effects?.Dispose(); }
    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        signalMod?.DisposeSignalTickListener(OnSignalTick);
        plateRenderer?.Dispose();
        effects?.Dispose();
        if (Lit) Api.World.BlockAccessor.RemoveBlockLight(BlockCraftingMachine.CrystalLight, Pos);
    }

    // ---- mechanics: clutch and plate

    private void RunProcess(float dt)
    {
        bool doorsClosed = !DoorOpen(4) && !DoorOpen(5);
        var sense = new Sense(inputs, doorsClosed, OccupiedCells, Recipe != null, inventory[ProductSlot].Empty, networkSpeed > PlateDrive.Still, PlateRunning);
        var ev = process.Step(sense, dt);
        if (ev == MachineProcess.Event.Craft && Recipe != null)
            MachineRecipes.Craft(Api.World, Recipe, GridCells(), GridSize, inventory[ProductSlot], stack => Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(.5, 1.5, .5)));
        else if (ev == MachineProcess.Event.Overload) { Burn(); overloadSerial++; }
        DrainStability(doorsClosed);
        if (process.State != state) { state = process.State; MarkDirty(); }
        else if (state == MachineProcess.Crafting && (int)(process.Progress * 10) % 5 == 0) MarkDirty();   // progress for the info box, twice a second
    }

    /// <summary>Overload: whatever lies on the plate turns to dust, one pile per occupied cell.</summary>
    private void Burn()
    {
        var dust = Api.World.GetItem(new AssetLocation("signalsmachines:burntdust"));
        for (int i = 0; i < GridSlots; i++)
        {
            if (inventory[i].Empty) continue;
            inventory[i].Itemstack = dust == null ? null : new ItemStack(dust);
            inventory[i].MarkDirty();
        }
    }

    private void DrainStability(bool doorsClosed)
    {
        bool leak = !doorsClosed && inputs.Crystal > 0 && inputs.Strength > 0;
        bool craft = state == MachineProcess.Crafting;
        if (!leak && !craft) return;
        float range = leak ? LeakRange : CraftDrainRange;
        var centre = Pos.ToVec3d().Add(.5, 1.5, .5);
        foreach (var player in Api.World.GetPlayersAround(centre, range, range) ?? Array.Empty<IPlayer>())
        {
            var stability = player.Entity?.GetBehavior<Vintagestory.GameContent.EntityBehaviorTemporalStabilityAffected>();
            if (stability == null) continue;
            double distance = player.Entity.Pos.DistanceTo(centre);
            float drain = leak ? LeakDrainPerTick * (float)Math.Max(0, 1 - distance / LeakRange) : CraftDrainPerTick;
            stability.OwnStability = Math.Max(0, stability.OwnStability - drain);
        }
    }

    /// <summary>Server, every 0.1 s: the plate follows the network speed through the clutch, with inertia.</summary>
    public void OnMechanicsTick(float dt)
    {
        networkSpeed = SpeedSource();
        float target = inputs.Clutch > 0 ? networkSpeed : 0;
        plateSpeed = PlateDrive.Step(plateSpeed, target, dt);
        RunProcess(dt);
        // clients integrate the angle themselves; send the speed when it moved, or once a second while turning
        double now = Api.World.ElapsedMilliseconds;
        if (Math.Abs(plateSpeed - syncedPlateSpeed) > .005f || (plateSpeed > 0 && now - lastPlateSync > 1000))
        {
            syncedPlateSpeed = plateSpeed;
            lastPlateSync = now;
            MarkDirty();
        }
    }

    private byte[] savedSimState;   // from the save game, applied once the program is loaded

    private void LoadProgram()
    {
        program = tube == null || Api?.Side != EnumAppSide.Server ? null : TubeProgram.Get(tube, Api);
        sim = program == null ? null : new CircuitSimulator(program, id => ProgramStore.Of(Api)?.Get(id));
        if (sim != null && savedSimState != null) sim.LoadState(savedSimState);
        savedSimState = null;
    }

    // ---- signals

    private void OnSignalTick()
    {
        var beh = GetBehavior<BEBehaviorSignalConnector>();
        if (beh == null) return;
        byte Node(int i) => beh.GetNodeAt(new NodePos(Pos, i))?.value ?? 0;
        var next = sim == null ? Read(Node) : RunTube(Node);
        if (next != inputs)
        {
            foreach (int pin in new[] { 4, 5 })
                if (inputs[pin] == 0 && next[pin] > 0) doorOpenedAt[pin - 4] = Api.World.ElapsedMilliseconds;
            bool wasLit = Lit;
            inputs = next;
            if (Lit != wasLit) Relight();
            MarkDirty();
        }
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
        if (placerUid != null && player.PlayerUID == placerUid) placerTraits = TraitsOf(player);   // the placer's class may have changed
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
        tree.SetFloat("progress", process.Progress);
        tree.SetInt("overloads", overloadSerial);
        if (sim != null) tree.SetBytes("simState", sim.SaveState()); else tree.RemoveAttribute("simState");
        tree.SetBool("tubeControls", program != null);
        tree.SetFloat("plateSpeed", plateSpeed);
        tree.SetFloat("networkSpeed", networkSpeed);
        if (placerUid != null) tree.SetString("placer", placerUid);
        if (placerName != null) tree.SetString("placerName", placerName);
        if (placerTraits != null) tree.SetString("placerTraits", string.Join(",", placerTraits));
        tree.SetString("recipe", recipeText);
        var product = Recipe?.Recipe.Output?.ResolvedItemStack;
        if (product != null) tree.SetItemstack("product", product); else tree.RemoveAttribute("product");
    }

    private bool tubeControlsSynced;   // client copy of TubeControls
    private string recipeNameSynced = "";   // client copy of the recipe name
    private float progressSynced;
    private ItemStack productSynced;   // client copy of what the recipe makes, for the ghost
    private string ghostCode;          // what the renderer's ghost was built from

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        tube = tree.GetItemstack("programTube");
        tube?.ResolveBlockOrItem(worldForResolving);
        savedSimState = tree.GetBytes("simState");
        var b = tree.GetBytes("inputs", new byte[5]);
        bool wasLit = Lit;
        inputs = new MachineInputs(b[0], b[1], b[2], b[3], b[4]);
        if (Api is ICoreClientAPI && Lit != wasLit) Relight();   // the client lights its own chunks
        state = (byte)tree.GetInt("state");
        progressSynced = tree.GetFloat("progress");
        overloadSerial = tree.GetInt("overloads");
        if (Api?.Side == EnumAppSide.Server || Api == null) process.Restore(state, progressSynced);
        tubeControlsSynced = tree.GetBool("tubeControls");
        plateSpeed = tree.GetFloat("plateSpeed");
        networkSpeed = tree.GetFloat("networkSpeed");
        placerUid = tree.GetString("placer");
        placerName = tree.GetString("placerName");
        placerTraits = tree.HasAttribute("placerTraits") ? new HashSet<string>(tree.GetString("placerTraits").Split(',', StringSplitOptions.RemoveEmptyEntries)) : null;
        recipeNameSynced = tree.GetString("recipe", "");
        productSynced = tree.GetItemstack("product");
        productSynced?.ResolveBlockOrItem(worldForResolving);
        UpdatePlatePicture();
        if (Api is ICoreClientAPI)
        {
            RebuildMesh();
            RefreshPlateItems();
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
        dsc.AppendLine(Lang.Get("signalsmachines:machine-state", state) + " (" + Lang.Get("signalsmachines:machine-state-" + state) + ")");
        if (state == MachineProcess.Crafting && cellsForProgress() > 0)
            dsc.AppendLine(Lang.Get("signalsmachines:machine-progress", (int)(100 * (Api?.Side == EnumAppSide.Server ? process.Progress : progressSynced) / process.Duration(cellsForProgress()))));
        int cellsForProgress() => OccupiedCells;
        dsc.AppendLine(Lang.Get("signalsmachines:machine-drive", (int)(networkSpeed * 100), (int)(plateSpeed * 100)));
        dsc.AppendLine(Lang.Get("signalsmachines:machine-doors", DoorOpen(4) ? Lang.Get("signalsmachines:door-open") : Lang.Get("signalsmachines:door-closed"), DoorOpen(5) ? Lang.Get("signalsmachines:door-open") : Lang.Get("signalsmachines:door-closed")));
        int cells = OccupiedCells;
        if (cells > 0 || !inventory[ProductSlot].Empty) dsc.AppendLine(Lang.Get("signalsmachines:machine-cells", cells, inventory[ProductSlot].Empty ? "-" : inventory[ProductSlot].GetStackName()));
        string recipeName = Api?.Side == EnumAppSide.Server ? recipeText : recipeNameSynced;
        if (cells > 0) dsc.AppendLine(Lang.Get("signalsmachines:machine-recipe", recipeName.Length == 0 ? Lang.Get("signalsmachines:recipe-none") : recipeName));
        if (placerName != null) dsc.AppendLine(Lang.Get("signalsmachines:machine-placer", placerName));
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
        plateRenderer.CrystalDown = inputs.Crystal > 0;
        plateRenderer.WestDoor.Set(DoorOpen(4));
        plateRenderer.EastDoor.Set(DoorOpen(5));
        // the ghost of the product: rebuilt only when the recipe's output changes, shown while crafting
        string code = productSynced?.Collectible?.Code?.ToString();
        if (code != ghostCode) { plateRenderer.SetGhost(productSynced); ghostCode = code; }
        int cells = OccupiedCells;
        plateRenderer.Materialised = state == MachineProcess.Crafting && cells > 0 ? progressSynced / process.Duration(cells) : 0;
    }

    private MeshData PartMesh(ICoreClientAPI capi, string part)
    {
        var shape = capi.Assets.Get(new AssetLocation("signalsmachines", "shapes/block/craftingmachine-" + part + ".json")).ToObject<Shape>();
        capi.Tesselator.TesselateShape(Block, shape, out MeshData mesh);
        return mesh;
    }

    private MeshData PlateMesh(ICoreClientAPI capi)
    {
        // unrotated: the renderer turns plate and contents together by block rotation and running angle
        var shape = capi.Assets.Get(new AssetLocation("signalsmachines", "shapes/block/craftingmachine-plate.json")).ToObject<Shape>();
        capi.Tesselator.TesselateShape(Block, shape, out MeshData mesh);
        return mesh;
    }

    private void RefreshPlateItems()
    {
        if (plateRenderer == null) return;
        for (int i = 0; i <= ProductSlot; i++) plateRenderer.SetStack(i, inventory[i].Itemstack);
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
