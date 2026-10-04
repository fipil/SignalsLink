using signals.src;
using signals.src.signalNetwork;
using SignalsTubes.src.circuit;
using SignalsTubes.src.programtube;
using SignalsTubes.src.socket;
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
public class BECraftingMachine : BlockEntityContainer, ISidedAutomation, SignalsTubes.src.socket.ITubeSocket
{
    public const int PinCount = 8, StatePin = 0, FirstInput = 1, LastInput = 5;
    /// <summary>The socket sits where the socket block's would, moved 4 units towards the model's south.</summary>
    public static readonly Vec3f SocketShift = new(0, 0, 4);
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

    /// <summary>The crystal is down and powered: the machine casts block light (BlockCraftingMachine.GetLightHsv).</summary>
    public bool Lit => inputs.Crystal > 0 && inputs.Strength > 0;

    // Lighting up: exchanging the block for itself makes the world place GetLightHsv anew (the lantern's way).
    // Going dark the exchange sees no light on either side, so the light must be taken away by hand.
    private void Relight()
    {
        if (Api == null) return;
        if (Lit) Api.World.BlockAccessor.ExchangeBlock(Block.Id, Pos);
        else Api.World.BlockAccessor.RemoveBlockLight(BlockCraftingMachine.CrystalLight, Pos);
    }

    // server: when pins 4/5 last went high / low; a door never seen closing counts as long shut
    private readonly long[] doorOpenedAt = new long[2], doorClosedAt = { long.MinValue / 2, long.MinValue / 2 };

    /// <summary>The door is back in its frame: pin low and the closing ride (slide + push) over. Opening breaks it at once.</summary>
    public bool DoorShut(int pin) =>
        !DoorOpen(pin) && Api.World.ElapsedMilliseconds - doorClosedAt[pin - 4] >= (DoorMotion.CloseSlideSeconds + DoorMotion.PushSeconds) * 1000;

    private int doorsReady;   // bit 0 = pin 4, bit 1 = pin 5; the server works it out, the client gets it synced

    /// <summary>The door has finished its ride down; closing shuts access the moment the pin drops.</summary>
    public bool DoorFullyOpen(int pin) => Api?.Side == EnumAppSide.Client
        ? (doorsReady >> (pin - 4) & 1) != 0
        : DoorOpen(pin) && Api.World.ElapsedMilliseconds - doorOpenedAt[pin - 4] >= (DoorMotion.PushSeconds + DoorMotion.OpenSlideSeconds) * 1000;

    // server, every mechanics tick: tell the clients when a door becomes usable or stops being so
    private void TrackDoors()
    {
        int now = (DoorFullyOpen(4) ? 1 : 0) | (DoorFullyOpen(5) ? 2 : 0);
        if (now != doorsReady) { doorsReady = now; MarkDirty(); }
    }

    /// <summary>Nothing lands on or leaves a turning plate: clutch open and the plate at rest.</summary>
    public bool PlateStill => inputs.Clutch == 0 && plateSpeed == 0;

    /// <summary>A hand reaches the cells on the same terms as automation: some door fully open, plate still.</summary>
    public bool HandAccess => PlateStill && (DoorFullyOpen(4) || DoorFullyOpen(5));

