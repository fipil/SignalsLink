using signals.src.hangingwires;
using signals.src.signalNetwork;
using SignalsTubes.src.circuit;
using SignalsTubes.src.socket;
using SignalsTubes.Tests.fidelity;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using static SignalsTubes.Tests.fidelity.SignalsRig;

namespace SignalsTubes.Tests;

/// <summary>Seating a tube: the socket turns towards the aimed side, and the wires survive only the same program.</summary>
public class SocketOrientationTests
{
    private static Block Variant(float rx, float ry, float rz) => new() { Shape = new CompositeShape { rotateX = rx, rotateY = ry, rotateZ = rz } };

    [Fact]
    public void TheAimedSideIsReadInThePlateOfTheSocket()
    {
        // on the floor: the height of the hit does not matter, the horizontal offset does
        Assert.Equal(BlockFacing.NORTH, SocketOrientation.AimedSide(BlockFacing.DOWN, new Vec3d(.5, .9, .1)));
        Assert.Equal(BlockFacing.EAST, SocketOrientation.AimedSide(BlockFacing.DOWN, new Vec3d(.9, .0, .55)));
        Assert.Equal(BlockFacing.SOUTH, SocketOrientation.AimedSide(BlockFacing.DOWN, new Vec3d(.4, .2, .95)));
        // on an east wall: only up/down and north/south remain
        Assert.Equal(BlockFacing.UP, SocketOrientation.AimedSide(BlockFacing.EAST, new Vec3d(.95, .9, .5)));
        Assert.Equal(BlockFacing.NORTH, SocketOrientation.AimedSide(BlockFacing.EAST, new Vec3d(.05, .5, .1)));
    }

    [Fact]
    public void PinSidesFollowTheVariantsShapeRotation()
    {
        // the variants' rotations as the block type defines them: the 0-1-2 row (local north) faces the orientation
        Assert.Equal(BlockFacing.NORTH, SocketOrientation.WorldSide(Variant(0, 0, 0), BlockFacing.NORTH));      // north-down
        Assert.Equal(BlockFacing.EAST, SocketOrientation.WorldSide(Variant(0, 270, 0), BlockFacing.NORTH));     // east-down
        Assert.Equal(BlockFacing.SOUTH, SocketOrientation.WorldSide(Variant(0, 180, 0), BlockFacing.NORTH));    // south-down
        Assert.Equal(BlockFacing.UP, SocketOrientation.WorldSide(Variant(90, 0, 0), BlockFacing.NORTH));        // up-north (on a wall)
        Assert.Equal(BlockFacing.UP, SocketOrientation.WorldSide(Variant(0, 0, 90), BlockFacing.EAST));         // north-east: pin 3 points up
        Assert.Equal(BlockFacing.NORTH, SocketOrientation.LocalSide[1]);
        Assert.Equal(BlockFacing.WEST, SocketOrientation.LocalSide[7]);
    }

    private static CircuitProgram PassThrough(int attenuation = 0) => ProgramCodec.FromJson(
        "{\"v\":1,\"n\":2,\"links\":[[0,1," + attenuation + ",0]],\"parts\":[],\"pins\":[{\"i\":0,\"r\":\"in\",\"n\":0},{\"i\":1,\"r\":\"out\",\"n\":1}]}");

    private static (SignalsRig rig, BETubeSocket socket) WiredSocket()
    {
        var rig = new SignalsRig();
        rig.Socket(At(0));
        rig.SourceBlock(At(1), 9);
        rig.Connection(At(2));
        rig.Wire(At(1), 0, At(0), 0);
        rig.Wire(At(0), 1, At(2), 0);
        return (rig, (BETubeSocket)rig.World.BlockAccessor.GetBlockEntity(At(0)));
    }

    [Fact]
    public void TheSameProgramGoesBackInWithItsWires()
    {
        var (rig, socket) = WiredSocket();
        socket.Insert(rig.Tube(PassThrough()), null);
        Assert.Equal(2, rig.Wires.data.connections.Count);
        socket.TakeTube();
        Assert.Equal(2, rig.Wires.data.connections.Count);                 // pulling it out changes nothing
        socket.Insert(rig.Tube(PassThrough(), "copy"), null);             // a copy shares the program id
        Assert.Equal(2, rig.Wires.data.connections.Count);
    }

