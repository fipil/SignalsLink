using Cairo;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SignalsTubes.src.schematic;

/// <summary>
/// Draws one schematic level with electronic-style symbols. Hovering a nested tube shows its name
/// and description; right-clicking an openable one asks the dialog to descend into it.
/// </summary>
public class GuiElementSchematic : GuiElement
{
    private readonly Action<string> onOpen;
    private readonly Action onBack;
    private long lastClick; private SchematicLayout.Element lastClicked;
    private double fit = 1;
    private SchematicLayout layout;
    private bool locked;
    private string lockedName;
    private double scale = 1, offsetX, offsetY;
    private double zoom = 1, panX, panY;      // the viewer's own zoom and pan on top of fit-to-area
    private bool dragging; private int dragX, dragY;
    private LoadedTexture texture, hoverTexture;
    private SchematicLayout.Element hovered;

    private static readonly double[] Wire = { .72, .72, .68, 1 }, Ink = { .95, .93, .88, 1 }, Dim = { .55, .55, .5, 1 };
    private static readonly double[] Glow = { .25, .78, .74, 1 }, Box = { .18, .16, .13, 1 }, SolderedBox = { .11, .10, .09, 1 }, Red = { .9, .4, .4, 1 };

    public GuiElementSchematic(ICoreClientAPI capi, ElementBounds bounds, Action<string> onOpen, Action onBack) : base(capi, bounds)
    {
        this.onOpen = onOpen; this.onBack = onBack;
        texture = new LoadedTexture(capi);
        hoverTexture = new LoadedTexture(capi);
    }

    public void SetLevel(JObject level)
    {
        locked = (bool?)level["locked"] == true;
        lockedName = (string)level["name"];
        layout = locked ? null : SchematicLayout.From(level);
        hovered = null;
        zoom = 1; panX = panY = 0;
        Redraw();
    }

    public override void ComposeElements(Context ctxStatic, ImageSurface surface) { Bounds.CalcWorldBounds(); Redraw(); }

    private void Redraw()
    {
        int w = (int)Bounds.InnerWidth, h = (int)Bounds.InnerHeight;
        if (w <= 0 || h <= 0) return;
        var surface = new ImageSurface(Format.Argb32, w, h);
        var ctx = new Context(surface);
        ctx.SetSourceRGBA(0, 0, 0, .35);
        ctx.Rectangle(0, 0, w, h); ctx.Fill();
        if (locked) DrawLocked(ctx, w, h);
        else if (layout != null) Draw(ctx, w, h);
        generateTexture(surface, ref texture);
        ctx.Dispose(); surface.Dispose();
    }

    private void DrawLocked(Context ctx, int w, int h)
    {
        var font = CairoFont.WhiteMediumText();
        font.SetupContext(ctx);
        Color(ctx, Red);
        string text = Lang.Get("signalstubes:schematic-locked", lockedName ?? "");
        var ext = ctx.TextExtents(text);
        ctx.MoveTo((w - ext.Width) / 2, h / 2.0);
        ctx.ShowText(text);
        ctx.NewPath();
    }

    private void Draw(Context ctx, int w, int h)
    {
        fit = Math.Min(Math.Min(w / layout.Width, h / layout.Height), 1.4);
        scale = fit * zoom;
        offsetX = (w - layout.Width * scale) / 2 + panX;
        offsetY = (h - layout.Height * scale) / 2 + panY;
        ctx.Translate(offsetX, offsetY);
        ctx.Scale(scale, scale);
        ctx.LineCap = LineCap.Round;
        ctx.LineJoin = LineJoin.Round;
        var small = CairoFont.WhiteDetailText();
        small.UnscaledFontsize = 12;

        // wires first, parts over them
        ctx.LineWidth = 2;
        Color(ctx, Wire);
        foreach (var net in layout.Nets)
        {
            foreach (var s in net.Segments) { ctx.MoveTo(s.x1, s.y1); ctx.LineTo(s.x2, s.y2); }
            ctx.Stroke();
            foreach (var j in net.Junctions) { ctx.Arc(j.x, j.y, 3, 0, Math.PI * 2); ctx.Fill(); }
        }
        foreach (var e in layout.Elements)
        {
            ctx.Save();
            ctx.Translate(e.X, e.Y);
            ctx.LineWidth = 2;
            Color(ctx, Ink);
            if (e.IsPin) DrawPin(ctx, e, small);
            else if (e.IsBox) DrawBox(ctx, e, small);
            else DrawPart(ctx, e, small);
            ctx.Restore();
            // port labels: inside the box for tubes, beside the lead for parts
            foreach (var p in e.Ports)
            {
                if (p.Name == null || e.IsPin) continue;
                small.SetupContext(ctx);
                Color(ctx, e.IsBox ? Dim : Ink);
                double tw = ctx.TextExtents(p.Name).Width;
                double tx, ty;
                if (e.IsBox) { tx = p.Side == SchematicLayout.Side.Left ? p.X + 4 : p.X - tw - 4; ty = p.Y + 4; }
                else
                {
                    // outside the symbol, just above the lead, so neither the circle nor the wire runs through the letter
                    tx = p.Side == SchematicLayout.Side.Left ? p.X - tw - 3 : p.Side == SchematicLayout.Side.Right ? p.X + 3 : p.X + 5;
                    ty = p.Side == SchematicLayout.Side.Bottom ? p.Y - 2 : p.Y - 5;
                }
                ctx.MoveTo(tx, ty); ctx.ShowText(p.Name);
                ctx.NewPath();
            }
        }
    }

