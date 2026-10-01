using Newtonsoft.Json.Linq;
using SignalsTubes.src.circuit;
using SignalsTubes.src.schematic;

namespace SignalsTubes.Tests;

public class SchematicTests
{
    // Threshold NOT: in -> valve grid, source -> cathode, anode -> resistor 14 -> out; an inlined AND-ish group and a soldered box.
    private static CircuitProgram Sample()
    {
        var p = new CircuitProgram { NodeCount = 9 };
        p.Components.Add(new Component(ComponentKind.Source, 15, 1));
        p.Components.Add(new Component(ComponentKind.Valve, 0, 0, 1, 2));
        p.Links.Add(new Link(2, 3, 14, 14));
        // group 0: inlined tube with a buffer and a delay, its pins on nodes 3 (in) and 5 (out)
        p.Groups.Add(new Group { Name = "Zpožďovač", Description = "čeká", Pins = { new GroupPin { Index = 0, Role = PinRole.Input, Node = 3 }, new GroupPin { Index = 1, Role = PinRole.Output, Node = 5 } } });
        p.Components.Add(new Component(ComponentKind.Buffer, 0, 3, 4) { Group = 0 });
        p.Components.Add(new Component(ComponentKind.Delay, 2, 4, 5) { Group = 0 });
        // group 1: soldered tube on nodes 5 (in) and 6 (out)
        p.Groups.Add(new Group { Name = "Cizí", Ref = "prog-x", Pins = { new GroupPin { Index = 0, Role = PinRole.Input, Node = 5 }, new GroupPin { Index = 1, Role = PinRole.Output, Node = 6 } } });
        p.Components.Add(new Component(ComponentKind.Tube, 0, 5, 6) { Ref = "prog-x", Group = 1 });
        p.Pins.Add(Pin.Input(0, 0));
        p.Pins.Add(Pin.Output(1, 6));
        return p;
    }

    private static SchematicLevel.Context Ctx(string viewer, bool lockView) => new()
    {
        ViewerUid = viewer,
        Resolve = id => id == "prog-x" ? new CircuitProgram { NodeCount = 2, Components = { new Component(ComponentKind.Buffer, 0, 0, 1) }, Pins = { Pin.Input(0, 0), Pin.Output(1, 1) } } : null,
        References = id => id == "prog-x" ? new SchematicLevel.ReferenceInfo("author-x", lockView, "Cizí") : null
    };

    [Fact]
    public void RootLevelListsOwnPartsResistorsBoxesAndPins()
    {
        var level = SchematicLevel.Build(Sample(), "Test", Array.Empty<string>(), Ctx("me", true));
        Assert.False((bool)level["locked"]);
        Assert.Equal(new[] { "source", "valve", "resistor" }, ((JArray)level["parts"]).Select(p => (string)p["kind"]));
        var boxes = (JArray)level["boxes"];
        Assert.Equal(2, boxes.Count);
        Assert.False((bool)boxes[0]["soldered"]); Assert.True((bool)boxes[0]["openable"]); Assert.Equal("g:0", (string)boxes[0]["step"]);
        Assert.True((bool)boxes[1]["soldered"]); Assert.False((bool)boxes[1]["openable"]); Assert.Equal("r:prog-x", (string)boxes[1]["step"]);
        Assert.Equal(2, ((JArray)level["pins"]).Count);
        Assert.Equal(new[] { "Test" }, ((JArray)level["crumbs"]).Select(c => (string)c));
    }

    [Fact]
    public void GroupLevelShowsItsPartsAndPinsAndSolderedLevelRespectsTheViewLock()
    {
        var group = SchematicLevel.Build(Sample(), "Test", new[] { "g:0" }, Ctx("me", true));
        Assert.Equal(new[] { "buffer", "delay" }, ((JArray)group["parts"]).Select(p => (string)p["kind"]));
        Assert.Empty((JArray)group["boxes"]);
        Assert.Equal(new[] { "Test", "Zpožďovač" }, ((JArray)group["crumbs"]).Select(c => (string)c));

        var locked = SchematicLevel.Build(Sample(), "Test", new[] { "r:prog-x" }, Ctx("me", true));
        Assert.True((bool)locked["locked"]);
        var open = SchematicLevel.Build(Sample(), "Test", new[] { "r:prog-x" }, Ctx("author-x", true));
        Assert.False((bool)open["locked"]);
        Assert.Equal("buffer", (string)((JArray)open["parts"])[0]["kind"]);
        var unlocked = SchematicLevel.Build(Sample(), "Test", new[] { "r:prog-x" }, Ctx("me", false));
        Assert.False((bool)unlocked["locked"]);
    }

