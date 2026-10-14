using SignalsTubes.src.programtube;

namespace SignalsTubes.src.circuit;

/// <summary>Per running tube, including every occurrence of every nested program.</summary>
public static class CircuitLimits
{
    // Root is level 1. Keep all recursive simulator operations safely bounded.
    public const int MaxDepth = 64;
    // One unit per program instance, node, link and component (including tube components).
    public const int MaxExpandedSize = 65_536;
}

public sealed class CircuitProgramException : FormatException
{
    public TubeProgramError Error { get; }
    public CircuitProgramException(TubeProgramError error, string message) : base(message) => Error = error;
}
