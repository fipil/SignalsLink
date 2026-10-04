using SignalsTubes.src.circuit;
using SignalsTubes.src.programtube;
using SignalsTubes.Tests.fidelity;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SignalsTubes.Tests;

/// <summary>Export to JSON and back: texts, locks, the program, referenced programs under new ids, soldered tubes, the importer as author.</summary>
public class TubeTransferTests
{
    private static CircuitProgram Inverter() => ProgramCodec.FromJson(
        "{\"v\":1,\"n\":3,\"links\":[],\"parts\":[{\"k\":\"source\",\"n\":[1],\"p\":15},{\"k\":\"valve\",\"n\":[0,1,2]}],\"pins\":[{\"i\":0,\"r\":\"in\",\"n\":0,\"name\":\"A\"},{\"i\":1,\"r\":\"out\",\"n\":2,\"name\":\"not A\"}]}");

    [Fact]
    public void ATubeWithASolderedReferenceSurvivesTheRoundTrip()
    {
        var rig = new SignalsRig();
        string innerId = rig.Store.Put(Inverter());
        // outer program: one Tube part referring to the inverter, wired pin 0 in -> tube -> pin 1 out
        var outer = ProgramCodec.FromJson("{\"v\":1,\"n\":2,\"links\":[],\"parts\":[{\"k\":\"tube\",\"n\":[0,1],\"ref\":\"" + innerId + "\"}],\"pins\":[{\"i\":0,\"r\":\"in\",\"n\":0},{\"i\":1,\"r\":\"out\",\"n\":1}]}");
        var tube = rig.Tube(outer, "Composite");
        tube.Attributes.SetString(TubeProgram.DescriptionKey, "two in one");
        tube.Attributes.SetBool(TubeProgram.LockViewKey, true);
        TubeProgram.SetSoldered(tube, new[] { rig.Tube(Inverter(), "Inner") });

        var json = TubeTransfer.Export(tube, rig.Api);
        Assert.Equal("Composite", (string)json["name"]);
        Assert.True(json["programs"][innerId] != null);   // the referenced program travels along
        Assert.Single((Newtonsoft.Json.Linq.JArray)json["soldered"]);

        // a different server: the old id means nothing there, a fresh store assigns new ones
        var other = new SignalsRig();
        var imported = TubeTransfer.Import(json, (ICoreServerAPI)other.Api, "uid-2", "Bob");

        Assert.Equal("Composite", imported.Attributes.GetString(TubeProgram.NameKey));
        Assert.Equal("two in one", imported.Attributes.GetString(TubeProgram.DescriptionKey));
        Assert.True(TubeProgram.LockView(imported));
        Assert.Equal("uid-2", TubeProgram.Author(imported));
        Assert.Equal("Bob", TubeProgram.AuthorName(imported));
        var program = TubeProgram.Get(imported, other.Api);
        Assert.NotNull(program);
        string newRef = program.Components.Single(c => c.Kind == ComponentKind.Tube).Ref;
        Assert.NotNull(other.Store.Get(newRef));                                   // the reference resolves on the new server
        Assert.Equal(2, other.Store.Get(newRef).Components.Count);                 // and is the inverter
        var soldered = TubeProgram.Soldered(imported, other.World);
        Assert.Equal("Inner", Assert.Single(soldered).Attributes.GetString(TubeProgram.NameKey));
        Assert.Equal("uid-2", TubeProgram.Author(soldered[0]));
    }
}
