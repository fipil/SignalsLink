using signals.src.hangingwires;
using signals.src.signalNetwork;
using SignalsTubes.src.circuit;
using Vintagestory.API.MathTools;
using static SignalsTubes.Tests.fidelity.SignalsRig;

namespace SignalsTubes.Tests.fidelity;

/// <summary>
/// A world being loaded: the wires are already in the hanging-wires data when the devices come up,
/// one by one, in whatever order the chunks happen to load. Every order must end in a working network.
/// </summary>
public class WorldLoadTests
{
    private static CircuitProgram PassThrough() => ProgramCodec.FromJson(
        "{\"v\":1,\"n\":2,\"links\":[[0,1,0,0]],\"parts\":[],\"pins\":[{\"i\":0,\"r\":\"in\",\"n\":0},{\"i\":1,\"r\":\"out\",\"n\":1}]}");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ASocketWithATubeWorksWhicheverChunkLoadsFirst(bool socketFirst)
    {
        var rig = new SignalsRig();
        // wires as saved in the world: socket pin 0 <- source, socket pin 1 -> lamp (a connection block)
        rig.Wires.data.connections.Add(new WireConnection(new NodePos(At(1), 0), new NodePos(At(0), 0)));
        rig.Wires.data.connections.Add(new WireConnection(new NodePos(At(0), 1), new NodePos(At(2), 0)));
        var tube = rig.Tube(PassThrough(), "pass");
        if (socketFirst)
        {
            rig.Socket(At(0), tube);
            rig.SourceBlock(At(1), 9);
            rig.Connection(At(2));
        }
        else
        {
            rig.SourceBlock(At(1), 9);
            rig.Connection(At(2));
            rig.Socket(At(0), tube);
        }
        for (int i = 0; i < 4; i++) rig.Tick();
        Assert.Equal(9, rig.Level(At(2), 0));   // the lamp sees the tube's output
        rig.Drive(At(1), 0, 3);
        for (int i = 0; i < 4; i++) rig.Tick();
        Assert.Equal(3, rig.Level(At(2), 0));   // and follows the input
    }
}
