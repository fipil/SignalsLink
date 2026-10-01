using SignalsTubes.src.circuit;
using SignalsTubes.src.programtube;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SignalsTubes.src.imprint;

/// <summary>A programmed tube sitting in a socket inside the network being read.</summary>
public sealed class NestedTube
{
    public string Name;
    public string Description;
    public string ProgramId;
    public List<PublicPin> Pins = new();
    /// <summary>True: keep it by reference and consume the item (foreign locked, or itself containing soldered parts).</summary>
    public bool Solder;
    /// <summary>The program when it may be inlined; null when soldered.</summary>
    public CircuitProgram Program;
}

/// <summary>A node: block position plus node index, the same identity Signals uses.</summary>
public readonly record struct NodeRef(BlockPos Pos, int Index);

/// <summary>What the reader needs to see of the world. The bridge answers from Signals; tests fake it.</summary>
public interface ICircuitWorld
{
    Block BlockAt(BlockPos pos);
    /// <summary>Node indices the device at pos provides; empty when there is no device.</summary>
    IReadOnlyList<int> NodesAt(BlockPos pos);
    /// <summary>Output level of a source node.</summary>
    byte SourceOutput(NodeRef node);
    /// <summary>Hanging wires touching this node.</summary>
    IEnumerable<NodeRef> WiresFrom(NodeRef node);
    /// <summary>Saved state of the block entity at pos, null when there is none.</summary>
    ITreeAttribute EntityAttributes(BlockPos pos);
    /// <summary>The programmed tube in the socket at pos, null for no socket, an empty socket or a blank tube.</summary>
    NestedTube TubeAt(BlockPos pos, string readerUid);
}
