using signals.src;
using signals.src.signalNetwork;
using SignalsTubes.src.programtube;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SignalsTubes.src.copier;

/// <summary>Green socket: the original. Takes programmed tubes; not pulled out while a copy is running.</summary>
public class ItemSlotCopyIn : ItemSlot
{
    private readonly Func<bool> busy;
    public ItemSlotCopyIn(InventoryBase inventory, Func<bool> busy) : base(inventory) { this.busy = busy; MaxSlotStackSize = 1; }
    public override bool CanHold(ItemSlot source) => source?.Itemstack?.Collectible is ItemProgramTube && !TubeProgram.IsBlank(source.Itemstack) && base.CanHold(source);
    public override bool CanTake() => Itemstack != null && !busy();
}

/// <summary>Red socket: the blank that becomes the copy. Automation may take it only once it is written.</summary>
public class ItemSlotCopyOut : ItemSlot
{
    public ItemSlotCopyOut(InventoryBase inventory) : base(inventory) { MaxSlotStackSize = 1; }
    public override bool CanHold(ItemSlot source) => source?.Itemstack?.Collectible is ItemProgramTube && TubeProgram.IsBlank(source.Itemstack) && base.CanHold(source);
    public override bool CanTake() => Itemstack != null && !TubeProgram.IsBlank(Itemstack);
}

/// <summary>
/// Copies the program of the tube in the green socket onto the blank in the red one. The two sockets
/// are a two-slot inventory for chutes and flaps; players use the sockets directly. Automation may take
/// the original any time a copy is not running and the copy once it is written. Charged by temporal
/// gears; a copy costs 10 points plus one per part. A start pulse on pin 0 begins a copy, pin 1 reports
/// the state. Copy-locked tubes are copied only by their author or a copier the author placed.
/// </summary>
public class BETubeCopier : BlockEntityContainer
{
    public const int In = 0, Out = 1;
    public const int ChargePerGear = 1000, MaxCharge = 5000, BaseCost = 10;
    public const float CopySeconds = 5;
    public const byte StateIdle = 0, StateCopying = 1, StateDone = 2, StateNoInput = 10, StateNoBlank = 11, StateLocked = 12, StateNoCharge = 13;

    private readonly InventoryGeneric inventory;
    private string placerUid;
    private int charge, copies;
    private byte state;
    private float progress;    // seconds into the current copy
    private byte lastStart;
    private SignalNetworkMod signalMod;
    private ILoadedSound workSound;   // client loop while copying
    private static readonly Dictionary<string, MeshData> meshCache = new();

    public override InventoryBase Inventory => inventory;
    public override string InventoryClassName => "tubecopier";
    public ItemStack Original => inventory[In].Itemstack;
    public ItemStack Target => inventory[Out].Itemstack;
    public byte State => state;
    public int Charge => charge;