    private static void Color(Context ctx, double[] c) => ctx.SetSourceRGBA(c[0], c[1], c[2], c[3]);

    private static void Text(Context ctx, CairoFont font, string text, double cx, double cy)
    {
        font.SetupContext(ctx);
        var ext = ctx.TextExtents(text);
        ctx.MoveTo(cx - ext.Width / 2 - ext.XBearing, cy + ext.Height / 2);
        ctx.ShowText(text);
        ctx.NewPath();
    }

    private void DrawPin(Context ctx, SchematicLayout.Element e, CairoFont font)
    {
        bool output = e.PinRole == "output";
        // a tag with the pin number, pointing into the circuit
        Color(ctx, e.PinRole switch { "input" => new[] { .35, .7, .4, 1 }, "output" => new[] { .85, .4, .35, 1 }, _ => new[] { .4, .5, .85, 1 } });
        ctx.MoveTo(output ? 8 : 0, 0); ctx.LineTo(output ? e.W : e.W - 8, 0);
        ctx.LineTo(output ? e.W : e.W, e.H / 2); ctx.LineTo(output ? e.W : e.W - 8, e.H);
        ctx.LineTo(output ? 8 : 0, e.H); ctx.LineTo(output ? 0 : 0, output ? e.H / 2 : e.H); ctx.ClosePath(); ctx.Fill();
        Color(ctx, Ink);
        Text(ctx, font, (e.Param + 1).ToString(), e.W / 2, e.H / 2);
        if (e.Label.Length > 0)
        {
            Color(ctx, Ink);
            font.SetupContext(ctx);
            double tw = ctx.TextExtents(e.Label).Width;
            ctx.MoveTo(output ? e.W + 6 : -tw - 6, e.H / 2 + 4);
            ctx.ShowText(e.Label);
            ctx.NewPath();
        }
    }

    private void DrawBox(Context ctx, SchematicLayout.Element e, CairoFont font)
    {
        Color(ctx, e.Soldered ? SolderedBox : Box);
        ctx.Rectangle(0, 0, e.W, e.H); ctx.FillPreserve();
        Color(ctx, hovered == e ? Glow : Ink);
        ctx.LineWidth = e.Soldered ? 3 : 2;
        ctx.Stroke();
        Color(ctx, Ink);
        Text(ctx, font, Shorten(e.Name, 14), e.W / 2, 11);
        if (!e.Openable)
        {
            Color(ctx, Red);
            Text(ctx, font, "■", e.W - 8, e.H - 8);
        }
        foreach (var p in e.Ports)
        {
            Color(ctx, Ink);
            double px = p.Side == SchematicLayout.Side.Left ? 0 : e.W, py = p.Y - e.Y;
            ctx.Arc(px, py, 2.5, 0, Math.PI * 2); ctx.Fill();
        }
    }

