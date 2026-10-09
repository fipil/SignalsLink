using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SignalsTubes.src.imprint;

[ProtoContract]
public class ProbeMarkPacket
{
    [ProtoMember(1)] public int ImprinterX;
    [ProtoMember(2)] public int ImprinterY;
    [ProtoMember(3)] public int ImprinterZ;
    [ProtoMember(4)] public int TargetX;
    [ProtoMember(5)] public int TargetY;
    [ProtoMember(6)] public int TargetZ;
    [ProtoMember(7)] public bool FromDialog;   // no probe in hand then; the open dialog is the authority
}

/// <summary>Client asks for the dialog state of the imprinter at X/Y/Z.</summary>
[ProtoContract]
public class ImprinterOpenPacket
{
    [ProtoMember(1)] public int X;
    [ProtoMember(2)] public int Y;
    [ProtoMember(3)] public int Z;
    [ProtoMember(4)] public string Path;   // schematic level, steps joined by ';'
}

/// <summary>Server answers with the dialog state as JSON (see BEImprinter.StateJson).</summary>
[ProtoContract]
public class ImprinterStatePacket
{
    [ProtoMember(1)] public int X;
    [ProtoMember(2)] public int Y;
    [ProtoMember(3)] public int Z;
    [ProtoMember(4)] public string Json;
}

/// <summary>Client sends the edits and asks to imprint.</summary>
[ProtoContract]
public class ImprintPacket
{
    [ProtoMember(1)] public int X;
    [ProtoMember(2)] public int Y;
    [ProtoMember(3)] public int Z;
    [ProtoMember(4)] public string Json;
}

/// <summary>
/// Network side of the imprinter: the probe's marking click (caught before the block sees it, so a
/// switch is not flipped), the dialog state, and the imprint request.
/// </summary>
public static class ImprinterNetwork
{
    public const string Channel = "signalstubes";
    private static long lastClick;
    private static readonly Dictionary<BlockPos, GuiDialogImprinter> dialogs = new();

    public static void StartClient(ICoreClientAPI capi)
    {
        capi.Network.RegisterChannel(Channel)
            .RegisterMessageType<ProbeMarkPacket>()
            .RegisterMessageType<ImprinterOpenPacket>()
            .RegisterMessageType<ImprinterStatePacket>()
            .RegisterMessageType<ImprintPacket>()
            .SetMessageHandler<ImprinterStatePacket>(packet =>
            {
                var pos = new BlockPos(packet.X, packet.Y, packet.Z);
                if (!dialogs.TryGetValue(pos, out var dialog))
                {
                    dialog = new GuiDialogImprinter(pos, capi);
                    dialog.OnClosed += () => dialogs.Remove(pos);
                    dialogs[pos] = dialog;
                }
                dialog.SetState(packet.Json);
            });

        capi.Input.InWorldAction += (EnumEntityAction action, bool on, ref EnumHandling handled) =>
        {
            if (action != EnumEntityAction.InWorldRightMouseDown || !on) return;
            var player = capi.World.Player;
            var stack = player.InventoryManager.ActiveHotbarSlot?.Itemstack;
            var sel = player.CurrentBlockSelection;
            if (!ItemImprinterTool.IsProbe(stack) || sel == null) return;
            if (!ItemImprinterTool.IsMarkable(capi.World.BlockAccessor.GetBlock(sel.Position))) return;
            handled = EnumHandling.PreventDefault;
            long now = capi.World.ElapsedMilliseconds;   // the action repeats while the button is held
            if (now - lastClick < 400) return;
            lastClick = now;
            var imprinter = ItemImprinterTool.ImprinterOf(stack);
            capi.Network.GetChannel(Channel).SendPacket(new ProbeMarkPacket {
                ImprinterX = imprinter.X, ImprinterY = imprinter.Y, ImprinterZ = imprinter.Z,
                TargetX = sel.Position.X, TargetY = sel.Position.Y, TargetZ = sel.Position.Z });
        };
    }

    public static void ToggleMark(ICoreClientAPI capi, BlockPos imprinter, BlockPos target) =>
        capi.Network.GetChannel(Channel).SendPacket(new ProbeMarkPacket {
            ImprinterX = imprinter.X, ImprinterY = imprinter.Y, ImprinterZ = imprinter.Z,
            TargetX = target.X, TargetY = target.Y, TargetZ = target.Z, FromDialog = true });

    public static void OpenDialog(ICoreClientAPI capi, BlockPos pos, string path = "") =>
        capi.Network.GetChannel(Channel).SendPacket(new ImprinterOpenPacket { X = pos.X, Y = pos.Y, Z = pos.Z, Path = path });