    [Fact]
    public void LayoutFlowsLeftToRightAndWiresEveryPort()
    {
        var level = SchematicLevel.Build(Sample(), "Test", Array.Empty<string>(), Ctx("me", true));
        var layout = SchematicLayout.From(level);
        var input = layout.Elements.Single(e => e.IsPin && e.PinRole == "input");
        var output = layout.Elements.Single(e => e.IsPin && e.PinRole == "output");
        Assert.Equal(0, input.Column);
        Assert.True(output.Column > layout.Elements.Where(e => !e.IsPin).Max(e => e.Column));
        var valve = layout.Elements.Single(e => e.Kind == "valve");
        var resistor = layout.Elements.Single(e => e.Kind == "resistor");
        var delayBox = layout.Elements.Single(e => e.IsBox && !e.Soldered);
        var soldered = layout.Elements.Single(e => e.IsBox && e.Soldered);
        Assert.True(valve.Column < resistor.Column && resistor.Column < delayBox.Column && delayBox.Column < soldered.Column);
        Assert.All(layout.Elements, e => Assert.True(e.X >= 0 && e.Y >= 0 && e.X + e.W <= layout.Width && e.Y + e.H <= layout.Height));
        foreach (var e in layout.Elements)
            foreach (var p in e.Ports)
            {
                var net = layout.Nets.Single(n => n.Node == p.Node);
                Assert.Contains(net.Segments, s => (Math.Abs(s.x1 - p.X) < 1e-6 && Math.Abs(s.y1 - p.Y) < 1e-6) || net.Segments.Count == 0);
            }
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "signalstubes-layout.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new
        {
            layout.Width, layout.Height,
            elements = layout.Elements.Select(e => new { e.Id, e.Kind, e.Label, e.X, e.Y, e.W, e.H, ports = e.Ports.Select(p => new { p.X, p.Y, p.Side, p.Name }) }),
            nets = layout.Nets.Select(n => new { n.Node, n.Segments, n.Junctions })
        }));
    }

    [Fact]
    public void LayoutDumpAndWithNotBehind()
    {
        // pins 5, 7 -> soldered AND (out node 2) -> valve grid; source -> cathode; anode -> resistor 14 -> pin 4
        var p = new CircuitProgram { NodeCount = 6 };
        p.Groups.Add(new Group { Name = "Kubovo AND", Ref = "and", Pins = { new GroupPin { Index = 5, Role = PinRole.Input, Node = 0 }, new GroupPin { Index = 7, Role = PinRole.Input, Node = 1 }, new GroupPin { Index = 2, Role = PinRole.Output, Node = 2 } } });
        p.Components.Add(new Component(ComponentKind.Tube, 0, 0, 1, 2) { Ref = "and", Group = 0 });
        p.Components.Add(new Component(ComponentKind.Source, 15, 3));
        p.Components.Add(new Component(ComponentKind.Valve, 0, 2, 3, 4));
        p.Links.Add(new Link(4, 5, 14, 14));
        p.Pins.Add(Pin.Input(5, 0)); p.Pins.Add(Pin.Input(7, 1)); p.Pins.Add(Pin.Output(4, 5));
        var level = SchematicLevel.Build(p, "T", Array.Empty<string>(), Ctx("me", true));
        var layout = SchematicLayout.From(level);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "signalstubes-layout2.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new
        {
            layout.Width, layout.Height,
            elements = layout.Elements.Select(e => new { e.Id, e.Kind, e.Label, e.X, e.Y, e.W, e.H, ports = e.Ports.Select(q => new { q.X, q.Y, q.Side, q.Name }) }),
            nets = layout.Nets.Select(n => new { n.Node, n.Segments, n.Junctions })
        }));
        Assert.Equal(1, ((JArray)level["parts"]).Count(x => (string)x["kind"] == "resistor"));
    }

    [Fact]
    public void LayoutDumpToggleMemory()
    {
        // pin 4 -> actuator trigger, which is also the switch's right terminal b (feedback); source -> a; pin 6 on b
        var p = new CircuitProgram { NodeCount = 2 };
        p.Components.Add(new Component(ComponentKind.Toggle, 0, 1, 0, 1));
        p.Components.Add(new Component(ComponentKind.Source, 15, 0));
        p.Pins.Add(Pin.Input(4, 1)); p.Pins.Add(Pin.Output(6, 1));
        var level = SchematicLevel.Build(p, "T", Array.Empty<string>(), Ctx("me", true));
        var layout = SchematicLayout.From(level);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "signalstubes-layout3.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new
        {
            layout.Width, layout.Height,
            elements = layout.Elements.Select(e => new { e.Id, e.Kind, e.Label, e.X, e.Y, e.W, e.H, ports = e.Ports.Select(q => new { q.X, q.Y, q.Side, q.Name }) }),
            nets = layout.Nets.Select(n => new { n.Node, n.Segments, n.Junctions })
        }));
    }
}
