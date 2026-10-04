using System.IO;
using System.Text;
using signals.src.signalNetwork;
using SignalsTubes.src.circuit;
using SignalsTubes.src.programtube;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace SignalsMachines.src.craftingmachine;

/// <summary>
/// Debug trace of one machine, switched on per machine by "/machines trace" while aiming at it and
/// gone with the server. Writes a header (tube, program, wiring) and then every signal and mechanics
/// step into Logs/signalsmachines-trace-x-y-z.log, flushed line by line.
/// </summary>
public sealed class MachineTrace : IDisposable
{
    private readonly StreamWriter writer;
    private readonly ICoreServerAPI sapi;
    private readonly long started;
    public string Path { get; }

    private MachineTrace(ICoreServerAPI sapi, BECraftingMachine be)
    {
        this.sapi = sapi;
        string dir = System.IO.Path.Combine(GamePaths.Logs);
        Directory.CreateDirectory(dir);
        Path = System.IO.Path.Combine(dir, $"signalsmachines-trace-{be.Pos.X}-{be.Pos.Y}-{be.Pos.Z}.log");
        writer = new StreamWriter(Path, false, new UTF8Encoding(false)) { AutoFlush = true };
        started = sapi.World.ElapsedMilliseconds;
    }

    public static MachineTrace Start(ICoreServerAPI sapi, BECraftingMachine be)
    {
        var trace = new MachineTrace(sapi, be);
        trace.Header(be);
        return trace;
    }

    private string Stamp() => $"{(sapi.World.ElapsedMilliseconds - started) / 1000.0,8:0.000}";

    /// <summary>A forgotten trace must not fill the disk: past this size it writes one last line and stops.</summary>
    public const long MaxBytes = 20 * 1024 * 1024;
    private bool full;

    public void Line(string text)
    {
        lock (writer)
        {
            if (full) return;
            if (writer.BaseStream.Position > MaxBytes)
            {
                full = true;
                writer.WriteLine(Stamp() + "  === trace stopped: size limit " + MaxBytes / (1024 * 1024) + " MB ===");
                return;
            }
            writer.WriteLine(Stamp() + "  " + text);
        }
    }

    public void Header(BECraftingMachine be)
    {
        Line($"=== machine {be.Pos} block {be.Block?.Code} ===");
        Line($"inputs {be.Inputs} state {be.State} tubeControls {be.TubeControls} plateSpeed {be.PlateSpeed:0.000} networkSpeed {be.NetworkSpeed:0.000}");
        Line($"cells {be.OccupiedCells} recipe {(be.Recipe == null ? "-" : be.Recipe.Recipe.Name?.ToString())} product {(be.Inventory[BECraftingMachine.ProductSlot].Empty ? "-" : be.Inventory[BECraftingMachine.ProductSlot].GetStackName())}");
        for (int i = 0; i < BECraftingMachine.GridSlots; i++)
            if (!be.Inventory[i].Empty) Line($"  cell {i}: {be.Inventory[i].StackSize}x {be.Inventory[i].Itemstack.Collectible.Code}");
        Wiring(be);
        if (be.Tube != null)
        {
            Line($"tube: {be.Tube.GetName()} name=\"{be.Tube.Attributes.GetString(TubeProgram.NameKey, "")}\" id={be.Tube.Attributes.GetString(TubeProgram.IdKey, "-")}");
            foreach (var pin in TubeProgram.Pins(be.Tube)) Line($"  pin {pin.Index + 1}: {pin.Role} \"{pin.Name}\"");
            var program = TubeProgram.Get(be.Tube, sapi);
            if (program != null)
            {
                Line($"program: {program.Components.Count} parts, {program.Links.Count} links, {program.NodeCount} nodes");
                Line("program json: " + ProgramCodec.ToJson(program));
            }
            else Line("program: unreadable");
        }
        else Line("tube: none (wires control the machine)");
        Line("=== trace ===");
    }

    private void Wiring(BECraftingMachine be)
    {
        var beh = be.GetBehavior<BEBehaviorSignalConnector>();
        var wires = sapi.ModLoader.GetModSystem<signals.src.hangingwires.HangingWiresMod>();
        if (beh == null) { Line("no signal connector"); return; }
        foreach (var node in beh.GetNodes().Values.OrderBy(n => n.Pos.index))
        {
            var ends = wires == null ? new List<string>() : wires.data.connections
                .Where(w => w.pos1 == node.Pos || w.pos2 == node.Pos)
                .Select(w => (w.pos1 == node.Pos ? w.pos2 : w.pos1))
                .Select(p => $"{p.blockPos}#{p.index} ({sapi.World.BlockAccessor.GetBlock(p.blockPos)?.Code})").ToList();
            Line($"pin {node.Pos.index + 1}: net {(node.netId?.ToString() ?? "-")} value {node.value} output {node.output} wires [{string.Join(", ", ends)}]");
        }
    }

    /// <summary>One signal step: what each pin showed, what the tube read and gave, what the machine took.</summary>
    public void SignalStep(BECraftingMachine be, BEBehaviorSignalConnector beh, MachineInputs before, MachineInputs after, bool forgiven, string tube)
    {
        var sb = new StringBuilder("signal: pins[");
        for (int i = 0; i < BECraftingMachine.PinCount; i++)
        {
            var node = beh.GetNodeAt(new NodePos(be.Pos, i));
            sb.Append(i == 0 ? "" : " ").Append(i + 1).Append('=').Append(node == null ? "?" : $"{node.value}/{node.output}");
        }
        sb.Append("] ");
        if (tube != null) sb.Append(tube).Append(' ');
        sb.Append($"inputs {before} -> {after}{(forgiven ? " FORGIVEN" : "")} state {be.State}");
        Line(sb.ToString());
    }

    public void Dispose() { Line("=== end ==="); writer.Dispose(); }
}
