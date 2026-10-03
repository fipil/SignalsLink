using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsTubes.src.socket;

/// <summary>
/// Anything an imprinter plug can be pushed into: the socket block, or a machine with a built-in socket.
/// Its eight pins are the Signals anchors 0-7 at the block's position; the reader starts there.
/// </summary>
public interface ITubeSocket
{
    bool HasTube { get; }
    ItemStack Tube { get; }
    /// <summary>The imprinter whose plug sits here, or null.</summary>
    BlockPos Imprinter { get; }
    void SetImprinter(BlockPos pos);
    /// <summary>Server: removes the tube without dropping it (soldering).</summary>
    ItemStack TakeTube();
    /// <summary>World point where the plug cable meets the plug head.</summary>
    Vec3d PlugCableEnd();
}
