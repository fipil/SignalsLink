using Newtonsoft.Json.Linq;
using SignalsTubes.src.schematic;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace SignalsTubes.src.imprint;

/// <summary>
/// The imprinter's dialog: name and description of the tube, pin names, which switches and delays
/// go out to pins, what is refused and why. Built from a state the server sends on opening; the
/// imprint request carries the edits back.
/// </summary>
public class GuiDialogImprinter : GuiDialogBlockEntity
{
    public const int NameMax = 32, DescriptionMax = 120, PinNameMax = 24;
    private JObject state;
    private bool confirming;
    private readonly List<string> path = new();
    private GuiElementSchematic canvas;

    public GuiDialogImprinter(BlockPos pos, ICoreClientAPI capi) : base(Lang.Get("signalstubes:dialog-title"), pos, capi) { }

    // Closing without imprinting hands the typed values to the imprinter as a draft.
    public override void OnGuiClosed()
    {
        base.OnGuiClosed();
        if (state == null || (bool?)state["done"] == true) return;
        var request = confirming ? edits : SingleComposer?.GetTextInput("name") == null ? null : Collect();
        if (request == null) return;
        request["draft"] = true;
        capi.Network.GetChannel(ImprinterNetwork.Channel).SendPacket(new ImprintPacket { X = BlockEntityPosition.X, Y = BlockEntityPosition.Y, Z = BlockEntityPosition.Z, Json = Newtonsoft.Json.JsonConvert.SerializeObject(request, Newtonsoft.Json.Formatting.None) });
    }

    public void SetState(string json)
    {
        state = JObject.Parse(json);
        confirming = false;
        if ((bool?)state["done"] == true) { TryClose(); return; }
        Compose();
        if (!IsOpened()) TryOpen();
    }

