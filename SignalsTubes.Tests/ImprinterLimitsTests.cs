using System.Reflection;
using Newtonsoft.Json.Linq;
using SignalsTubes.src.circuit;
using SignalsTubes.src.imprint;
using SignalsTubes.src.programtube;
using SignalsTubes.src.socket;
using SignalsTubes.Tests.fidelity;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace SignalsTubes.Tests;

public class ImprinterLimitsTests
{
    private const string Locale = "imprinter-limit-test";
    static ImprinterLimitsTests()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../SignalsTubes/assets/signalstubes/lang/cs.json"));
        var translations = JObject.Parse(File.ReadAllText(path));
        Lang.AvailableLanguages[Locale] = Fake.Of<ITranslationService>((m, a) => m.Name switch {
            "HasTranslation" => translations[((string)a[0]).Split(':')[1]] != null,
            "Get" => string.Format((string)translations[((string)a[0]).Split(':')[1]], (object[])a[1]),
            _ => Fake.Unhandled });
    }

    private sealed class TestPlayer : Vintagestory.Server.ServerPlayer, IServerPlayer
    {
        public readonly List<string> Messages = new();
        public TestPlayer() : base(null, null) { LanguageCode = Locale; }
        protected override void Init() { }
        public override string PlayerUID => "me";
        public new void SendMessage(int groupId, string message, EnumChatType chatType, string data = null) => Messages.Add(message);
    }

    private static CircuitProgram Leaf() => new() { NodeCount = 2, Pins = new() { Pin.Input(0, 0), Pin.Output(1, 1) } };
    private static CircuitProgram Wrapper(string id)
    {
        var p = Leaf();
        p.Components.Add(new Component(ComponentKind.Tube, 0, 0, 1) { Ref = id });
        return p;
    }
    private static FieldInfo Field(string name) => typeof(BEImprinter).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);

    [Theory]
    [InlineData("size", "65536")]
    [InlineData("depth", "64")]
    [InlineData("cycle", "cyklick")]
    [InlineData("valid", "")]
    public void ImprintChecksBeforeConsumingOrOverwriting(string kind, string reason)
    {
        var rig = new SignalsRig();
        var p = Leaf();
        if (kind == "size") p.NodeCount = CircuitLimits.MaxExpandedSize;
        var nested = rig.Tube(p);
        if (kind == "depth")
            for (int i = 1; i < CircuitLimits.MaxDepth; i++)
                nested = rig.Tube(Wrapper(nested.Attributes.GetString(TubeProgram.IdKey)));
        if (kind == "cycle") p.Components.Add(new Component(ComponentKind.Tube, 0, 0, 1) { Ref = nested.Attributes.GetString(TubeProgram.IdKey) });
        // Foreign copy-locked tubes are soldered, rather than inlined.
        nested.Attributes.SetString(TubeProgram.AuthorKey, "someone-else");
        nested.Attributes.SetBool(TubeProgram.LockCopyKey, true);
        var rootPos = rig.Socket(SignalsRig.At(0));
        var nestedPos = rig.Socket(SignalsRig.At(2), nested);
        rig.Wire(rootPos, 0, nestedPos, 0);
        rig.Wire(rootPos, 1, nestedPos, 1);
        var source = (BETubeSocket)rig.World.BlockAccessor.GetBlockEntity(nestedPos);
        var sourceTube = source.Tube;
        var destination = rig.Tube(Leaf(), "Previous program");
        TubeProgram.SetSoldered(destination, new[] { rig.Tube(Leaf(), "Already soldered") });
        string oldId = destination.Attributes.GetString(TubeProgram.IdKey);
        var draft = new JObject { ["name"] = "Keep draft" };
        var imprinter = new BEImprinter { Api = rig.Api, Pos = SignalsRig.At(4), Block = new Block() };
        Field("socket").SetValue(imprinter, rootPos);
        Field("tube").SetValue(imprinter, destination);
        Field("draft").SetValue(imprinter, draft);
        var player = new TestPlayer();
        var messages = player.Messages;
        bool ok = imprinter.Imprint(player, "New program", confirmSolder: true);
        if (kind == "valid")
        {
            Assert.True(ok);
            Assert.Null(source.Tube);
            Assert.Equal(2, TubeProgram.Soldered(destination, rig.World).Count);
            Assert.Equal("New program", destination.Attributes.GetString(TubeProgram.NameKey));
        }
        else
        {
            Assert.False(ok);
            Assert.Same(sourceTube, source.Tube);
            Assert.Equal(oldId, destination.Attributes.GetString(TubeProgram.IdKey));
            Assert.Equal("Previous program", destination.Attributes.GetString(TubeProgram.NameKey));
            Assert.Single(TubeProgram.Soldered(destination, rig.World));
            Assert.Same(draft, Field("draft").GetValue(imprinter));
            string message = Assert.Single(messages);
            Assert.Contains(reason, message);
            Assert.DoesNotContain("signalstubes:", message);
        }
    }
}