    public BETubeCopier()
    {
        inventory = new InventoryGeneric(2, null, null, (id, inv) => id == In ? new ItemSlotCopyIn(inv, () => state == StateCopying) : new ItemSlotCopyOut(inv));
        inventory.SlotModified += _ => { OnSlotsChanged(); };
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server)
        {
            signalMod = api.ModLoader.GetModSystem<SignalNetworkMod>();
            signalMod.RegisterSignalTickListener(OnSignalTick);
            RegisterGameTickListener(ServerTick, 250);
        }
    }

    public override void OnBlockPlaced(ItemStack byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);
    }

    public void SetPlacer(IPlayer player) { placerUid = player?.PlayerUID; MarkDirty(); }

    public override void OnBlockUnloaded() { base.OnBlockUnloaded(); signalMod?.DisposeSignalTickListener(OnSignalTick); StopSound(); }
    public override void OnBlockRemoved() { base.OnBlockRemoved(); signalMod?.DisposeSignalTickListener(OnSignalTick); StopSound(); }

    private void UpdateSound()
    {
        if (Api is not ICoreClientAPI capi) return;
        if (state != StateCopying) { StopSound(); return; }
        if (workSound != null) return;
        workSound = capi.World.LoadSound(new SoundParams {
            Location = new AssetLocation("signalstubes:sounds/copier-work.ogg"),
            Position = Pos.ToVec3f().Add(.5f, .6f, .5f),
            ShouldLoop = true, DisposeOnFinish = false, Range = 12, Volume = .6f
        });
        workSound?.Start();
    }

    private void StopSound() { workSound?.Stop(); workSound?.Dispose(); workSound = null; }

    private void OnSlotsChanged()
    {
        if (Api?.Side != EnumAppSide.Server) return;
        state = StateIdle; progress = 0;   // any change of the sockets ends the previous run or clears its error
        MarkDirty(true);
    }

    // ---- copying

    /// <summary>Why a copy cannot start now, or StateIdle when it can.</summary>
    public byte Check(string byPlayerUid = null)
    {
        var original = Original;
        if (original == null || TubeProgram.IsBlank(original)) return StateNoInput;
        var target = Target;
        if (target == null || !TubeProgram.IsBlank(target)) return StateNoBlank;
        // Soldered-in tubes belong to other authors: a composite holding any is never copied, not even by its own author.
        if (TubeProgram.Soldered(original, Api?.World).Count > 0) return StateLocked;
        if (TubeProgram.LockCopy(original) && !TubeProgram.IsAuthor(original, byPlayerUid ?? placerUid) && !TubeProgram.IsAuthor(original, placerUid)) return StateLocked;
        if (charge < Cost(original)) return StateNoCharge;
        return StateIdle;
    }

    private int Cost(ItemStack original) => BaseCost + (TubeProgram.Get(original, Api)?.Components.Count ?? 0);

    public bool Start(string byPlayerUid = null)
    {
        if (state == StateCopying) return false;
        byte why = Check(byPlayerUid);
        state = why == StateIdle ? StateCopying : why;
        progress = 0;
        MarkDirty(true);
        return state == StateCopying;
    }

    private void ServerTick(float dt)
    {
        if (state != StateCopying) return;
        progress += dt;
        if (progress < CopySeconds) return;
        Finish();
    }

    private void Finish()
    {
        byte why = Check();
        if (why != StateIdle) { state = why; MarkDirty(true); return; }
        var original = Original;
        var target = Target;
        var copy = new ItemStack(target.Collectible);   // the blank's own colour
        foreach (var key in new[] { TubeProgram.IdKey, TubeProgram.NameKey, TubeProgram.DescriptionKey, TubeProgram.AuthorKey, TubeProgram.AuthorNameKey, "pins", "fingerprint" })
            if (original.Attributes.HasAttribute(key)) copy.Attributes[key] = original.Attributes[key].Clone();
        if (original.Attributes.HasAttribute("circuit")) copy.Attributes["circuit"] = original.Attributes["circuit"].Clone();   // creative samples embed their program
        copy.Attributes.SetBool(TubeProgram.LockCopyKey, TubeProgram.LockCopy(original));
        copy.Attributes.SetBool(TubeProgram.LockViewKey, TubeProgram.LockView(original));
        charge -= Cost(original);
        copies++;
        inventory[Out].Itemstack = copy;
        inventory[Out].MarkDirty();
        state = StateDone;
        progress = 0;
        Api.World.PlaySoundAt(new AssetLocation("signalstubes:sounds/tube-click"), Pos.X + .5, Pos.Y + .7, Pos.Z + .5, null, false, 12, .8f);
        MarkDirty(true);
    }

    public bool AddCharge(ItemSlot slot, IPlayer player)
    {
        if (slot.Itemstack?.Collectible.Code.ToString() != "game:gear-temporal") return false;
        if (charge >= MaxCharge) { Say(player, "copier-full"); return true; }
        if (Api.Side == EnumAppSide.Client) return true;
        slot.TakeOut(1);
        slot.MarkDirty();
        charge = Math.Min(MaxCharge, charge + ChargePerGear);
        Say(player, "copier-charged", charge);
        MarkDirty(true);
        return true;
    }

    private void Say(IPlayer player, string key, params object[] args)
    {
        if (player is Vintagestory.API.Server.IServerPlayer sp)
            sp.SendMessage(GlobalConstants.InfoLogChatGroup, Lang.Get("signalstubes:" + key, args), EnumChatType.Notification);
    }

    // ---- pins: 0 start (read), 1 state (driven)

    private void OnSignalTick()
    {
        var beh = GetBehavior<BEBehaviorSignalConnector>();
        if (beh == null) return;
        byte start = beh.GetNodeAt(new NodePos(Pos, 0))?.value ?? 0;
        if (start > 0 && lastStart == 0 && state != StateCopying) Start();
        lastStart = start;
        beh.UpdateSource(new NodePos(Pos, 1), (byte)Math.Min(state, (byte)15));
    }

    // ---- sockets

    public bool Interact(int slotId, IPlayer player)
    {
        var hand = player.InventoryManager.ActiveHotbarSlot;
        var slot = inventory[slotId];
        if (slot.Empty)
        {
            if (hand.Empty || !slot.CanHold(hand)) return false;
            if (Api.Side == EnumAppSide.Client) return true;
            slot.Itemstack = hand.TakeOut(1);
            hand.MarkDirty(); slot.MarkDirty();
        }
        else
        {
            if (!hand.Empty) return false;
            if (Api.Side == EnumAppSide.Client) return true;
            var taken = slot.TakeOutWhole();
            slot.MarkDirty();
            if (!player.InventoryManager.TryGiveItemstack(taken, true)) Api.World.SpawnItemEntity(taken, Pos.ToVec3d().Add(.5, .5, .5));
        }
        Api.World.PlaySoundAt(new AssetLocation("signalstubes:sounds/tube-click"), Pos.X + .5, Pos.Y + .7, Pos.Z + .5, null, false, 12, .65f);
        return true;
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        if (placerUid != null) tree.SetString("placer", placerUid);
        tree.SetInt("charge", charge);
        tree.SetInt("copies", copies);
        tree.SetInt("state", state);
        tree.SetFloat("progress", progress);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        placerUid = tree.GetString("placer");
        charge = tree.GetInt("charge");
        copies = tree.GetInt("copies");
        state = (byte)tree.GetInt("state");
        progress = tree.GetFloat("progress");
        if (Api is ICoreClientAPI) { MarkDirty(true); UpdateSound(); }
    }

    public override void GetBlockInfo(IPlayer forPlayer, System.Text.StringBuilder dsc)
    {
        dsc.AppendLine(Lang.Get("signalstubes:copier-state-" + state));
        dsc.AppendLine(Lang.Get("signalstubes:copier-status", charge, copies));
        foreach (var stack in new[] { Original, Target })
            if (stack != null && !TubeProgram.IsBlank(stack)) dsc.Append(ItemProgramTube.FullInfo(stack, Api.World));
    }

    // ---- mesh: cabinet with whatever sits in the two sockets

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        mesher.AddMeshData(GetMesh((ICoreClientAPI)Api));
        return true;
    }

    private MeshData GetMesh(ICoreClientAPI capi)
    {
        var block = (BlockTubeCopier)Block;
        string Key(ItemStack s) => s == null ? "" : s.Collectible.Code + ":" + TubeVisuals.MeshKey(s, false);
        string key = $"{block.Code}|{Key(Original)}|{Key(Target)}";
        lock (meshCache)
        {
            if (meshCache.TryGetValue(key, out var cached)) return cached;
            if (meshCache.Count >= 64) meshCache.Clear();
            Shape shape = capi.Assets.Get(new AssetLocation("signalstubes", "shapes/block/tubecopier.json")).ToObject<Shape>().Clone();
            if (Original?.Collectible is ItemProgramTube a) TubeVisuals.Append(shape, a.BuildShape(Original, false), .5f, new Vec3f(0, 10.6f, 4));
            if (Target?.Collectible is ItemProgramTube b) TubeVisuals.Append(shape, b.BuildShape(Target, false), .5f, new Vec3f(8, 10.6f, 4));
            capi.Tesselator.TesselateShape(block, shape, out MeshData mesh, new Vec3f(0, block.Shape.rotateY, 0));
            meshCache[key] = mesh;
            return mesh;
        }
    }
}
