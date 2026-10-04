using SignalsTubes.src.circuit;
using SignalsTubes.src.programtube;
using SignalsTubes.src.schematic;
using SignalsTubes.src.socket;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace SignalsTubes.src.imprint;

/// <summary>
/// Holds the tube to write and two tethered tools: the plug (into a socket, marks the circuit boundary)
/// and the probe (marks switches and delays for pins). Reading and imprinting happen on the server.
/// </summary>
public class BEImprinter : BlockEntity
{
    public const double MaxCable = 24;
    private const int RefusedSlot = 7311, MarkedSlot = 7312;
    public const string PlugCode = "signalstubes:imprinterplug", ProbeCode = "signalstubes:imprinterprobe";

    /// <summary>A tool that lives in the imprinter and may be carried by one player at a time.</summary>
    private sealed class Tool
    {
        public readonly string Code; public readonly string Key;
        public bool Out; public string Holder;
        public Tool(string code, string key) { Code = code; Key = key; }
    }

    private ItemStack tube;
    private BlockPos socket;
    private readonly Tool plug = new(PlugCode, "plug"), probe = new(ProbeCode, "probe");
    private string operatorUid;   // who sees the highlights: the last player to handle a tool
    private readonly HashSet<BlockPos> exposed = new();
    private readonly List<BlockPos> refused = new();
    // What the dialog had typed when it was closed without imprinting; kept until the cable is pulled or the network changes.
    private Newtonsoft.Json.Linq.JObject draft;
    private string draftFingerprint;
    private CableRenderer plugCable, probeCable;
    private static readonly Dictionary<string, MeshData> meshCache = new();

    public bool HasTube => tube != null;
    public ItemStack Tube => tube;
    public BlockPos Socket => socket;
    public bool PlugOut => plug.Out;
    public bool PlugHome => !plug.Out && socket == null;
    public bool ProbeHome => !probe.Out;