    /// <summary>
    /// Hand on a cell (0-8) or the product (9) through an open door: a held stack goes in, one piece or
    /// the whole stack when sneaking; an empty hand takes everything the cell holds. Server moves the items.
    /// </summary>
    public bool HandleCell(IPlayer player, int slot)
    {
        if (!HandAccess || slot < 0 || slot > ProductSlot) return false;
        var hand = player.InventoryManager.ActiveHotbarSlot;
        var cell = inventory[slot];
        bool put = !hand.Empty && slot != ProductSlot && cell.CanHold(hand);
        bool take = hand.Empty && !cell.Empty;
        if (!put && !take) return false;
        if (Api.Side == EnumAppSide.Client) return true;
        if (put)
        {
            int count = player.Entity.Controls.Sneak ? hand.StackSize : 1;
            if (hand.TryPutInto(Api.World, cell, count) == 0) return false;
        }
        else
        {
            var taken = cell.TakeOutWhole();
            if (!player.InventoryManager.TryGiveItemstack(taken, true)) Api.World.SpawnItemEntity(taken, Pos.ToVec3d().Add(.5, 1.5, .5));
        }
        cell.MarkDirty();
        Api.World.PlaySoundAt(new AssetLocation("sounds/player/build"), Pos.X + .5, Pos.Y + 1.3, Pos.Z + .5, player, true, 12, .5f);
        return true;
    }

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
    private TubeHost host;   // runs the tube's program (server)
    private SignalNetworkMod signalMod;
    private MeshData tubeMesh;
    private MachineInputs inputs;
    private byte state;
    private readonly MachineProcess process = new();
    public MachineProcess Process => process;
    /// <summary>Temporal stability taken from players per 0.1 s: a whisper while crafting, a blast through an open door (contract 23, 23a).</summary>
    public const float CraftDrainPerTick = 0.0005f, LeakDrainPerTick = 0.01f, CraftDrainRange = 2f, LeakRange = 8f;
    /// <summary>Reaching into the open chamber with the crystal lit: a steep extra drain right at the door.</summary>
    public const float DoorwayDrainPerTick = 0.05f, DoorwayRange = 1.5f;
    private BEBehaviorMPConsumer mpc;
    private PlateRenderer plateRenderer;
    private MachineEffects effects;
    private int overloadSerial;
    /// <summary>Counts overloads; the client plays the bang when it changes.</summary>
    public int OverloadSerial => overloadSerial;
    /// <summary>Client: the current angle of the plate picture, for effects aimed at the cells.</summary>
    public float PlateAngle => plateRenderer?.Angle ?? 0;
    /// <summary>Client: how much of its cell the drawn item takes (cell units); null without a picture.</summary>
    public Vec3f CellExtent(int slot) => plateRenderer?.Extent(slot);
    private float plateSpeed, syncedPlateSpeed, networkSpeed;
    private double lastPlateSync;
    /// <summary>Speed of the mechanical network at the axle; tests substitute it.</summary>
    public Func<float> SpeedSource;

