using signals.src;
using signals.src.hangingwires;
using signals.src.signalNetwork;
using SignalsTubes.src.programtube;
using SignalsTubes.src.socket;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SignalsTubes.src.imprint;

/// <summary>Server-side view of the live Signals network for the reader.</summary>
public sealed class SignalsCircuitWorld : ICircuitWorld
{
    private readonly ICoreAPI api;
    private readonly SignalNetworkMod signals;
    private readonly HangingWiresMod wires;

    public SignalsCircuitWorld(ICoreAPI api)
    {
        this.api = api;
        signals = api.ModLoader.GetModSystem<SignalNetworkMod>();
        wires = api.ModLoader.GetModSystem<HangingWiresMod>();
    }

    public Block BlockAt(BlockPos pos)
    {
        var block = api.World.BlockAccessor.GetBlock(pos);
        return block == null || block.Id == 0 ? null : block;
    }

    public IReadOnlyList<int> NodesAt(BlockPos pos) =>
        signals.GetDeviceAt(pos)?.GetNodes().Keys.Select(n => n.index).OrderBy(i => i).ToList() ?? new List<int>();

    public byte SourceOutput(NodeRef node) =>
        signals.GetDeviceAt(node.Pos)?.GetNodeAt(new NodePos(node.Pos, node.Index))?.output ?? 0;

    public IEnumerable<NodeRef> WiresFrom(NodeRef node)
    {
        var pos = new NodePos(node.Pos, node.Index);
        foreach (var wire in wires.data.connections)
        {
            if (wire.pos1 == pos) yield return new NodeRef(wire.pos2.blockPos.Copy(), wire.pos2.index);
            else if (wire.pos2 == pos) yield return new NodeRef(wire.pos1.blockPos.Copy(), wire.pos1.index);
        }
    }

    public NestedTube TubeAt(BlockPos pos, string readerUid)
    {
        if (api.World.BlockAccessor.GetBlockEntity(pos) is not BETubeSocket { HasTube: true } be || TubeProgram.IsBlank(be.Tube)) return null;
        var stack = be.Tube;
        var program = TubeProgram.Get(stack, api);
        if (program == null) return null;   // unreadable program: behaves as blank
        bool foreign = !TubeProgram.IsAuthor(stack, readerUid);
        string id = stack.Attributes.GetString(TubeProgram.IdKey);
        bool solder = id != null && (foreign && (TubeProgram.LockCopy(stack) || TubeProgram.LockView(stack)) || program.HasReferences);
        return new NestedTube
        {
            Name = stack.Attributes.GetString(TubeProgram.NameKey, stack.GetName()),
            Description = stack.Attributes.GetString(TubeProgram.DescriptionKey),
            ProgramId = id,
            Pins = TubeProgram.Pins(stack),
            Solder = solder,
            Program = solder ? null : program
        };
    }

    public ITreeAttribute EntityAttributes(BlockPos pos)
    {
        var be = api.World.BlockAccessor.GetBlockEntity(pos);
        if (be == null) return null;
        var tree = new TreeAttribute();
        be.ToTreeAttributes(tree);
        return tree;
    }
}