    private static string Shorten(string s, int max) => string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..(max - 1)] + "…";

    private void DrawPart(Context ctx, SchematicLayout.Element e, CairoFont font)
    {
        double w = e.W, h = e.H;
        switch (e.Kind)
        {
            case "source":
                ctx.Arc(w / 2, h / 2, w / 2 - 2, 0, Math.PI * 2); ctx.Stroke();
                ctx.MoveTo(w / 2 - 6, h / 2 - 8); ctx.LineTo(w / 2 - 6, h / 2 + 8);     // long plate
                ctx.MoveTo(w / 2 + 2, h / 2 - 4); ctx.LineTo(w / 2 + 2, h / 2 + 4);     // short plate
                ctx.Stroke();
                Text(ctx, font, e.Param.ToString(), w / 2, h + 8);
                break;
            case "resistor":
                ctx.Rectangle(4, 4, w - 8, h - 8); ctx.Stroke();
                Text(ctx, font, e.Param == e.Param2 ? e.Param.ToString() : $"{e.Param}/{e.Param2}", w / 2, h / 2);
                break;
            case "switch":
            case "toggle":
            {
                double y = e.Ports.First(p => p.Side == SchematicLayout.Side.Left && !p.Dashed && p.Name == null).Y - e.Y;   // the blade sits on the terminal lead
                ctx.Arc(6, y, 2.5, 0, Math.PI * 2); ctx.Fill();
                ctx.Arc(w - 6, y, 2.5, 0, Math.PI * 2); ctx.Fill();
                ctx.MoveTo(0, y); ctx.LineTo(6, y); ctx.MoveTo(w - 6, y); ctx.LineTo(w, y); ctx.Stroke();
                ctx.MoveTo(6, y);
                if (e.Param != 0) ctx.LineTo(w - 6, y); else ctx.LineTo(w - 10, y - 12);   // blade: closed or open
                ctx.Stroke();
                if (e.Kind == "toggle")
                {
                    // relay style: the actuator coil on top, fed by the trigger; it flips the blade below
                    double cy = e.Ports.First(p => p.Name == "T").Y - e.Y;
                    ctx.MoveTo(0, cy); ctx.LineTo(w / 2 - 12, cy); ctx.Stroke();
                    ctx.Rectangle(w / 2 - 12, cy - 9, 24, 18); ctx.Stroke();
                    Text(ctx, font, "T", w / 2, cy);
                    ctx.SetDash(new double[] { 2, 2 }, 0);
                    ctx.MoveTo(w / 2, cy + 9); ctx.LineTo(w / 2, y - 8); ctx.Stroke();
                    ctx.SetDash(Array.Empty<double>(), 0);
                }
                break;
            }
            case "valve":
            case "tetrode":
            {
                double cx = w / 2, cy = h / 2 - 4, r = Math.Min(w, h) / 2 - 4;
                ctx.Arc(cx, cy, r, 0, Math.PI * 2); ctx.Stroke();
                // anode plate on top, cathode at the bottom, grids dashed in between
                ctx.MoveTo(cx - r * .55, cy - r * .5); ctx.LineTo(cx + r * .55, cy - r * .5); ctx.Stroke();
                ctx.MoveTo(cx + r * .55, cy - r * .5); ctx.LineTo(cx + r, cy - r * .5); ctx.LineTo(w, e.Ports.First(p => p.Side == SchematicLayout.Side.Right).Y - e.Y); ctx.Stroke();
                ctx.MoveTo(cx - r * .5, cy + r * .5); ctx.LineTo(cx + r * .5, cy + r * .5); ctx.Stroke();   // cathode
                double kx = e.Ports.First(p => p.Side == SchematicLayout.Side.Bottom).X - e.X;   // straight down onto the wire's grid point
                ctx.MoveTo(kx, cy + r * .5); ctx.LineTo(kx, h); ctx.Stroke();
                ctx.SetDash(new double[] { 3, 3 }, 0);
                int grids = e.Kind == "tetrode" ? 2 : 1;
                for (int g = 0; g < grids; g++)
                {
                    double gy = cy + (grids == 1 ? 0 : g == 0 ? -r * .15 : r * .2);
                    ctx.MoveTo(cx - r * .7, gy); ctx.LineTo(cx + r * .7, gy); ctx.Stroke();
                    var port = e.Ports.Where(p => p.Side == SchematicLayout.Side.Left).ElementAt(g);
                    ctx.MoveTo(0, port.Y - e.Y); ctx.LineTo(cx - r, port.Y - e.Y); ctx.LineTo(cx - r * .7, gy); ctx.Stroke();
                }
                ctx.SetDash(Array.Empty<double>(), 0);
                break;
            }
            case "delay":
                ctx.Rectangle(2, 2, w - 4, h - 4); ctx.Stroke();
                Text(ctx, font, e.Param.ToString(), w / 2, h / 2);   // the setting as the block shows it
                break;
            case "buffer":
                ctx.MoveTo(4, 2); ctx.LineTo(w - 4, h / 2); ctx.LineTo(4, h - 2); ctx.ClosePath(); ctx.Stroke();
                break;
            default:
                ctx.Rectangle(2, 2, w - 4, h - 4); ctx.Stroke();
                Text(ctx, font, e.Kind, w / 2, h / 2);
                break;
        }
        // a setting fed from a node: dashed lead into the part
        foreach (var p in e.Ports.Where(p => p.Dashed))
        {
            Color(ctx, Dim);
            ctx.SetDash(new double[] { 2, 3 }, 0);
            ctx.MoveTo(p.X - e.X, p.Y - e.Y); ctx.LineTo(w / 2, h / 2); ctx.Stroke();
            ctx.SetDash(Array.Empty<double>(), 0);
        }
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        if (texture.TextureId != 0)
            api.Render.Render2DTexturePremultipliedAlpha(texture.TextureId, (int)Bounds.renderX, (int)Bounds.renderY, (int)Bounds.InnerWidth, (int)Bounds.InnerHeight);
        if (hovered != null && hoverTexture.TextureId != 0)
        {
            double x = Bounds.renderX + offsetX + (hovered.X + hovered.W / 2) * scale - hoverTexture.Width / 2.0;
            double y = Bounds.renderY + offsetY + (hovered.Y + hovered.H + 6) * scale;
            api.Render.Render2DTexturePremultipliedAlpha(hoverTexture.TextureId, (int)x, (int)y, hoverTexture.Width, hoverTexture.Height);
        }
    }

    private SchematicLayout.Element BoxAt(int mouseX, int mouseY) => ElementAt(mouseX, mouseY, boxesOnly: true);

    private SchematicLayout.Element ElementAt(int mouseX, int mouseY, bool boxesOnly)
    {
        if (layout == null) return null;
        double x = (mouseX - Bounds.absX - offsetX) / scale, y = (mouseY - Bounds.absY - offsetY) / scale;
        return layout.Elements.FirstOrDefault(e => (e.IsBox || !boxesOnly && !e.IsPin) && x >= e.X && x <= e.X + e.W && y >= e.Y && y <= e.Y + e.H);
    }

    private static string Describe(SchematicLayout.Element e)
    {
        if (e.IsBox)
            return e.Name + (string.IsNullOrEmpty(e.Description) ? "" : "\n" + e.Description)
                + "\n" + Lang.Get(e.Openable ? "signalstubes:schematic-open-hint" : "signalstubes:schematic-locked-hint");
        string key = "signalstubes:schematic-part-" + e.Kind;
        return e.Kind switch
        {
            "resistor" => Lang.Get(key, e.Param == e.Param2 ? e.Param.ToString() : $"{e.Param}/{e.Param2}"),
            "source" or "delay" => Lang.Get(key, e.Param),
            "switch" or "toggle" => Lang.Get(key) + " " + Lang.Get(e.Param != 0 ? "signalstubes:schematic-on" : "signalstubes:schematic-off"),
            _ => Lang.Get(key)
        };
    }

    public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
    {
        if (dragging)
        {
            panX += args.X - dragX; panY += args.Y - dragY;
            dragX = args.X; dragY = args.Y;
            Redraw();
            return;
        }
        var box = IsPositionInside(args.X, args.Y) ? ElementAt(args.X, args.Y, boxesOnly: false) : null;
        if (box == hovered) return;
        hovered = box;
        Redraw();
        if (box == null) return;
        hoverTexture.Dispose();
        hoverTexture = api.Gui.TextTexture.GenTextTexture(Describe(box), CairoFont.WhiteSmallText(), 260, new TextBackground { FillColor = GuiStyle.DialogStrongBgColor, Padding = 6, Radius = 4 });
    }

    // Double click on a nested tube opens it, right click anywhere goes back up; left drag pans.
    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        base.OnMouseDownOnElement(api, args);
        if (args.Button == EnumMouseButton.Right) { onBack(); return; }
        if (args.Button != EnumMouseButton.Left) return;
        var box = BoxAt(args.X, args.Y);
        long now = api.World.ElapsedMilliseconds;
        if (box != null && box == lastClicked && now - lastClick < 400)
        {
            lastClicked = null;
            if (box.Openable) onOpen(box.Step);
            return;
        }
        lastClicked = box; lastClick = now;
        dragging = true; dragX = args.X; dragY = args.Y;
    }

    public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
    {
        base.OnMouseUp(api, args);
        dragging = false;
    }

    public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs args)
    {
        if (!IsPositionInside(api.Input.MouseX, api.Input.MouseY) || layout == null) return;
        args.SetHandled();
        // Zoom around the cursor: the diagram point under it stays under it.
        double cx = api.Input.MouseX - Bounds.absX, cy = api.Input.MouseY - Bounds.absY;
        double px = (cx - offsetX) / scale, py = (cy - offsetY) / scale;
        zoom = Math.Clamp(zoom * (args.delta > 0 ? 1.15 : 1 / 1.15), .3, 6);
        double newScale = fit * zoom;
        panX = cx - (Bounds.InnerWidth - layout.Width * newScale) / 2 - px * newScale;
        panY = cy - (Bounds.InnerHeight - layout.Height * newScale) / 2 - py * newScale;
        Redraw();
    }

    public override void Dispose()
    {
        base.Dispose();
        texture.Dispose();
        hoverTexture.Dispose();
    }
}
