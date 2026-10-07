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
    private static readonly Dictionary<string, MeshData> meshCache = new();

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
    public string RoleOf(int pin)
    {
        var p = tube == null ? null : TubeProgram.Pins(tube).FirstOrDefault(x => x.Index == pin);
        return p == null ? "free" : p.Role switch { PinRole.Input => "in", PinRole.Output => "out", _ => "param" };
    }

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
    }

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        signalMod?.DisposeSignalTickListener(OnSignalTick);
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        signalMod?.DisposeSignalTickListener(OnSignalTick);
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

    public bool Interact(IPlayer player)
    {
        var hand = player.InventoryManager.ActiveHotbarSlot;
        bool insert = tube == null && hand.Itemstack?.Collectible is ItemProgramTube;
        bool remove = tube != null && hand.Empty;
        if (!insert && !remove) return false;
        if (Api.Side == EnumAppSide.Client) return true;  // only the server moves stacks
        if (insert)
        {
            tube = hand.TakeOut(1);
            hand.MarkDirty();
            LoadProgram();
            DropWiresOnHiddenPins();
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

    // Pins the new tube does not have disappear, so wires hanging on them fall off as items.
    private void DropWiresOnHiddenPins()
    {
        var wires = Api.ModLoader.GetModSystem<HangingWiresMod>();
        if (wires == null) return;
        int mask = PinMask;
        int dropped = 0;
        for (int i = 0; i < BlockTubeSocket.PinCount; i++)
        {
            if ((mask >> i & 1) != 0) continue;
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
        if (sim != null) tree.SetBytes("simState", sim.SaveState()); else tree.RemoveAttribute("simState");   // the tube's memory survives a reload
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        tube = tree.GetItemstack(TubeKey);
        tube?.ResolveBlockOrItem(worldForResolving);
        lit = tree.GetBool(LitKey);
        imprinter = tree.HasAttribute("imprinter") ? Vintagestory.API.Util.SerializerUtil.Deserialize<BlockPos>(tree.GetBytes("imprinter")) : null;
        savedSimState = tree.GetBytes("simState");
        if (Api != null) LoadProgram();
        if (Api is ICoreClientAPI) MarkDirty(true);
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        mesher.AddMeshData(GetMesh((ICoreClientAPI)Api));
        return true;
    }

    private MeshData GetMesh(ICoreClientAPI capi)
    {
        var block = (BlockTubeSocket)Block;
        string roles = string.Concat(Enumerable.Range(0, BlockTubeSocket.PinCount).Select(i => RoleOf(i)[0]));
        string key = $"{block.Code}|{roles}|{imprinter != null}|{tube?.Collectible.Code}|{(tube == null ? "" : TubeVisuals.MeshKey(tube, lit))}";
        lock (meshCache)
        {
            if (meshCache.TryGetValue(key, out var cached)) return cached;
            if (meshCache.Count >= 256) meshCache.Clear();
            Shape shape = capi.Assets.Get(new AssetLocation("signalstubes", "shapes/block/tubesocket.json")).ToObject<Shape>().Clone();
            int visible = tube == null ? (1 << BlockTubeSocket.PinCount) - 1 : PinMask;
            var parts = new List<ShapeElement>();
            foreach (var e in shape.Elements)
            {
                if (e.Name.StartsWith("pin_"))
                {
                    int i = e.Name[4] - '0';
                    if ((visible >> i & 1) == 0) continue;
                    if (e.Name.EndsWith("_cap")) PaintTop(e, RoleOf(i));
                }
                parts.Add(e);
            }
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