    private Vec3d Anchor(float x, float y, float z)
    {
        var local = new Cuboidf(x / 16, y / 16, z / 16, x / 16, y / 16, z / 16)
            .RotatedCopy(0, ((BlockImprinter)Block).RotationDegrees, 0, new Vec3d(.5, .5, .5));
        return Pos.ToVec3d().Add(local.MidX, local.MidY, local.MidZ);
    }
    public Vec3d PlugAnchor() => Anchor(8, 5, 16.6f);
    public Vec3d ProbeAnchor() => Anchor(14.75f, 11.8f, 8);

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        tube?.ResolveBlockOrItem(api.World);
        if (api.Side == EnumAppSide.Server) RegisterGameTickListener(ServerTick, 1000);
        else UpdateCables();
    }

    public override void OnBlockUnloaded() { base.OnBlockUnloaded(); DisposeCables(); }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        DisposeCables();
        if (Api.Side == EnumAppSide.Server) { Revoke(plug); Revoke(probe); Unlink(); ShowHighlights(); }
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

    // ---- server: tools

    private void ServerTick(float dt)
    {
        bool changed = false;
        foreach (var tool in new[] { plug, probe })
            if (tool.Out && Holder(tool) == null) { Revoke(tool); changed = true; }
        if (socket != null && (Api.World.BlockAccessor.GetBlockEntity(socket) is not ITubeSocket || TooFar(socket))) { Unlink(); changed = true; }
        if (socket != null) changed |= Refresh();
        if (changed) MarkDirty(true);
    }

    private bool TooFar(BlockPos pos) => pos.ToVec3d().Add(.5, .5, .5).DistanceTo(PlugAnchor()) > MaxCable;

    private static bool Carries(EntityAgent entity, string code, BlockPos imprinter)
    {
        bool has = false;
        entity?.WalkInventory(slot => { if (ItemImprinterTool.BelongsTo(slot.Itemstack, code, imprinter)) has = true; return !has; });
        return has;
    }

    private IServerPlayer Holder(Tool tool)
    {
        if (tool.Holder == null) return null;
        var player = Api.World.PlayerByUid(tool.Holder) as IServerPlayer;
        if (player?.Entity == null || player.Entity.ServerPos.XYZ.DistanceTo(Pos.ToVec3d()) > MaxCable + 1) return null;
        return Carries(player.Entity, tool.Code, Pos) ? player : null;
    }

    private void Revoke(Tool tool)
    {
        if (tool.Holder != null && Api.World.PlayerByUid(tool.Holder) is IServerPlayer player)
            player.Entity?.WalkInventory(slot =>
            {
                if (ItemImprinterTool.BelongsTo(slot.Itemstack, tool.Code, Pos)) { slot.Itemstack = null; slot.MarkDirty(); }
                return true;
            });
        tool.Out = false;
        tool.Holder = null;
    }

    private bool Give(Tool tool, IServerPlayer player)
    {
        if (tool.Out) return false;
        var item = Api.World.GetItem(new AssetLocation(tool.Code));
        if (item == null) return false;
        if (!player.InventoryManager.TryGiveItemstack(ItemImprinterTool.Create(item, Pos), true)) return false;
        tool.Out = true;
        tool.Holder = player.PlayerUID;
        operatorUid = player.PlayerUID;
        return true;
    }

    public bool TakePlug(IServerPlayer player)
    {
        if (plug.Out) return false;
        Unlink();
        bool ok = Give(plug, player);
        MarkDirty(true);
        return ok;
    }

    public void ReturnPlug() { Revoke(plug); Unlink(); MarkDirty(true); }

    public bool TakeProbe(IServerPlayer player)
    {
        bool ok = Give(probe, player);
        MarkDirty(true);
        return ok;
    }

    public void ReturnProbe() { Revoke(probe); MarkDirty(true); }

    /// <summary>Called by the socket the plug was pushed into.</summary>
    public bool LinkTo(IServerPlayer player, BlockPos socketPos)
    {
        if (!plug.Out || player.PlayerUID != plug.Holder) return false;
        if (TooFar(socketPos)) { Say(player, "imprint-too-far"); return false; }
        if (Api.World.BlockAccessor.GetBlockEntity(socketPos) is not ITubeSocket target) return false;
        Revoke(plug);
        socket = socketPos.Copy();
        target.SetImprinter(Pos);
        operatorUid = player.PlayerUID;
        Refresh();
        MarkDirty(true);
        return true;
    }

    /// <summary>Called by the socket when the plug is pulled out by hand.</summary>
    public void UnplugToHand(IServerPlayer player)
    {
        Unlink();
        Give(plug, player);
        MarkDirty(true);
    }

    private void Unlink()
    {
        if (socket != null && Api.World.BlockAccessor.GetBlockEntity(socket) is ITubeSocket target) target.SetImprinter(null);
        socket = null;
        draft = null;
        exposed.Clear();
        refused.Clear();
        ShowHighlights();
    }

    public bool ToggleExposed(BlockPos pos)
    {
        bool marked = exposed.Add(pos.Copy());
        if (!marked) exposed.Remove(pos);
        Refresh();
        MarkDirty(true);
        return marked;
    }

    // ---- server: reading and imprinting

    private CircuitReader.Result Read(string uid = null) => CircuitReader.Read(new SignalsCircuitWorld(Api), socket, exposed, uid ?? operatorUid, ReservedPins());

    // the pins a machine socket gives a meaning to; exposed switches and delays go to the others
    private ISet<int> ReservedPins()
    {
        var reserved = new HashSet<int>();
        if (socket != null && Api.World.BlockAccessor.GetBlockEntity(socket) is ITubeSocket target)
            for (int i = 0; i < CircuitProgram.MaxPins; i++) if (target.PinName(i) != null) reserved.Add(i);
        return reserved;
    }

    private const string NoWarnKey = "signalstubes-nosolderwarn";
    public static bool WarnsAboutSoldering(IServerPlayer player) => player.GetModdata(NoWarnKey) == null;
    public static void SetSolderWarning(IServerPlayer player, bool on) => player.SetModdata(NoWarnKey, on ? null : new byte[] { 1 });

    public void SetDraft(string json)
    {
        try { draft = Newtonsoft.Json.Linq.JObject.Parse(json); } catch { return; }
        draftFingerprint = socket == null ? null : CircuitFingerprint.Compute(Read().Program);
    }

    /// <summary>Re-reads the network; true when the refused set changed.</summary>
    private bool Refresh()
    {
        if (socket == null) return false;
        var result = Read();
        if (draft != null && CircuitFingerprint.Compute(result.Program) != draftFingerprint) draft = null;
        var now = result.Refusals.Select(r => r.Pos).ToList();
        bool changed = now.Count != refused.Count || now.Zip(refused).Any(p => !p.First.Equals(p.Second));
        refused.Clear();
        refused.AddRange(now);
        ShowHighlights();
        return changed;
    }

    private void ShowHighlights()
    {
        if (operatorUid == null || Api.World.PlayerByUid(operatorUid) is not IServerPlayer player) return;
        // Arbitrary = one highlight per listed block (Cube would read the list as two corners of a box)
        Api.World.HighlightBlocks(player, RefusedSlot, refused.ToList(),
            refused.Select(_ => ColorUtil.ColorFromRgba(220, 40, 40, 96)).ToList(), EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);
        var marked = socket == null ? new List<BlockPos>() : exposed.ToList();
        Api.Logger.Debug("[signalstubes] imprinter {0}: highlights to {1}: {2} refused, {3} marked, socket {4}", Pos, player.PlayerName, refused.Count, marked.Count, socket);
        Api.World.HighlightBlocks(player, MarkedSlot, marked,
            marked.Select(_ => ColorUtil.ColorFromRgba(40, 200, 60, 96)).ToList(), EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);
    }

    /// <summary>Everything the dialog shows, as JSON. Pin and tube names come from the tube already inserted.</summary>
    public string StateJson(IServerPlayer player, bool done, string path = "")
    {
        operatorUid = player.PlayerUID;
        var result = socket == null ? null : Read(player.PlayerUID);
        var existingPins = tube == null ? new List<PublicPin>() : TubeProgram.Pins(tube);
        // a machine socket names its own pins; they come prefilled so the imprinted tube carries them
        var target = socket == null ? null : Api.World.BlockAccessor.GetBlockEntity(socket) as ITubeSocket;
        string PinName(int index) => (string)draft?["pinNames"]?[index.ToString()] ?? existingPins.FirstOrDefault(p => p.Index == index)?.Name ?? target?.PinName(index) ?? "";
        bool foreignLocked = tube != null && !TubeProgram.IsAuthor(tube, player.PlayerUID) && (TubeProgram.LockCopy(tube) || TubeProgram.LockView(tube));
        var state = new Newtonsoft.Json.Linq.JObject
        {
            ["done"] = done,
            ["ok"] = result != null && result.Ok && tube != null && !foreignLocked,
            ["hasTube"] = tube != null,
            ["blank"] = tube != null && TubeProgram.IsBlank(tube),
            ["foreignLocked"] = foreignLocked,
            ["author"] = tube == null ? "" : TubeProgram.AuthorName(tube) ?? "",
            ["lockCopy"] = (bool?)draft?["lockCopy"] ?? (tube != null && TubeProgram.LockCopy(tube)),
            ["lockView"] = (bool?)draft?["lockView"] ?? (tube != null && TubeProgram.LockView(tube)),
            ["linked"] = socket != null,
            ["solder"] = new Newtonsoft.Json.Linq.JArray((result?.Soldered ?? new List<CircuitReader.Soldered>()).Select(s => s.Name)),
            ["warn"] = WarnsAboutSoldering(player),
            ["name"] = (string)draft?["name"] ?? tube?.Attributes.GetString(TubeProgram.NameKey, "") ?? "",
            ["description"] = (string)draft?["description"] ?? tube?.Attributes.GetString(TubeProgram.DescriptionKey, "") ?? "",
            ["parts"] = result == null ? 0 : CircuitSimplifier.Simplify(result.Program).Components.Count,
            ["pins"] = new Newtonsoft.Json.Linq.JArray((result?.Program.Pins ?? new List<Pin>()).OrderBy(p => p.Index).Select(p =>
                new Newtonsoft.Json.Linq.JObject { ["i"] = p.Index, ["r"] = p.Role.ToString().ToLowerInvariant(), ["name"] = PinName(p.Index) })),
            ["adj"] = new Newtonsoft.Json.Linq.JArray((result?.Adjustables ?? new List<CircuitReader.Adjustable>()).Select(a =>
                new Newtonsoft.Json.Linq.JObject { ["x"] = a.Pos.X, ["y"] = a.Pos.Y, ["z"] = a.Pos.Z, ["kind"] = a.Kind.ToString().ToLowerInvariant(), ["marked"] = exposed.Contains(a.Pos) })),
            ["refused"] = new Newtonsoft.Json.Linq.JArray((result?.Refusals ?? new List<CircuitReader.Refusal>()).Select(r =>
                new Newtonsoft.Json.Linq.JObject { ["x"] = r.Pos.X, ["y"] = r.Pos.Y, ["z"] = r.Pos.Z, ["reason"] = r.Reason })),
            ["path"] = path ?? "",
            ["schematic"] = Schematic(player, result, path)
        };
        return Newtonsoft.Json.JsonConvert.SerializeObject(state, Newtonsoft.Json.Formatting.None);
    }

    // The schematic shows the network being imprinted when a socket is plugged in, otherwise what the
    // inserted tube holds. The viewer sees inside soldered tubes only when their author or unlocked.
    private Newtonsoft.Json.Linq.JObject Schematic(IServerPlayer player, CircuitReader.Result result, string path)
    {
        var refs = new Dictionary<string, SchematicLevel.ReferenceInfo>();
        void Gather(ItemStack stack)
        {
            foreach (var item in TubeProgram.Soldered(stack, Api.World))
            {
                string id = item.Attributes.GetString(TubeProgram.IdKey);
                if (id != null && !refs.ContainsKey(id))
                    refs[id] = new SchematicLevel.ReferenceInfo(TubeProgram.Author(item), TubeProgram.LockView(item), item.Attributes.GetString(TubeProgram.NameKey, item.GetName()));
                Gather(item);
            }
        }
        CircuitProgram root; string rootName;
        if (result != null)
        {
            root = CircuitSimplifier.Simplify(result.Program);
            rootName = Lang.Get("signalstubes:schematic-network");
            foreach (var s in result.Soldered)
                if (Api.World.BlockAccessor.GetBlockEntity(s.Pos) is ITubeSocket { HasTube: true } be)
                {
                    string id = be.Tube.Attributes.GetString(TubeProgram.IdKey);
                    if (id != null) refs[id] = new SchematicLevel.ReferenceInfo(TubeProgram.Author(be.Tube), TubeProgram.LockView(be.Tube), s.Name);
                    Gather(be.Tube);
                }
        }
        else if (tube != null && !TubeProgram.IsBlank(tube))
        {
            if (TubeProgram.LockView(tube) && !TubeProgram.IsAuthor(tube, player.PlayerUID))
                return new Newtonsoft.Json.Linq.JObject { ["locked"] = true, ["name"] = tube.GetName() };
            root = TubeProgram.Get(tube, Api);
            rootName = tube.GetName();
            Gather(tube);
            if (root == null) return null;
        }
        else return null;
        var steps = string.IsNullOrEmpty(path) ? Array.Empty<string>() : path.Split(';');
        var store = ProgramStore.Of(Api);
        return SchematicLevel.Build(root, rootName, steps, new SchematicLevel.Context
        {
            Resolve = id => store?.Get(id),
            References = id => refs.TryGetValue(id, out var info) ? info : null,
            ViewerUid = player.PlayerUID
        });
    }

    /// <summary>Takes the dialog's edits (names, exposure) and imprints. True when the tube was written.</summary>
    public bool ApplyAndImprint(IServerPlayer player, string json)
    {
        Newtonsoft.Json.Linq.JObject edits;
        try { edits = Newtonsoft.Json.Linq.JObject.Parse(json); } catch { return false; }
        exposed.Clear();
        foreach (var e in edits["exposed"] as Newtonsoft.Json.Linq.JArray ?? new Newtonsoft.Json.Linq.JArray())
            exposed.Add(new BlockPos((int)e["x"], (int)e["y"], (int)e["z"]));
        var names = edits["pinNames"] as Newtonsoft.Json.Linq.JObject;
        if ((bool?)edits["draft"] == true) { SetDraft(json); return false; }
        if ((bool?)edits["erase"] == true) return Erase(player);
        if ((bool?)edits["noWarn"] == true) SetSolderWarning(player, false);
        if (Clip((string)edits["name"], GuiDialogImprinter.NameMax).Length == 0) { Say(player, "imprint-no-name"); SetDraft(json); return false; }
        bool written = Imprint(player, Clip((string)edits["name"], GuiDialogImprinter.NameMax), Clip((string)edits["description"], GuiDialogImprinter.DescriptionMax),
            index => Clip((string)names?[index.ToString()], GuiDialogImprinter.PinNameMax),
            (bool?)edits["lockCopy"] == true, (bool?)edits["lockView"] == true, (bool?)edits["confirmSolder"] == true);
        if (!written) SetDraft(json);   // a refused imprint must not throw away what was typed
        return written;
    }

    private bool ForeignLocked(IServerPlayer player) =>
        tube != null && !TubeProgram.IsAuthor(tube, player.PlayerUID) && (TubeProgram.LockCopy(tube) || TubeProgram.LockView(tube));

    /// <summary>Back to a blank tube; a locked tube of someone else stays as it is.</summary>
    public bool Erase(IServerPlayer player)
    {
        if (tube == null) { Say(player, "imprint-no-tube"); return false; }
        if (ForeignLocked(player)) { Say(player, "imprint-locked"); return false; }
        // Desoldering: the tubes that were consumed come back.
        foreach (var item in TubeProgram.Soldered(tube, Api.World))
            if (!player.InventoryManager.TryGiveItemstack(item, true)) Api.World.SpawnItemEntity(item, Pos.ToVec3d().Add(.5, 1, .5));
        TubeProgram.Clear(tube);
        MarkDirty(true);
        Say(player, "imprint-erased");
        return true;
    }

    private static string Clip(string s, int max)
    {
        s = (s ?? "").Trim();
        return s.Length <= max ? s : s[..max];
    }

    public bool Imprint(IServerPlayer player, string name = null, string description = null, System.Func<int, string> pinName = null, bool lockCopy = false, bool lockView = false, bool confirmSolder = false)
    {
        if (socket == null) { Say(player, "imprint-no-socket"); return false; }
        if (tube == null) { Say(player, "imprint-no-tube"); return false; }
        if (ForeignLocked(player)) { Say(player, "imprint-locked"); return false; }
        operatorUid = player.PlayerUID;
        var result = Read(player.PlayerUID);
        refused.Clear();
        refused.AddRange(result.Refusals.Select(r => r.Pos));
        ShowHighlights();
        if (!result.Ok)
        {
            string reasons = string.Join(", ", result.Refusals.Select(r => Lang.Get("signalstubes:refuse-" + r.Reason)).Distinct().Take(3));
            Say(player, "imprint-refused", reasons);
            return false;
        }
        if (result.Soldered.Count > 0 && !confirmSolder && WarnsAboutSoldering(player)) return false;   // the dialog asks first
        draft = null;
        var program = CircuitSimplifier.Simplify(result.Program);
        if (pinName != null)
            foreach (var pin in program.Pins) pin.Name = string.IsNullOrEmpty(pinName(pin.Index)) ? null : pinName(pin.Index);
        // Soldering: the referenced tubes leave their sockets and travel inside the new tube.
        var consumed = new List<ItemStack>();
        foreach (var s in result.Soldered)
            if (Api.World.BlockAccessor.GetBlockEntity(s.Pos) is ITubeSocket be && be.TakeTube() is ItemStack taken) consumed.Add(taken);
        var previous = TubeProgram.Soldered(tube, Api.World);   // re-imprinting a composite keeps what it already held
        TubeProgram.Set(tube, program, ProgramStore.Of(Api), player.PlayerUID, player.PlayerName, lockCopy || program.HasReferences, lockView);
        TubeProgram.SetSoldered(tube, previous.Concat(consumed));
        SetOrRemove(TubeProgram.NameKey, name);
        SetOrRemove(TubeProgram.DescriptionKey, description);
        MarkDirty(true);
        Say(player, "imprint-done", program.Components.Count, program.Pins.Count);
        return true;
    }

    private void SetOrRemove(string key, string value)
    {
        if (string.IsNullOrEmpty(value)) tube.Attributes.RemoveAttribute(key);
        else tube.Attributes.SetString(key, value);
    }

    private static void Say(IServerPlayer player, string key, params object[] args) =>
        player.SendMessage(GlobalConstants.InfoLogChatGroup, Lang.Get("signalstubes:" + key, args), EnumChatType.Notification);

    public bool InteractTube(IPlayer player)
    {
        var hand = player.InventoryManager.ActiveHotbarSlot;
        bool insert = tube == null && hand.Itemstack?.Collectible is ItemProgramTube;
        bool remove = tube != null && hand.Empty;
        if (!insert && !remove) return false;
        if (Api.Side == EnumAppSide.Client) return true;
        if (insert) { tube = hand.TakeOut(1); hand.MarkDirty(); }
        else
        {
            var taken = tube; tube = null;
            if (!player.InventoryManager.TryGiveItemstack(taken, true)) Api.World.SpawnItemEntity(taken, Pos.ToVec3d().Add(.5, .5, .5));
        }
        MarkDirty(true);
        Api.World.PlaySoundAt(new AssetLocation("signalstubes:sounds/tube-click"), Pos.X + .5, Pos.Y + .7, Pos.Z + .5, null, false, 12, .65f);
        return true;
    }

    // ---- persistence and sync

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        if (tube != null) tree.SetItemstack("tube", tube); else tree.RemoveAttribute("tube");
        if (socket != null) tree.SetBytes("socket", SerializerUtil.Serialize(socket)); else tree.RemoveAttribute("socket");
        foreach (var tool in new[] { plug, probe })
        {
            tree.SetBool(tool.Key + "Out", tool.Out);
            if (tool.Holder != null) tree.SetString(tool.Key + "Holder", tool.Holder); else tree.RemoveAttribute(tool.Key + "Holder");
        }
        if (operatorUid != null) tree.SetString("operator", operatorUid);
        tree.SetBytes("exposed", SerializerUtil.Serialize(exposed.ToArray()));
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        tube = tree.GetItemstack("tube");
        tube?.ResolveBlockOrItem(worldForResolving);
        socket = tree.HasAttribute("socket") ? SerializerUtil.Deserialize<BlockPos>(tree.GetBytes("socket")) : null;
        foreach (var tool in new[] { plug, probe })
        {
            tool.Out = tree.GetBool(tool.Key + "Out");
            tool.Holder = tree.GetString(tool.Key + "Holder");
        }
        operatorUid = tree.GetString("operator");
        exposed.Clear();
        if (tree.HasAttribute("exposed")) exposed.UnionWith(SerializerUtil.Deserialize<BlockPos[]>(tree.GetBytes("exposed")));
        if (Api is ICoreClientAPI) { UpdateCables(); MarkDirty(true); }
    }

    // ---- client

    private void DisposeCables()
    {
        plugCable?.Dispose(); plugCable = null;
        probeCable?.Dispose(); probeCable = null;
    }

    private void UpdateCables()
    {
        var capi = Api as ICoreClientAPI;
        if (capi == null) return;
        DisposeCables();
        if (socket != null)
        {
            // the socket says where its plug head is; its entity may not be loaded here yet, so ask until it answers
            Vec3d end = null;
            var at = socket.Copy();
            plugCable = new CableRenderer(capi, PlugAnchor(), () => end ??= (capi.World.BlockAccessor.GetBlockEntity(at) as ITubeSocket)?.PlugCableEnd(),
                CableMesh.PlugCable, CableRenderer.WireTexture);
        }
        else if (plug.Out && plug.Holder == capi.World.Player.PlayerUID)
            plugCable = new CableRenderer(capi, PlugAnchor(), HandOf(capi, PlugCode), CableMesh.PlugCable, CableRenderer.WireTexture);
        if (probe.Out && probe.Holder == capi.World.Player.PlayerUID)
            probeCable = new CableRenderer(capi, ProbeAnchor(), HandOf(capi, ProbeCode), CableMesh.ProbeLead, CableRenderer.RedTexture);
    }

    // Roughly the right hand of the local player while the tool is in the active slot.
    private System.Func<Vec3d> HandOf(ICoreClientAPI capi, string code) => () =>
    {
        var player = capi.World.Player;
        if (!ItemImprinterTool.BelongsTo(player.InventoryManager.ActiveHotbarSlot?.Itemstack, code, Pos)) return null;
        float yaw = player.Entity.Pos.Yaw;
        return player.Entity.Pos.XYZ.Add(-Math.Sin(yaw) * .3 + Math.Cos(yaw) * .35, player.Entity.LocalEyePos.Y - .5, -Math.Cos(yaw) * .3 - Math.Sin(yaw) * .35);
    };

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        mesher.AddMeshData(GetMesh((ICoreClientAPI)Api));
        return true;
    }

    private MeshData GetMesh(ICoreClientAPI capi)
    {
        var block = (BlockImprinter)Block;
        string key = $"{block.Code}|{PlugHome}|{ProbeHome}|{tube?.Collectible.Code}|{(tube == null ? "" : TubeVisuals.MeshKey(tube, false))}";
        lock (meshCache)
        {
            if (meshCache.TryGetValue(key, out var cached)) return cached;
            if (meshCache.Count >= 64) meshCache.Clear();
            Shape shape = capi.Assets.Get(new AssetLocation("signalstubes", "shapes/block/imprinter.json")).ToObject<Shape>().Clone();
            shape.Elements = shape.Elements.Where(e => (PlugHome || !e.Name.StartsWith("plug_")) && (ProbeHome || !e.Name.StartsWith("probe_"))).ToArray();
            if (tube?.Collectible is ItemProgramTube item)
                TubeVisuals.Append(shape, item.BuildShape(tube, false), .5f, new Vec3f(4, 10.6f, 4));
            capi.Tesselator.TesselateShape(block, shape, out MeshData mesh, new Vec3f(0, block.Shape.rotateY, 0));
            meshCache[key] = mesh;
            return mesh;
        }
    }
}
