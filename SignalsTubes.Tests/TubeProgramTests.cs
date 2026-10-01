using Newtonsoft.Json.Linq;
using SignalsTubes.src.circuit;
using SignalsTubes.src.programtube;
using Vintagestory.API.Common;

namespace SignalsTubes.Tests;

public class TubeProgramTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../SignalsTubes"));
    private static readonly ItemProgramTube Tube = new() { Code = new AssetLocation("signalstubes:programtube"), MaxStackSize = 1 };

    private static IEnumerable<(string name, string circuit)> CreativeSamples()
    {
        var item = JObject.Parse(File.ReadAllText(Path.Combine(Root, "assets/signalstubes/itemtypes/programtube.json")));
        foreach (var stack in item["creativeinventoryStacksByType"]["*-fire"][0]["stacks"])
            if (stack["attributes"] != null)
                yield return ((string)stack["attributes"]["programName"], (string)stack["attributes"]["circuit"]);
    }

    [Fact]
    public void CreativeSamplesAreValidProgramsWithDistinctFingerprints()
    {
        var prints = new HashSet<string>();
        foreach (var (name, circuit) in CreativeSamples())
        {
            var program = ProgramCodec.FromJson(circuit);
            Assert.True(prints.Add(CircuitFingerprint.Compute(program)), name);
        }
        Assert.Equal(4, prints.Count);
    }

    [Fact]
    public void InverterSampleInvertsAndClockSampleTicks()
    {
        var samples = CreativeSamples().ToDictionary(s => s.name, s => ProgramCodec.FromJson(s.circuit));
        var inverter = new CircuitSimulator(samples["Inverter"]);
        inverter.SetInput(0, 15);
        inverter.Step(); inverter.Step();
        Assert.Equal(0, inverter.GetOutput(1));
        inverter.SetInput(0, 0);
        inverter.Step(); inverter.Step();
        Assert.Equal(15, inverter.GetOutput(1));

        var clock = new CircuitSimulator(samples["Clock"]);
        clock.Step(); byte a = clock.GetOutput(0);
        clock.Step(); byte b = clock.GetOutput(0);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void StackProgramIsParsedOnceAndSharedButSimulatorsDoNotInterfere()
    {
        var (_, circuit) = CreativeSamples().First(s => s.name == "Memory");
        var stack = new ItemStack(Tube);
        stack.Attributes.SetString(ProgramCodec.AttributeKey, circuit);
        var program = TubeProgram.Get(stack, null);
        Assert.Same(program, TubeProgram.Get(stack.Clone(), null));
        Assert.Equal(0b11, TubeVisuals.PinMask(stack));
        Assert.Equal(2, TubeVisuals.PinCount(stack));

        var first = new CircuitSimulator(program);
        var second = new CircuitSimulator(program);
        first.SetInput(0, 15); first.Step(); first.Step();
        second.Step(); second.Step();
        Assert.Equal(15, first.GetOutput(1));
        Assert.Equal(0, second.GetOutput(1));
        Assert.Equal(0, program.Components[1].Param);
    }

    [Fact]
    public void BlankAndBrokenStacksFallBackToPinCount()
    {
        var blank = new ItemStack(Tube);
        Assert.True(TubeProgram.IsBlank(blank));
        Assert.Equal(0xFF, TubeVisuals.PinMask(blank));
        blank.Attributes.SetInt("pinCount", 3);
        Assert.Equal(0b111, TubeVisuals.PinMask(blank));

        var broken = new ItemStack(Tube);
        broken.Attributes.SetString(ProgramCodec.AttributeKey, "{\"v\":1,\"n\":0,\"links\":[[0,0,0,0]],\"parts\":[],\"pins\":[]}");
        Assert.Null(TubeProgram.Get(broken, null));
        Assert.Equal(0xFF, TubeVisuals.PinMask(broken));
    }

    [Fact]
    public void AppendScalesMovesAndKeepsGlassLast()
    {
        var tube = Newtonsoft.Json.JsonConvert.DeserializeObject<Shape>(File.ReadAllText(Path.Combine(Root, "assets/signalstubes/shapes/item/programtube.json")));
        var target = new Shape { Elements = new[] { new ShapeElement { Name = "plate", From = new double[3], To = new double[] { 16, 1, 16 } } }, Textures = new() };
        TubeVisuals.Append(target, tube, .5f, new Vintagestory.API.MathTools.Vec3f(4, 1, 4));
        var foot = target.Elements.Single(e => e.Name == "tube_tube_base");
        Assert.Equal(new double[] { 6, 2, 6 }, foot.From);
        Assert.Equal(new double[] { 10, 2.6, 10 }, foot.To);
        int firstGlass = Array.FindIndex(target.Elements, e => e.RenderPass == 3);
        Assert.All(target.Elements.Skip(firstGlass), e => Assert.Equal(3, e.RenderPass));
        Assert.Equal(tube.Elements.Length + 1, target.Elements.Length);
        Assert.Contains("tube-glass", target.Textures.Keys);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(5, 5)]
    [InlineData(9, 8)]
    public void BlankTubeShowsPinCountPinsAndNoGlyph(int requested, int expected)
    {
        var template = Newtonsoft.Json.JsonConvert.DeserializeObject<Shape>(File.ReadAllText(Path.Combine(Root, "assets/signalstubes/shapes/item/programtube.json")));
        var stack = new ItemStack(Tube);
        stack.Attributes.SetInt("pinCount", requested);
        var shape = TubeVisuals.Build(template, stack);
        Assert.Equal(expected, shape.Elements.Count(e => e.Name.StartsWith("tube_pin_")));
        Assert.DoesNotContain(shape.Elements, e => e.Name.StartsWith("glyph_"));
        int glass = Array.FindIndex(shape.Elements, e => e.Name.StartsWith("bulb_"));
        Assert.All(shape.Elements.Skip(glass), e => Assert.StartsWith("bulb_", e.Name));
    }

    [Fact]
    public void ProgrammedTubeShowsItsPinsAndAMirroredGlyphThatGlowsOnlyWhenLit()
    {
        var template = Newtonsoft.Json.JsonConvert.DeserializeObject<Shape>(File.ReadAllText(Path.Combine(Root, "assets/signalstubes/shapes/item/programtube.json")));
        var (_, circuit) = CreativeSamples().First(s => s.name == "Delay line");
        var stack = new ItemStack(Tube);
        stack.Attributes.SetString(ProgramCodec.AttributeKey, circuit);
        var shape = TubeVisuals.Build(template, stack);
        Assert.Equal(new[] { "tube_pin_0", "tube_pin_1", "tube_pin_2" }, shape.Elements.Where(e => e.Name.StartsWith("tube_pin_")).Select(e => e.Name));
        var glyph = shape.Elements.Where(e => e.Name.StartsWith("glyph_")).ToArray();
        Assert.NotEmpty(glyph);
        Assert.All(glyph, e => Assert.All(e.FacesResolved, f => Assert.Equal(0, f.Glow)));
        // Every run mirrors about x = 8.
        foreach (var e in glyph)
            Assert.Contains(glyph, m => Math.Abs(m.From[0] - (16 - e.To[0])) < 1e-6 && Math.Abs(m.To[0] - (16 - e.From[0])) < 1e-6 && m.From[1] == e.From[1]);
        var cells = TubeVisuals.GlyphCells(Convert.FromHexString(TubeVisuals.Fingerprint(stack)));
        Assert.True(Enumerable.Range(0, 7).Sum(r => Enumerable.Range(0, 5).Count(c => cells[r, c])) >= 8);

        var litShape = TubeVisuals.Build(template, stack, lit: true);
        Assert.All(litShape.Elements.Where(e => e.Name.StartsWith("glyph_")), e => Assert.All(e.FacesResolved, f => Assert.Equal(TubeVisuals.GlyphGlow, f.Glow)));
        Assert.NotEqual(TubeVisuals.MeshKey(stack, false), TubeVisuals.MeshKey(stack, true));
    }

    [Fact]
    public void ImprintedTubeCarriesOnlyThePublicPartAndTheStoreKeepsTheProgram()
    {
        var (_, circuit) = CreativeSamples().First(s => s.name == "Delay line");
        var program = ProgramCodec.FromJson(circuit);
        program.Pins[0].Name = "Trigger";
        var store = new ProgramStore();
        var stack = new ItemStack(Tube);
        TubeProgram.Set(stack, program, store, "uid-1", "Fipil", lockCopy: true, lockView: false);

        Assert.False(stack.Attributes.HasAttribute(ProgramCodec.AttributeKey));
        Assert.Null(TubeProgram.Get(stack, null));                       // no server api, no program
        Assert.Same(program, store.Get(stack.Attributes.GetString(TubeProgram.IdKey)));
        Assert.Equal(CircuitFingerprint.Compute(program), TubeProgram.Fingerprint(stack));
        var pins = TubeProgram.Pins(stack);
        Assert.Equal(new[] { 0, 1, 2 }, pins.Select(p => p.Index));
        Assert.Equal("Trigger", pins[0].Name);
        Assert.Equal(PinRole.Delay, pins[2].Role);
        Assert.Equal(0b111, TubeVisuals.PinMask(stack));
        Assert.True(TubeProgram.LockCopy(stack));
        Assert.False(TubeProgram.LockView(stack));
        Assert.True(TubeProgram.IsAuthor(stack, "uid-1"));
        Assert.False(TubeProgram.IsAuthor(stack, "uid-2"));

        TubeProgram.Clear(stack);
        Assert.True(TubeProgram.IsBlank(stack));
        Assert.Equal(0xFF, TubeVisuals.PinMask(stack));
        Assert.Null(TubeProgram.Author(stack));
    }
}