    [Fact]
    public void ADifferentProgramDropsEveryWire()
    {
        var (rig, socket) = WiredSocket();
        socket.Insert(rig.Tube(PassThrough()), null);
        socket.TakeTube();
        socket.Insert(rig.Tube(PassThrough(3), "other"), null);
        Assert.Empty(rig.Wires.data.connections);
    }

    [Fact]
    public void AFreshSocketOnlyLosesWiresOnPinsTheTubeDoesNotHave()
    {
        var (rig, socket) = WiredSocket();
        rig.Connection(At(3));
        rig.Wire(At(0), 5, At(3), 0);                                      // pin 5: not on a two-pin tube
        socket.Insert(rig.Tube(PassThrough()), null);
        Assert.Equal(2, rig.Wires.data.connections.Count);
        Assert.DoesNotContain(rig.Wires.data.connections, w => w.pos1.index == 5 || w.pos2.index == 5);
    }

    [Fact]
    public void TheBedKeepsTheOrientationOnlyForTheProgramThatWasThere()
    {
        var (rig, socket) = WiredSocket();
        var tube = rig.Tube(PassThrough());
        socket.Insert(tube, null);
        socket.TakeTube();
        var onBed = new BlockSelection { Position = At(0), SelectionBoxIndex = BlockTubeSocket.BedBox, HitPosition = new Vec3d(.5, .1, .9) };
        var onPlate = new BlockSelection { Position = At(0), SelectionBoxIndex = BlockTubeSocket.PlateBox, HitPosition = new Vec3d(.5, .1, .9) };
        Assert.Null(socket.ChosenOrientation(onBed, tube));
        Assert.Equal(BlockFacing.SOUTH, socket.ChosenOrientation(onPlate, tube));
        Assert.Equal(BlockFacing.SOUTH, socket.ChosenOrientation(onBed, rig.Tube(PassThrough(3), "other")));
    }

    [Fact]
    public void PuttingTheSameTubeBackOnTheBedKeepsTheOrientation()
    {
        var (rig, socket) = WiredSocket();
        var tube = rig.Tube(PassThrough());
        socket.Insert(tube, null);
        socket.TakeTube();
        var onBed = new BlockSelection { Position = At(0), SelectionBoxIndex = BlockTubeSocket.BedBox, HitPosition = new Vec3d(.5, .1, .9) };
        // what Interact does, in its order: decide, then take the stack out of the hand
        var turnTo = socket.ChosenOrientation(onBed, tube);
        socket.Insert(tube, turnTo);
        Assert.Null(turnTo);
        Assert.Equal(2, rig.Wires.data.connections.Count);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void ATagStripLiesOutsideItsPinAndReadsAlongItWhicheverWayUp(int pin)
    {
        const float width = .14f, gap = .5f / 16, lift = .07f;
        var d = SocketOrientation.TagSide[pin].Normalf;
        var c = SocketOrientation.PinCentre(pin);
        foreach (bool flip in new[] { false, true })
        {
            var (origin, right, up, length) = SocketOrientation.TagStrip(pin, 4f, flip, width, gap, lift);
            Assert.Equal(width * 4, length, 4);
            // reading direction and top lie in the plate and are at right angles; the text reads up x normal
            Assert.Equal(0, up.Y, 4); Assert.Equal(0, right.Y, 4);
            Assert.Equal(0, right.X * up.X + right.Z * up.Z, 4);
            Assert.Equal(-up.Z, right.X, 4); Assert.Equal(up.X, right.Z, 4);
            // the strip's corners: it starts just past the pin's outer face and runs outwards, centred on the pin
            var corners = new[] { origin, origin + right * length, origin - up * width, origin + right * length - up * width };
            float along(Vec3f p) => (p.X - c.X) * d.X + (p.Z - c.Z) * d.Z;
            float across(Vec3f p) => (p.X - c.X) * up.X + (p.Z - c.Z) * up.Z;
            Assert.Equal(3 / 16f + gap, corners.Min(along), 3);   // from the pin's middle: past the socket's edge; float sums land a hair off at 4 places
            Assert.Equal(3 / 16f + gap + length, corners.Max(along), 3);
            Assert.Equal(-width / 2, corners.Min(across), 3);
            Assert.Equal(width / 2, corners.Max(across), 3);
        }
        // flipping turns the text round, nothing else
        var a = SocketOrientation.TagStrip(pin, 4f, false, width, gap, lift);
        var b = SocketOrientation.TagStrip(pin, 4f, true, width, gap, lift);
        Assert.Equal(-a.up.X, b.up.X, 4); Assert.Equal(-a.right.X, b.right.X, 4);
    }
}
