using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SignalsTubes.src.imprint;

/// <summary>Server-session selections, retained across imprinter chunk unloads, never saved to disk.</summary>
public sealed class ImprintExclusions : ModSystem
{
    private sealed record Snapshot(string Block, string Wires);
    private readonly Dictionary<BlockPos, Dictionary<BlockPos, Snapshot>> selections = new();
    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        api.Event.DidBreakBlock += (_, _, sel) => Changed(sel.Position);
        api.Event.DidPlaceBlock += (_, _, sel, _) => Changed(sel.Position);
    }

    public void Changed(BlockPos pos)
    {
        selections.Remove(pos); // a replaced imprinter must not inherit the old one's selection
        foreach (var selection in selections.Values) selection.Remove(pos);
    }

    public bool Toggle(BlockPos imprinter, BlockPos pos, ICircuitWorld world)
    {
        if (!selections.TryGetValue(imprinter, out var selection)) selections[imprinter.Copy()] = selection = new();
        if (selection.Remove(pos)) return false;
        selection[pos.Copy()] = Signature(pos, world);
        return true;
    }

    public HashSet<BlockPos> Current(BlockPos imprinter, ICircuitWorld world, System.Func<BlockPos, bool> loaded)
    {
        if (!selections.TryGetValue(imprinter, out var selection)) return new();
        foreach (var pair in selection.ToArray())
        {
            if (!loaded(pair.Key)) continue;
            var now = Signature(pair.Key, world);
            // A loaded chunk may still be initializing its signal devices. Do not mistake that for rewiring.
            if (now.Block != pair.Value.Block || world.NodesAt(pair.Key).Count > 0 && now.Wires != pair.Value.Wires)
                selection.Remove(pair.Key);
        }
        return selection.Keys.Select(p => p.Copy()).ToHashSet();
    }

    public override void Dispose() { selections.Clear(); base.Dispose(); }

    private static Snapshot Signature(BlockPos pos, ICircuitWorld world)
    {
        var block = world.BlockAt(pos);
        if (block?.Code == null) return new("", "");
        // Signal-driven variants and the position of a switch are not structural changes.
        var variants = block.Variant?.Where(v => v.Key is not ("state" or "powered" or "lit"))
            .OrderBy(v => v.Key).Select(v => v.Key + "=" + v.Value) ?? Enumerable.Empty<string>();
        var wires = world.NodesAt(pos).SelectMany(i => world.WiresFrom(new NodeRef(pos, i))
            .Select(n => $"{i}:{n.Pos.dimension}:{n.Pos.X}:{n.Pos.Y}:{n.Pos.Z}:{n.Index}"));
        return new(block.Code.Domain + ":" + block.Code.Path.Split('-')[0] + "|" + string.Join(";", variants),
            string.Join(";", wires.OrderBy(w => w, StringComparer.Ordinal)));
    }
}