    public bool HasTube => tube != null;
    public ItemStack Tube => tube;
    /// <summary>A programmed tube sits in the socket and drives the inputs.</summary>
    public bool TubeControls => host?.Controls == true;
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
            plateRenderer = new PlateRenderer(capi, Pos, PlateMesh(capi), PartMesh(capi, "crystal"), PartMesh(capi, "door-west"), PartMesh(capi, "door-east"),
                PartMesh(capi, "door-west-rods"), PartMesh(capi, "door-east-rods"), PartMesh(capi, "door-west-glass"), PartMesh(capi, "door-east-glass"),
                ((BlockCraftingMachine)Block).RotationRadians);
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
        // the process waits for the doors to finish closing (the crystal must not light while they still slide);
        // the leak below follows the pins alone, as the doors are no longer open then
        bool doorsClosed = !DoorOpen(4) && !DoorOpen(5);
        var sense = new Sense(inputs, DoorShut(4) && DoorShut(5), OccupiedCells, Recipe != null, inventory[ProductSlot].Empty, networkSpeed > PlateDrive.Still, PlateRunning);
        var ev = process.Step(sense, dt);
        if (ev == MachineProcess.Event.Craft && Recipe != null)
            MachineRecipes.Craft(Api.World, Recipe, GridCells(), GridSize, inventory[ProductSlot], stack => Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(.5, 1.5, .5)), Pos.ToVec3d().Add(.5, 1.5, .5));
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
            if (leak && distance < DoorwayRange) drain += DoorwayDrainPerTick * (float)(1 - distance / DoorwayRange);   // standing in the doorway
            stability.OwnStability = Math.Max(0, stability.OwnStability - drain);
        }
    }

    /// <summary>Server, every 0.1 s: the plate follows the network speed through the clutch, with inertia.</summary>
    public void OnMechanicsTick(float dt)
    {
        networkSpeed = SpeedSource();
        float target = inputs.Clutch > 0 ? networkSpeed : 0;
        plateSpeed = PlateDrive.Step(plateSpeed, target, dt);
        TrackDoors();
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

    private byte[] savedSimState;

    private void LoadProgram()
    {
        if (Api == null) return;
        host ??= new TubeHost(Api);
        if (savedSimState != null) { host.RestoreState(savedSimState); savedSimState = null; }
        host.Load(tube);
    }

    // ---- signals

    private void OnSignalTick()
    {
        var beh = GetBehavior<BEBehaviorSignalConnector>();
        if (beh == null) return;
        byte Node(int i) => TubeHost.PinLevel(beh, Pos, i);
        var next = TubeControls ? RunTube(Node) : Read(Node);
        if (next != inputs)
        {
            // the first change after a tube went in or out is the operator's doing, not a surge
            if (forgiveNextChange) { process.Forgive(); forgiveNextChange = false; }
            foreach (int pin in new[] { 4, 5 })
            {
                if (inputs[pin] == 0 && next[pin] > 0) doorOpenedAt[pin - 4] = Api.World.ElapsedMilliseconds;
                if (inputs[pin] > 0 && next[pin] == 0) doorClosedAt[pin - 4] = Api.World.ElapsedMilliseconds;
            }
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
        host.Step(node, IsMachineInput);
        return new(host.Output(1), host.Output(2), host.Output(3), host.Output(4), host.Output(5));
    }

    // ---- socket (also an imprinter's target: the plug goes in while no tube sits here)

    private BlockPos imprinter;
    public BlockPos Imprinter => imprinter;

    public void SetImprinter(BlockPos pos)
    {
        imprinter = pos?.Copy();
        RebuildMesh();
        MarkDirty(true);
    }

    public ItemStack TakeTube()
    {
        var taken = tube;
        tube = null;
        LoadProgram();
        forgiveNextChange = true;
        MarkDirty(true);
        return taken;
    }

    private bool forgiveNextChange;   // set when the tube goes in or out; the first input change after that is not judged for steepness

    public string Diagnostics() => host == null ? null : host.Describe() + $"machine: inputs {inputs}, state {state}, tube controls {TubeControls}\n";

    /// <summary>The imprinter prefills pin names with these; reserve pins 6-7 stay unnamed.</summary>
    public string PinName(int index) => index <= LastInput ? (Block as BlockCraftingMachine)?.PinName(index) : null;

    public Vec3d PlugCableEnd()
    {
        var local = new Cuboidf(8 / 16f, 7 / 16f, 12 / 16f, 8 / 16f, 7 / 16f, 12 / 16f)
            .RotatedCopy(0, ((BlockCraftingMachine)Block).RotationDegrees, 0, new Vec3d(.5, .5, .5));
        return Pos.ToVec3d().Add(local.MidX, local.MidY, local.MidZ);
    }

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
        forgiveNextChange = true;   // whatever the new controller starts with is not a surge
        MarkDirty(true);
        Api.World.PlaySoundAt(new AssetLocation("signalsmachines:sounds/tube-click"), Pos.X + .5, Pos.Y + .15, Pos.Z + .5, null, false, 12, .65f);
        return true;
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        if (tube != null) tree.SetItemstack("programTube", tube);
        else tree.RemoveAttribute("programTube");
        if (imprinter != null) tree.SetBytes("imprinter", Vintagestory.API.Util.SerializerUtil.Serialize(imprinter)); else tree.RemoveAttribute("imprinter");
        tree.SetBytes("inputs", new[] { inputs.Clutch, inputs.Crystal, inputs.Strength, inputs.DoorWest, inputs.DoorEast });
        tree.SetInt("state", state);
        tree.SetInt("doorsReady", doorsReady);
        tree.SetFloat("progress", process.Progress);
        tree.SetInt("overloads", overloadSerial);
        if (host?.SaveState() is byte[] simState) tree.SetBytes("simState", simState); else tree.RemoveAttribute("simState");
        tree.SetBool("tubeControls", TubeControls);
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
        imprinter = tree.HasAttribute("imprinter") ? Vintagestory.API.Util.SerializerUtil.Deserialize<BlockPos>(tree.GetBytes("imprinter")) : null;
        savedSimState = tree.GetBytes("simState");   // handed to the host once it exists (the tree may come before Initialize)
        var b = tree.GetBytes("inputs", new byte[5]);
        bool wasLit = Lit;
        inputs = new MachineInputs(b[0], b[1], b[2], b[3], b[4]);
        if (Api is ICoreClientAPI && Lit != wasLit) Relight();   // the client lights its own chunks
        state = (byte)tree.GetInt("state");
        doorsReady = tree.GetInt("doorsReady");   // the server recomputes its own from the clock next tick
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
        // aimed at the tube in the socket: the tube and nothing else; elsewhere the machine alone
        var sel = forPlayer?.Entity?.BlockSelection;
        if (tube != null && sel != null && sel.Position.Equals(Pos) && sel.SelectionBoxIndex == BlockCraftingMachine.SocketBox)
        {
            dsc.Append(ItemProgramTube.FullInfo(tube, Api.World).TrimStart('\r', '\n'));
            return;
        }
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
    }

    // ---- mesh

    private void RebuildMesh()
    {
        tubeMesh = null;
        if (Api is not ICoreClientAPI capi || Block is not BlockCraftingMachine block) return;
        if (tube?.Collectible is ItemProgramTube item)
        {
            MeshData mesh = item.BuildMesh(capi, tube, capi.Tesselator.GetTextureSource(Block));
            mesh.Scale(new Vec3f(), .5f, .5f, .5f);
            mesh.Translate(4f / 16, 1f / 16, 8f / 16);
            mesh.Rotate(new Vec3f(.5f, .5f, .5f), 0, block.RotationRadians, 0);
            tubeMesh = mesh;
        }
        else if (imprinter != null) tubeMesh = PlugMesh(capi, block);
    }

    // The imprinter's plug head, borrowed from the socket block's shape and moved onto this socket.
    private static MeshData PlugMesh(ICoreClientAPI capi, BlockCraftingMachine block)
    {
        var socketBlock = capi.World.GetBlock(new AssetLocation("signalstubes:tubesocket-north-down"));
        if (socketBlock == null) return null;
        var shape = capi.Assets.Get(new AssetLocation("signalstubes", "shapes/block/tubesocket.json")).ToObject<Shape>().Clone();
        shape.Elements = shape.Elements.Where(e => e.Name.StartsWith("plug_")).ToArray();
        capi.Tesselator.TesselateShape(socketBlock, shape, out MeshData mesh);
        mesh.Translate(SocketShift.X / 16, SocketShift.Y / 16, SocketShift.Z / 16);
        mesh.Rotate(new Vec3f(.5f, .5f, .5f), 0, block.RotationRadians, 0);
        return mesh;
    }

    // The client turns the plate at the synced speed while the clutch is closed and brakes it into the
    // home position itself once it opens, so the picture always comes to rest aligned with the grid.
    private void UpdatePlatePicture()
    {
        if (plateRenderer == null) return;
        if (inputs.Clutch > 0 && networkSpeed > PlateDrive.Still) plateRenderer.Drive(plateSpeed);
        else plateRenderer.Release();
        plateRenderer.CrystalDown = inputs.Crystal > 0;
        // full glow at the strength the batch asks for (or anything above it); without a batch at 9
        int wanted = OccupiedCells > 0 ? OccupiedCells : 9;
        plateRenderer.CrystalGlow = inputs.Strength == 0 ? 0 : Math.Min(1f, (float)inputs.Strength / wanted);
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
