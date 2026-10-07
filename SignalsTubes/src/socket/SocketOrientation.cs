using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsTubes.src.socket;

/// <summary>
/// The socket's orientation variant is the world side its pins 0-1-2 face (Signals' cover convention).
/// Pure geometry: which side of the plate the player aims at, and which world side a pin ends up on
/// for a given variant.
/// </summary>
public static class SocketOrientation
{
    /// <summary>Local side of each pin in the unrotated shape: 0-2 north, 3 east, 4-6 south, 7 west.</summary>
    public static readonly BlockFacing[] LocalSide =
    {
        BlockFacing.NORTH, BlockFacing.NORTH, BlockFacing.NORTH, BlockFacing.EAST,
        BlockFacing.SOUTH, BlockFacing.SOUTH, BlockFacing.SOUTH, BlockFacing.WEST
    };

    /// <summary>The side of the plate (in its own plane) the hit point lies towards; `side` is the face the socket sits on.</summary>
    public static BlockFacing AimedSide(BlockFacing side, Vec3d hit)
    {
        var n = side.Normalf;
        var v = hit.SubCopy(.5, .5, .5);
        double along = v.X * n.X + v.Y * n.Y + v.Z * n.Z;
        v.Sub(n.X * along, n.Y * along, n.Z * along);          // projected onto the plate
        return BlockFacing.FromVector(v.X, v.Y, v.Z);
    }

    /// <summary>A local direction of the shape turned the way this variant's shape is turned.</summary>
    public static BlockFacing WorldSide(Block variant, BlockFacing local)
    {
        var m = new Matrixf().RotateXDeg(variant.Shape.rotateX).RotateYDeg(variant.Shape.rotateY).RotateZDeg(variant.Shape.rotateZ);
        var v = m.TransformVector(new Vec4f(local.Normalf.X, local.Normalf.Y, local.Normalf.Z, 0));
        return BlockFacing.FromVector(v.X, v.Y, v.Z);
    }

    public static BlockFacing OrientationOf(Block socket) => BlockFacing.FromCode(socket.Variant["orientation"]);
    public static BlockFacing SideOf(Block socket) => BlockFacing.FromCode(socket.Variant["side"]);
}