    public static void StartServer(ICoreServerAPI sapi)
    {
        var parsers = sapi.ChatCommands.Parsers;
        sapi.ChatCommands.Create("tubes").WithDescription("Signals Tubes").RequiresPrivilege(Privilege.chat)
            .BeginSubCommand("warn").WithDescription("Warn before soldering tubes into a new one: on / off")
            .WithArgs(parsers.WordRange("state", "on", "off")).RequiresPlayer()
            .HandleWith(args =>
            {
                BEImprinter.SetSolderWarning((IServerPlayer)args.Caller.Player, (string)args[0] == "on");
                return TextCommandResult.Success(Lang.Get("signalstubes:warn-" + (string)args[0]));
            })
            .EndSubCommand()
            // Diagnostics: the pins of the socket aimed at, as the Signals network sees them.
            .BeginSubCommand("diag").WithDescription("Signals view of the socket you are looking at").RequiresPlayer()
            .HandleWith(args => TextCommandResult.Success(SocketDiagnostics(sapi, args.Caller.Player)))
            .EndSubCommand()
            // Admin: the tube in hand to a file on the server, and back into the hand of whoever imports it.
            .BeginSubCommand("export").WithDescription("Write the tube in your hand to ModData/signalstubes/tubes/<name>.json (no name: the tube's own)")
            .WithArgs(parsers.OptionalAll("name")).RequiresPlayer().RequiresPrivilege(Privilege.controlserver)
            .HandleWith(args => ExportTube(sapi, (IServerPlayer)args.Caller.Player, (string)args[0]))
            .EndSubCommand()
            .BeginSubCommand("import").WithDescription("Create the tube from ModData/signalstubes/tubes/<name>.json; you become its author")
            .WithArgs(parsers.All("name")).RequiresPlayer().RequiresPrivilege(Privilege.controlserver)
            .HandleWith(args => ImportTube(sapi, (IServerPlayer)args.Caller.Player, (string)args[0]))
            .EndSubCommand();

        sapi.Network.RegisterChannel(Channel)
            .RegisterMessageType<ProbeMarkPacket>()
            .RegisterMessageType<ImprinterOpenPacket>()
            .RegisterMessageType<ImprinterStatePacket>()
            .RegisterMessageType<ImprintPacket>()
            .SetMessageHandler<ProbeMarkPacket>((player, packet) =>
            {
                var imprinterPos = new BlockPos(packet.ImprinterX, packet.ImprinterY, packet.ImprinterZ);
                var target = new BlockPos(packet.TargetX, packet.TargetY, packet.TargetZ);
                if (packet.FromDialog ? !Reachable(player, imprinterPos)
                    : !ItemImprinterTool.BelongsTo(player.InventoryManager.ActiveHotbarSlot?.Itemstack, BEImprinter.ProbeCode, imprinterPos)
                      || player.Entity.Pos.DistanceTo(target.ToVec3d().Add(.5, .5, .5)) > 8) return;
                if (!ItemImprinterTool.IsMarkable(sapi.World.BlockAccessor.GetBlock(target))) return;
                if (sapi.World.BlockAccessor.GetBlockEntity(imprinterPos) is not BEImprinter imprinter) return;
                bool marked = imprinter.ToggleExposed(target);
                if (packet.FromDialog) return;
                player.SendMessage(GlobalConstants.InfoLogChatGroup, Lang.Get(marked ? "signalstubes:imprint-marked" : "signalstubes:imprint-unmarked"), EnumChatType.Notification);
            })
            .SetMessageHandler<ImprinterOpenPacket>((player, packet) =>
            {
                var pos = new BlockPos(packet.X, packet.Y, packet.Z);
                if (Reachable(player, pos) && sapi.World.BlockAccessor.GetBlockEntity(pos) is BEImprinter imprinter)
                    SendState(sapi, player, imprinter, false, packet.Path);
            })
            .SetMessageHandler<ImprintPacket>((player, packet) =>
            {
                var pos = new BlockPos(packet.X, packet.Y, packet.Z);
                if (!Reachable(player, pos) || sapi.World.BlockAccessor.GetBlockEntity(pos) is not BEImprinter imprinter) return;
                bool done = imprinter.ApplyAndImprint(player, packet.Json);
                if (!packet.Json.Contains("\"draft\":true")) SendState(sapi, player, imprinter, done);
            });
    }