    private void Compose()
    {
        var font = CairoFont.WhiteSmallText();
        var label = CairoFont.WhiteSmallText().WithColor(new[] { .8, .8, .8, 1 });
        var red = CairoFont.WhiteSmallText().WithColor(new[] { 1, .45, .45, 1 });
        const double w = 460, row = 30, pad = 10, canvasW = 600, canvasH = 440, gap = 20;
        double y = 40;
        var schematic = state["schematic"] as JObject;
        var bg = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bg.BothSizing = ElementSizing.FitToChildren;
        var dialog = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
        var composer = capi.Gui.CreateCompo("signalstubes-imprinter-" + BlockEntityPosition, dialog)
            .AddShadedDialogBG(bg)
            .AddDialogTitleBar(Lang.Get("signalstubes:dialog-title"), () => TryClose())
            .BeginChildElements(bg);

        if (confirming)
        {
            // Soldering warning: these tubes leave their sockets for good (until the new tube is erased).
            composer.AddStaticText(Lang.Get("signalstubes:dialog-solder-warning"), red, ElementBounds.Fixed(0, y + 4, w, row));
            y += row;
            foreach (var name in (JArray)state["solder"])
            {
                composer.AddStaticText("  " + (string)name, font, ElementBounds.Fixed(0, y + 4, w, row));
                y += row;
            }
            y += pad;
            composer.AddSwitch(null, ElementBounds.Fixed(0, y, 30, 30), "noWarn", 24);
            composer.AddStaticText(Lang.Get("signalstubes:dialog-no-warn"), font, ElementBounds.Fixed(40, y + 4, w - 40, row));
            y += row + pad;
            composer.AddSmallButton(Lang.Get("signalstubes:dialog-imprint"), () => Send(true), ElementBounds.Fixed(w - 260, y, 150, row));
            composer.AddSmallButton(Lang.Get("signalstubes:dialog-back"), () => { confirming = false; Compose(); return true; }, ElementBounds.Fixed(w - 100, y, 100, row));
            composer.EndChildElements();
            SingleComposer = composer.Compose();
            return;
        }

        composer.AddStaticText(Lang.Get("signalstubes:dialog-name"), label, ElementBounds.Fixed(0, y + 4, 110, row));
        composer.AddTextInput(ElementBounds.Fixed(120, y, w - 120, row), null, font, "name");
        y += row + pad;
        composer.AddStaticText(Lang.Get("signalstubes:dialog-description"), label, ElementBounds.Fixed(0, y + 4, 110, row));
        composer.AddTextArea(ElementBounds.Fixed(120, y, w - 120, row * 2), null, font, "description");
        y += row * 2 + pad;

        var pins = state["pins"] as JArray ?? new JArray();
        composer.AddStaticText(Lang.Get("signalstubes:dialog-pins", pins.Count, 8), label, ElementBounds.Fixed(0, y + 4, w, row));
        y += row;
        foreach (var pin in pins)
        {
            int index = (int)pin["i"];
            string role = Lang.Get("signalstubes:role-" + (string)pin["r"]);
            composer.AddStaticText($"{index + 1}", font, ElementBounds.Fixed(10, y + 4, 30, row));
            composer.AddStaticText(role, label, ElementBounds.Fixed(40, y + 4, 90, row));
            composer.AddTextInput(ElementBounds.Fixed(140, y, w - 140, row), null, font, "pin" + index);
            y += row + 4;
        }
        y += pad;

        var adjustables = state["adj"] as JArray ?? new JArray();
        if (adjustables.Count > 0)
        {
            composer.AddStaticText(Lang.Get("signalstubes:dialog-adjustables"), label, ElementBounds.Fixed(0, y + 4, w, row));
            y += row;
            int n = 0;
            foreach (var adj in adjustables)
            {
                var target = new BlockPos((int)adj["x"], (int)adj["y"], (int)adj["z"]);
                composer.AddSwitch(_ => ImprinterNetwork.ToggleMark(capi, BlockEntityPosition, target), ElementBounds.Fixed(10, y, 30, 30), "adj" + n, 24);
                composer.AddStaticText(Lang.Get("signalstubes:part-" + (string)adj["kind"]) + " " + Where(adj), font, ElementBounds.Fixed(50, y + 4, w - 50, row));
                y += row + 4; n++;
            }
            y += pad;
        }

        var refused = state["refused"] as JArray ?? new JArray();
        if (refused.Count > 0)
        {
            composer.AddStaticText(Lang.Get("signalstubes:dialog-refused"), red, ElementBounds.Fixed(0, y + 4, w, row));
            y += row;
            foreach (var r in refused.Take(6))
            {
                composer.AddStaticText(Where(r) + ": " + Lang.Get("signalstubes:refuse-" + (string)r["reason"]), red, ElementBounds.Fixed(10, y + 4, w - 10, row));
                y += row;
            }
            y += pad;
        }

        if ((bool)state["foreignLocked"])
        {
            composer.AddStaticText(Lang.Get("signalstubes:dialog-foreign-locked", (string)state["author"]), red, ElementBounds.Fixed(0, y + 4, w, row));
            y += row + pad;
        }
        else if ((bool)state["hasTube"])
        {
            composer.AddSwitch(null, ElementBounds.Fixed(0, y, 30, 30), "lockCopy", 24);
            composer.AddStaticText(Lang.Get("signalstubes:dialog-lock-copy"), font, ElementBounds.Fixed(40, y + 4, w / 2 - 40, row));
            composer.AddSwitch(null, ElementBounds.Fixed(w / 2, y, 30, 30), "lockView", 24);
            composer.AddStaticText(Lang.Get("signalstubes:dialog-lock-view"), font, ElementBounds.Fixed(w / 2 + 40, y + 4, w / 2 - 40, row));
            y += row + pad;
        }
        string author = (string)state["author"];
        bool linked = (bool)state["linked"];
        string info = (linked ? Lang.Get("signalstubes:dialog-parts", (int)state["parts"]) : "") + (author.Length > 0 ? "   " + Lang.Get("signalstubes:programtube-author", author) : "");
        if (info.Trim().Length > 0) { composer.AddStaticText(info.Trim(), label, ElementBounds.Fixed(0, y + 4, w, row)); y += row; }
        if ((bool)state["hasTube"] && !(bool)state["blank"] && !(bool)state["foreignLocked"])
            composer.AddSmallButton(Lang.Get("signalstubes:dialog-erase"), OnErase, ElementBounds.Fixed(0, y, 100, row), EnumButtonStyle.Small);
        if (linked)
            composer.AddSmallButton(Lang.Get("signalstubes:dialog-imprint"), OnImprint, ElementBounds.Fixed(w - 260, y, 150, row), (bool)state["ok"] ? EnumButtonStyle.Normal : EnumButtonStyle.Small, "imprint");
        composer.AddSmallButton(Lang.Get("signalstubes:dialog-close"), () => TryClose(), ElementBounds.Fixed(w - 100, y, 100, row));

        if (schematic != null)
        {
            // Breadcrumbs with a back button above the drawing, to the right of the form.
            double cx = w + gap;
            var crumbs = schematic["crumbs"] as JArray ?? new JArray();
            string trail = string.Join("  \u203A  ", crumbs.Select(c => (string)c));
            if (path.Count > 0)
                composer.AddSmallButton(Lang.Get("signalstubes:dialog-back"), () => { path.RemoveAt(path.Count - 1); Request(); return true; }, ElementBounds.Fixed(cx, 40, 80, row), EnumButtonStyle.Small);
            composer.AddStaticText(trail, label, ElementBounds.Fixed(cx + (path.Count > 0 ? 90 : 0), 44, canvasW - 90, row));
            canvas = new GuiElementSchematic(capi, ElementBounds.Fixed(cx, 40 + row + 6, canvasW, canvasH),
                step => { path.Add(step); Request(); },
                () => { if (path.Count > 0) { path.RemoveAt(path.Count - 1); Request(); } });
            composer.AddInteractiveElement(canvas, "schematic");
        }
        else canvas = null;
        composer.EndChildElements();
        SingleComposer = composer.Compose();
        canvas?.SetLevel(schematic);

        Text("name", (string)state["name"], NameMax, Lang.Get("signalstubes:dialog-name-hint"));
        var description = SingleComposer.GetTextArea("description");
        description.SetMaxLength(DescriptionMax);
        description.SetValue((string)state["description"] ?? "");
        foreach (var pin in pins)
        {
            int index = (int)pin["i"];
            Text("pin" + index, (string)pin["name"], PinNameMax, Lang.Get("signalstubes:pin-" + (string)pin["r"], index + 1));
        }
        for (int n = 0; n < adjustables.Count; n++) SingleComposer.GetSwitch("adj" + n).On = (bool)adjustables[n]["marked"];
        if (SingleComposer.GetSwitch("lockCopy") != null)
        {
            SingleComposer.GetSwitch("lockCopy").On = (bool)state["lockCopy"];
            SingleComposer.GetSwitch("lockView").On = (bool)state["lockView"];
        }
    }

