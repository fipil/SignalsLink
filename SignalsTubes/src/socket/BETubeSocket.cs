using signals.src;
using signals.src.hangingwires;
using signals.src.signalNetwork;
using SignalsTubes.src.circuit;
using SignalsTubes.src.programtube;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SignalsTubes.src.socket;

/// <summary>
/// Holds one tube and runs its program: every Signals step the input pins are read, the simulator
/// steps once, the output pins are driven. Pins are Signals nodes provided by BEBehaviorSignalConnector;
/// all eight are sources with output 0, so unused and input pins simply follow the wire.
/// </summary>
public class BETubeSocket : BlockEntity, ITubeSocket
{
    public Vec3d PlugCableEnd() => BlockTubeSocket.PlugCableEnd(Api.World.BlockAccessor, Pos);
    private const string TubeKey = "programTube";
    private const string LitKey = "lit";

    private ItemStack tube;
    private CircuitProgram program;
    private CircuitSimulator sim;
    private SignalNetworkMod signalMod;
    private bool lit;   // some pin belongs to a powered network; drives the glyph glow
    private BlockPos imprinter;   // an imprinter's plug sits in the socket
    private string lastProgram;   // id of the program last run here: the same one goes back in without losing the wires
    private static readonly Dictionary<string, MeshData> meshCache = new();

    // client: the insertion preview (what a click would do) and the pins hidden while it shows
    private SocketPreviewRenderer preview;
    private string previewKey;
    private bool previewHidesPins;
    private int previewPins = -1;   // -1: no preview; else the pins drawn solid while it shows (the held tube's on the bed, none while turning)
    private bool previewHeld;       // the tube was just taken out under the player's eyes: no preview until they look away and back
    /// <summary>Client: the turning preview is up, the real pins are out of the picture and out of the selection.</summary>
    public bool PreviewHidesPins => previewHidesPins;
    /// <summary>Client: pins to draw and select while a preview shows; -1 when none shows.</summary>
    public int PreviewPins => previewPins;
    public BlockFacing PreviewOrientation { get; private set; }   // non-null while the turning preview shows
    public ItemStack PreviewTube { get; private set; }

    public bool HasTube => tube != null;
    public BlockPos Imprinter => imprinter;

    public void SetImprinter(BlockPos pos)
    {
        imprinter = pos?.Copy();
        MarkDirty(true);
    }
    public ItemStack Tube => tube;
    // A blank tube still has all eight contacts.
    public int PinMask => tube == null ? 0 : TubeVisuals.PinMask(tube);

    // Roles and names come from the public part of the stack, so the client can show them too.
    public string RoleOf(int pin) => RoleOf(tube, pin);

    public static string RoleOf(ItemStack stack, int pin)
    {
        var p = stack == null ? null : TubeProgram.Pins(stack).FirstOrDefault(x => x.Index == pin);
        return p == null ? "free" : p.Role switch { PinRole.Input => "in", PinRole.Output => "out", _ => "param" };
    }

    public BlockFacing Orientation => SocketOrientation.OrientationOf(Block);
    public BlockFacing Side => SocketOrientation.SideOf(Block);