    // "Řízení craftovacího stroje" -> "Řízení-craftovacího-stroje.json": spaces become dashes, anything a file name cannot carry is dropped
    private static string TubeFile(ICoreServerAPI sapi, string name, out string error)
    {
        error = null;
        string file = new string((name ?? "").Trim().Select(c => char.IsWhiteSpace(c) ? '-' : c).Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.').ToArray()).Trim('.');
        if (file.Length == 0) { error = "Give the file a name (letters, digits, - and _)."; return null; }
        return System.IO.Path.Combine(sapi.GetOrCreateDataPath(System.IO.Path.Combine("ModData", "signalstubes", "tubes")), file + ".json");
    }

    private static TextCommandResult ExportTube(ICoreServerAPI sapi, IServerPlayer player, string name)
    {
        var stack = player.InventoryManager.ActiveHotbarSlot?.Itemstack;
        if (stack?.Collectible is not programtube.ItemProgramTube) return TextCommandResult.Error("Hold the tube to export.");
        if (string.IsNullOrWhiteSpace(name)) name = stack.Attributes.GetString(programtube.TubeProgram.NameKey);
        string path = TubeFile(sapi, name, out string error);
        if (path == null) return TextCommandResult.Error(error);
        System.IO.File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(programtube.TubeTransfer.Export(stack, sapi), Newtonsoft.Json.Formatting.Indented));
        return TextCommandResult.Success("Exported to " + path);
    }

    private static TextCommandResult ImportTube(ICoreServerAPI sapi, IServerPlayer player, string name)
    {
        string path = TubeFile(sapi, name, out string error);
        if (path == null) return TextCommandResult.Error(error);
        if (!System.IO.File.Exists(path)) return TextCommandResult.Error("No such file: " + path);
        ItemStack stack;
        try { stack = programtube.TubeTransfer.Import(Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path)), sapi, player.PlayerUID, player.PlayerName); }
        catch (Exception e) when (e is FormatException or Newtonsoft.Json.JsonException) { return TextCommandResult.Error("Unreadable tube file: " + e.Message); }
        if (stack == null) return TextCommandResult.Error("The file names an item this server does not have.");
        if (!player.InventoryManager.TryGiveItemstack(stack, true)) sapi.World.SpawnItemEntity(stack, player.Entity.Pos.XYZ);
        return TextCommandResult.Success("Imported " + (stack.Attributes.GetString(programtube.TubeProgram.NameKey) ?? stack.GetName()) + "; you are its author now.");
    }

    private static string SocketDiagnostics(ICoreServerAPI sapi, IPlayer player)
    {
        var sel = player.CurrentBlockSelection;
        if (sel == null || sapi.World.BlockAccessor.GetBlockEntity(sel.Position) is not BlockEntity be) return "Aim at a socket or machine.";
        var beh = be.GetBehavior<signals.src.signalNetwork.BEBehaviorSignalNodeProvider>();
        if (beh == null) return be.GetType().Name + ": no Signals node provider.";
        var wires = sapi.ModLoader.GetModSystem<signals.src.hangingwires.HangingWiresMod>();
        var sb = new System.Text.StringBuilder();
        var tubeSocket = be as socket.ITubeSocket;
        sb.AppendLine(be.GetType().Name + " at " + sel.Position + (tubeSocket == null ? "" : tubeSocket.HasTube ? ", tube: " + tubeSocket.Tube.GetName() : ", empty"));
        foreach (var node in beh.GetNodes().Values.OrderBy(n => n.Pos.index))
        {
            int wireCount = wires?.data.connections.Count(w => w.pos1 == node.Pos || w.pos2 == node.Pos) ?? 0;
            sb.AppendLine($"pin {node.Pos.index + 1}: net {(node.netId?.ToString() ?? "-")}, value {node.value}, output {node.output}, connections {node.Connections.Count}, wires {wireCount}");
        }
        if (tubeSocket?.Diagnostics() is string tube) sb.Append(tube);
        return sb.ToString();
    }

    private static bool Reachable(IServerPlayer player, BlockPos pos) =>
        player.Entity.Pos.DistanceTo(pos.ToVec3d().Add(.5, .5, .5)) < 8;

    public static void SendState(ICoreServerAPI sapi, IServerPlayer player, BEImprinter imprinter, bool done, string path = "") =>
        sapi.Network.GetChannel(Channel).SendPacket(new ImprinterStatePacket {
            X = imprinter.Pos.X, Y = imprinter.Pos.Y, Z = imprinter.Pos.Z, Json = imprinter.StateJson(player, done, path) }, player);
}