    private bool OnErase()
    {
        var request = new JObject { ["erase"] = true };
        capi.Network.GetChannel(ImprinterNetwork.Channel).SendPacket(new ImprintPacket { X = BlockEntityPosition.X, Y = BlockEntityPosition.Y, Z = BlockEntityPosition.Z, Json = Newtonsoft.Json.JsonConvert.SerializeObject(request, Newtonsoft.Json.Formatting.None) });
        return true;
    }

    private void Text(string key, string value, int max, string placeholder)
    {
        var input = SingleComposer.GetTextInput(key);
        input.SetMaxLength(max);
        input.SetPlaceHolderText(placeholder);
        input.SetValue(value ?? "");
    }

    // Offset from the imprinter, so the player can find the part.
    private string Where(JToken t)
    {
        int dx = (int)t["x"] - BlockEntityPosition.X, dy = (int)t["y"] - BlockEntityPosition.Y, dz = (int)t["z"] - BlockEntityPosition.Z;
        return $"({dx:+#;-#;0}, {dy:+#;-#;0}, {dz:+#;-#;0})";
    }

    private JObject edits;   // kept across the confirmation step

    private void Request() => ImprinterNetwork.OpenDialog(capi, BlockEntityPosition, string.Join(";", path));

    private bool OnImprint()
    {
        if (SingleComposer.GetTextInput("name").GetText().Trim().Length == 0)
        {
            capi.TriggerIngameError(this, "noname", Lang.Get("signalstubes:imprint-no-name"));
            return true;
        }
        edits = Collect();
        if (((JArray)state["solder"]).Count > 0 && (bool)state["warn"])
        {
            confirming = true;
            Compose();
            return true;
        }
        return Send(false);
    }

    private bool Send(bool confirmed)
    {
        var request = edits;
        request["confirmSolder"] = confirmed;
        request["noWarn"] = confirmed && (SingleComposer.GetSwitch("noWarn")?.On ?? false);
        capi.Network.GetChannel(ImprinterNetwork.Channel).SendPacket(new ImprintPacket { X = BlockEntityPosition.X, Y = BlockEntityPosition.Y, Z = BlockEntityPosition.Z, Json = Newtonsoft.Json.JsonConvert.SerializeObject(request, Newtonsoft.Json.Formatting.None) });
        return true;
    }

    private JObject Collect()
    {
        var request = new JObject
        {
            ["name"] = SingleComposer.GetTextInput("name").GetText(),
            ["description"] = SingleComposer.GetTextArea("description").GetText(),
            ["lockCopy"] = SingleComposer.GetSwitch("lockCopy")?.On ?? false,
            ["lockView"] = SingleComposer.GetSwitch("lockView")?.On ?? false,
            ["pinNames"] = new JObject(((JArray)state["pins"]).Select(p => new JProperty(((int)p["i"]).ToString(), SingleComposer.GetTextInput("pin" + (int)p["i"]).GetText()))),
            ["exposed"] = new JArray(((JArray)state["adj"]).Where((a, n) => SingleComposer.GetSwitch("adj" + n).On)
                .Select(a => new JObject { ["x"] = a["x"], ["y"] = a["y"], ["z"] = a["z"] }))
        };
        return request;
    }
}
