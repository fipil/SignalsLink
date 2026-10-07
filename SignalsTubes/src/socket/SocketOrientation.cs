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

    /// <summary>Which way a pin's name tag leaves the socket: the three pins along each side go out sideways (west: 0, 7, 6; east: 2, 3, 4), the middle pins of the front and back rows (1, 5) out that way.</summary>
    public static readonly BlockFacing[] TagSide =
    {
        BlockFacing.WEST, BlockFacing.NORTH, BlockFacing.EAST, BlockFacing.EAST,
        BlockFacing.EAST, BlockFacing.SOUTH, BlockFacing.WEST, BlockFacing.WEST
    };

    /// <summary>A pin's middle in the unrotated shape, in blocks (the pins are 2/16 squares at 3, 8 and 13 sixteenths).</summary>
    public static Vec3f PinCentre(int pin)
    {
        (float x, float z) = pin switch { 0 => (3, 3), 1 => (8, 3), 2 => (13, 3), 3 => (13, 8), 4 => (13, 13), 5 => (8, 13), 6 => (3, 13), _ => (3, 8) };
        return new Vec3f(x / 16, 2 / 16f, z / 16);
    }

    /// <summary>
    /// A pin's name tag as a strip in the plate's plane, in the unrotated frame: from just outside the socket's
    /// edge (the pins stand 3/16 in from it) outwards, `length` = width x the text's aspect. The text reads along `right`, its top towards `up`; `flip`
    /// turns it round (same strip, other way up). The quad is origin + right*length*x + (-up)*width*z.
    /// </summary>
    public static (Vec3f origin, Vec3f right, Vec3f up, float length) TagStrip(int pin, float aspect, bool flip, float width, float gap, float lift)
    {
        var d = TagSide[pin].Normalf;
        var c = PinCentre(pin);
        var start = new Vec3f(c.X + d.X * (3 / 16f + gap), lift, c.Z + d.Z * (3 / 16f + gap));
        float length = width * aspect;
        var up = flip ? new Vec3f(d.Z, 0, -d.X) : new Vec3f(-d.Z, 0, d.X);   // across the strip
        var right = new Vec3f(-up.Z, 0, up.X);                                  // up x normal(0,1,0): reading direction
        bool alongD = right.X * d.X + right.Z * d.Z > 0;
        var origin = alongD ? start : new Vec3f(start.X + d.X * length, start.Y, start.Z + d.Z * length);
        origin = new Vec3f(origin.X + up.X * width / 2, origin.Y, origin.Z + up.Z * width / 2);
        return (origin, right, up, length);
    }

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