    /// <summary>Author-given pin name, null when none.</summary>
    public string NameOf(int pin) => tube == null ? null : TubeProgram.Pins(tube).FirstOrDefault(x => x.Index == pin)?.Name;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        tube?.ResolveBlockOrItem(api.World);
        LoadProgram();
        if (api.Side == EnumAppSide.Server)
        {
            signalMod = api.ModLoader.GetModSystem<SignalNetworkMod>();
            signalMod.RegisterSignalTickListener(OnSignalTick);
        }
        else { knownCode = Block?.Code?.ToString(); RegisterGameTickListener(PreviewTick, 50); }
    }

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        signalMod?.DisposeSignalTickListener(OnSignalTick);
        ClearPreview();
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        signalMod?.DisposeSignalTickListener(OnSignalTick);
        ClearPreview();
    }

    // Turned in place (a tube went in facing another way): the block changed under the entity, the wires
    // follow their pins to the new anchors.
    public override void OnExchanged(Block block)
    {
        base.OnExchanged(block);
        Block = block;
        WiresFollowPins();
    }

    private string knownCode;   // client: the variant the wires were last drawn for

    // Client: the wire meshes know nothing of a block turned under them (an incremental rebuild only looks at
    // changed connections), so they are rebuilt whole. Rare enough: once per turned socket.
    private void WiresFollowPins()
    {
        if (Api is not ICoreClientAPI || Block?.Code == null) return;
        string code = Block.Code.ToString();
        if (code == knownCode) return;
        knownCode = code;
        MarkDirty(true);
        Api.ModLoader.GetModSystem<HangingWiresMod>()?.Renderer?.RequestFullRebuild();
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

    private byte[] savedSimState;   // from the save game, applied once the program is loaded

    private void LoadProgram()
    {
        program = tube == null || Api.Side != EnumAppSide.Server ? null : TubeProgram.Get(tube, Api);
        sim = program == null ? null : new CircuitSimulator(program, id => ProgramStore.Of(Api)?.Get(id));
        if (sim != null && savedSimState != null) sim.LoadState(savedSimState);
        savedSimState = null;
    }

    private void OnSignalTick()
    {
        var beh = GetBehavior<BEBehaviorSignalConnector>();
        if (beh == null) return;
        if (sim == null) return;
        foreach (var pin in program.Pins)
            if (pin.Role != PinRole.Output)
                sim.SetInput(pin.Index, beh.GetNodeAt(new NodePos(Pos, pin.Index))?.value ?? 0);
        sim.Step();
        foreach (var pin in program.Pins)
            if (pin.Role == PinRole.Output)
                beh.UpdateSource(new NodePos(Pos, pin.Index), sim.GetOutput(pin.Index));
        bool nowLit = program.Pins.Any(pin => beh.GetNodeAt(new NodePos(Pos, pin.Index))?.netId != null);
        if (nowLit != lit) { lit = nowLit; MarkDirty(true); }
    }

    private void ReleaseOutputs()
    {
        var beh = GetBehavior<BEBehaviorSignalConnector>();
        if (beh == null || program == null) return;
        foreach (var pin in program.Pins)
            if (pin.Role == PinRole.Output) beh.UpdateSource(new NodePos(Pos, pin.Index), 0);
    }

    /// <summary>Server: removes the tube without dropping it (the imprinter solders it into a new one).</summary>
    public ItemStack TakeTube()
    {
        if (tube == null) return null;
        ReleaseOutputs();
        var taken = tube;
        tube = null;
        lit = false;
        LoadProgram();
        MarkDirty(true);
        return taken;
    }

    public bool Interact(IPlayer player, BlockSelection sel)
    {
        var hand = player.InventoryManager.ActiveHotbarSlot;
        bool insert = tube == null && hand.Itemstack?.Collectible is ItemProgramTube;
        bool remove = tube != null && hand.Empty;
        if (!insert && !remove) return false;
        if (Api.Side == EnumAppSide.Client) return true;  // only the server moves stacks
        if (insert)
        {
            var turnTo = ChosenOrientation(sel, hand.Itemstack);   // decided before the hand is emptied
            Insert(hand.TakeOut(1), turnTo);
            hand.MarkDirty();
        }
        else
        {
            ReleaseOutputs();
            ItemStack taken = tube;
            tube = null;
            lit = false;
            LoadProgram();
            if (!player.InventoryManager.TryGiveItemstack(taken, true))
                Api.World.SpawnItemEntity(taken, Pos.ToVec3d().Add(.5, .5, .5));
        }
        MarkDirty(true);
        Api.World.PlaySoundAt(new AssetLocation("signalstubes:sounds/tube-click"), Pos.X + .5, Pos.Y + .15, Pos.Z + .5, null, false, 12, .65f);
        return true;
    }

    /// <summary>
    /// Server: seats a tube, turned so its pins 0-1-2 face <paramref name="turnTo"/> (null keeps the socket as it is).
    /// A different program than the last one run here drops every wire - the old wiring meant something else;
    /// the same program keeps them (he only took it out for a copy). A fresh socket just loses the wires
    /// on pins the tube does not have.
    /// </summary>
    // What makes two tubes "the same program": the stored program's id, or for tubes that carry their circuit
    // themselves (creative samples) the circuit's fingerprint. Blank tubes have neither.
    public static string ProgramIdentity(ItemStack stack) =>
        stack?.Attributes.GetString(TubeProgram.IdKey) ?? TubeProgram.Fingerprint(stack);

    public void Insert(ItemStack stack, BlockFacing turnTo)
    {
        string id = ProgramIdentity(stack);
        bool rewire = lastProgram != null && id != lastProgram;
        if (turnTo != null && turnTo != Orientation) TurnTo(turnTo);
        tube = stack;
        LoadProgram();
        if (rewire) DropWires(_ => true);
        else DropWires(i => (PinMask >> i & 1) == 0);
        lastProgram = id;
        MarkDirty(true);
    }

    /// <summary>
    /// Where a click with <paramref name="held"/> would turn the socket: towards the side of the plate aimed at.
    /// Aiming at the bed itself with the program that was here last means "put it back as it was": null.
    /// </summary>
    public BlockFacing ChosenOrientation(BlockSelection sel, ItemStack held)
    {
        if (sel == null) return null;
        bool bed = sel.SelectionBoxIndex == BlockTubeSocket.BedBox;   // a pin's box counts as the plate it stands on
        bool same = lastProgram != null && ProgramIdentity(held) == lastProgram;
        if (bed && same) return null;
        return SocketOrientation.AimedSide(Side, sel.HitPosition);
    }

    /// <summary>Turns the socket in place to the variant whose pins 0-1-2 face <paramref name="orientation"/>; the entity stays.</summary>
    public void TurnTo(BlockFacing orientation)
    {
        var block = Api.World.GetBlock(Block.CodeWithVariant("orientation", orientation.Code));
        if (block == null || block.Id == Block.Id) return;
        Api.World.BlockAccessor.ExchangeBlock(block.Id, Pos);
        Block = block;
        WiresFollowPins();
        MarkDirty(true);
    }

    // Wires on the chosen pins fall off as items.
    private void DropWires(System.Func<int, bool> onPin)
    {
        var wires = Api.ModLoader.GetModSystem<HangingWiresMod>();
        if (wires == null) return;
        int dropped = 0;
        for (int i = 0; i < BlockTubeSocket.PinCount; i++)
        {
            if (!onPin(i)) continue;
            var pin = new NodePos(Pos, i);
            foreach (var wire in wires.data.connections.Where(w => w.pos1 == pin || w.pos2 == pin).ToList())
                if (wires.TryToRemoveConnection(wire.pos1, wire.pos2)) dropped++;
        }
        var wireItem = Api.World.GetItem(new AssetLocation("signals:el_wire"));
        if (dropped > 0 && wireItem != null)
            Api.World.SpawnItemEntity(new ItemStack(wireItem, dropped), Pos.ToVec3d().Add(.5, .5, .5));
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        if (tube != null) tree.SetItemstack(TubeKey, tube);
        else tree.RemoveAttribute(TubeKey);
        tree.SetBool(LitKey, lit);
        if (imprinter != null) tree.SetBytes("imprinter", Vintagestory.API.Util.SerializerUtil.Serialize(imprinter)); else tree.RemoveAttribute("imprinter");
        if (lastProgram != null) tree.SetString("lastProgram", lastProgram); else tree.RemoveAttribute("lastProgram");
        if (sim != null) tree.SetBytes("simState", sim.SaveState()); else tree.RemoveAttribute("simState");   // the tube's memory survives a reload
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        bool hadTube = tube != null;
        tube = tree.GetItemstack(TubeKey);
        tube?.ResolveBlockOrItem(worldForResolving);
        lit = tree.GetBool(LitKey);
        imprinter = tree.HasAttribute("imprinter") ? Vintagestory.API.Util.SerializerUtil.Deserialize<BlockPos>(tree.GetBytes("imprinter")) : null;
        lastProgram = tree.GetString("lastProgram");
        savedSimState = tree.GetBytes("simState");
        if (Api != null) LoadProgram();
        if (Api is ICoreClientAPI capi)
        {
            // the tube left while the player was looking at the socket (they just pulled it out): a preview popping up
            // at once would read as "is it still in there?" - hold it until the look leaves the socket
            if (hadTube && tube == null && AimedAtByLocalPlayer(capi)) previewHeld = true;
            MarkDirty(true);
            WiresFollowPins();
        }
    }

    private bool AimedAtByLocalPlayer(ICoreClientAPI capi) => capi.World.Player?.CurrentBlockSelection?.Position.Equals(Pos) == true;

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        mesher.AddMeshData(GetMesh((ICoreClientAPI)Api));
        return true;
    }

    private MeshData GetMesh(ICoreClientAPI capi)
    {
        var block = (BlockTubeSocket)Block;
        string roles = string.Concat(Enumerable.Range(0, BlockTubeSocket.PinCount).Select(i => RoleOf(i)[0]));
        string key = $"{block.Code}|{roles}|{imprinter != null}|{previewPins}|{(PreviewTube == null ? "" : TubeVisuals.MeshKey(PreviewTube, false))}|{tube?.Collectible.Code}|{(tube == null ? "" : TubeVisuals.MeshKey(tube, lit))}";
        lock (meshCache)
        {
            if (meshCache.TryGetValue(key, out var cached)) return cached;
            if (meshCache.Count >= 256) meshCache.Clear();
            Shape shape = capi.Assets.Get(new AssetLocation("signalstubes", "shapes/block/tubesocket.json")).ToObject<Shape>().Clone();
            // while a preview shows, the solid pins are the held tube's (on the bed) or none (turning); else the installed tube's or all
            int visible = previewPins >= 0 ? previewPins : tube == null ? (1 << BlockTubeSocket.PinCount) - 1 : PinMask;
            var parts = SocketParts(shape, visible, previewPins >= 0 ? PreviewTube : tube);
            // an imprinter's plug sits in the socket: its own shape, so the socket's inventory picture stays empty
            if (imprinter != null)
                parts.AddRange(capi.Assets.Get(new AssetLocation("signalstubes", "shapes/block/tubesocket-plug.json")).ToObject<Shape>().Clone().Elements);
            shape.Elements = parts.ToArray();
            if (tube?.Collectible is ItemProgramTube item)
                TubeVisuals.Append(shape, item.BuildShape(tube, lit), .5f, new Vec3f(4, 1, 4));
            capi.Tesselator.TesselateShape(block, shape, out MeshData mesh, block.ShapeRotation);
            meshCache[key] = mesh;
            return mesh;
        }
    }

    // The socket's elements with only the given pins, their caps painted by the tube's roles.
    private static List<ShapeElement> SocketParts(Shape shape, int visible, ItemStack forTube)
    {
        var parts = new List<ShapeElement>();
        foreach (var e in shape.Elements)
        {
            if (e.Name.StartsWith("pin_"))
            {
                int i = e.Name[4] - '0';
                if ((visible >> i & 1) == 0) continue;
                if (e.Name.EndsWith("_cap")) PaintTop(e, RoleOf(forTube, i));
            }
            parts.Add(e);
        }
        return parts;
    }

    // ---- client: the insertion preview

    // Every 50 ms: while the local player aims at this empty socket with a tube in hand, show what a click
    // would seat here. On the bed with the program that was here last: just the tube, the real pins stay
    // (it goes back as it was). Anywhere else: pins and tube, turned towards the aimed side, the real pins hidden.
    private void PreviewTick(float dt)
    {
        var capi = (ICoreClientAPI)Api;
        var sel = capi.World.Player?.CurrentBlockSelection;
        var held = capi.World.Player?.InventoryManager.ActiveHotbarSlot?.Itemstack;
        bool looking = sel != null && sel.Position.Equals(Pos);
        if (!looking) previewHeld = false;   // the look left the socket: the next look may preview again
        bool aiming = looking && !previewHeld && tube == null && imprinter == null && held?.Collectible is ItemProgramTube;
        if (!aiming) { ClearPreview(); return; }
        var turnTo = ChosenOrientation(sel, held);
        bool tubeOnly = turnTo == null;
        var orientation = turnTo ?? Orientation;
        string key = $"{orientation.Code}|{tubeOnly}|{held.Collectible.Code}|{TubeVisuals.MeshKey(held, false)}";
        if (key == previewKey) return;
        previewKey = key;
        PreviewOrientation = tubeOnly ? null : orientation;
        PreviewTube = held;
        preview ??= new SocketPreviewRenderer(capi, Pos);
        preview.SetMesh(PreviewMesh(capi, held, orientation, tubeOnly));
        // on the bed the real pins show the held tube's pins, solid and coloured; while turning they are all gone
        int pins = tubeOnly ? TubeVisuals.PinMask(held) : 0;
        previewHidesPins = !tubeOnly;
        if (pins != previewPins) { previewPins = pins; MarkDirty(true); }
        else MarkDirty(true);   // the roles may differ between tubes with the same mask
    }

    private MeshData PreviewMesh(ICoreClientAPI capi, ItemStack held, BlockFacing orientation, bool tubeOnly)
    {
        var variant = capi.World.GetBlock(Block.CodeWithVariant("orientation", orientation.Code)) as BlockTubeSocket ?? (BlockTubeSocket)Block;
        Shape shape = capi.Assets.Get(new AssetLocation("signalstubes", "shapes/block/tubesocket.json")).ToObject<Shape>().Clone();
        // the turning preview shows the pins the tube would use; the "put it back" preview only the tube
        shape.Elements = tubeOnly ? Array.Empty<ShapeElement>() : SocketParts(shape, TubeVisuals.PinMask(held), held).Where(e => e.Name.StartsWith("pin_")).ToArray();
        if (held.Collectible is ItemProgramTube item) TubeVisuals.Append(shape, item.BuildShape(held, false), .5f, new Vec3f(4, 1, 4));
        capi.Tesselator.TesselateShape(variant, shape, out MeshData mesh, variant.ShapeRotation);
        return mesh;
    }

    private void ClearPreview()
    {
        if (preview == null && previewPins < 0) return;
        preview?.Dispose();
        preview = null;
        previewKey = null;
        PreviewOrientation = null;
        PreviewTube = null;
        previewHidesPins = false;
        previewPins = -1;
        MarkDirty(true);
    }

    // Role colour = one cell of the shared pin palette on the cap; a free pin keeps bare metal.
    // Resolved faces hold texture codes without the '#'.
    private static void PaintTop(ShapeElement cap, string role)
    {
        (string texture, float u, float v) = role switch
        {
            "in" => ("pintop", 2, 10),
            "out" => ("pintop", 6, 2),
            "param" => ("pintop", 2, 0),
            _ => ("terminal", 7, 7)
        };
        int up = BlockFacing.UP.Index;
        cap.FacesResolved[up] = new ShapeElementFace { Texture = texture, Uv = new[] { u, v, u + 1, v + 1 }, Enabled = true };
    }
}
