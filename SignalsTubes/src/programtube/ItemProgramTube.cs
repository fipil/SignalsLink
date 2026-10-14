using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SignalsTubes.src.programtube;

public class ItemProgramTube : Item
{
    private Shape template;
    public string Color => Variant["color"] ?? "fire";

    /// <summary>Client only. The tube shape for the given stack; blocks embed it into their own mesh.
    /// The base texture is renamed per colour so one block texture set can host every tube.</summary>
    public Shape BuildShape(ItemStack stack, bool lit)
    {
        var shape = TubeVisuals.Build(template, stack, lit);
        TubeVisuals.Retexture(shape, "tube-base", "tube-base-" + Color);
        return shape;
    }
    private readonly Dictionary<string, MultiTextureMeshRef> meshes = new();

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        if (api is ICoreClientAPI)
            template = api.Assets.Get(new AssetLocation("signalstubes", "shapes/item/programtube.json")).ToObject<Shape>();
    }

    // The installed mesh uses the block atlas; handheld and inventory meshes use the item atlas.
    // `lit`: the glyph glows, as in a socket whose pins belong to a powered network.
    public MeshData BuildMesh(ICoreClientAPI capi, ItemStack stack, ITexPositionSource textures, bool lit = false)
    {
        capi.Tesselator.TesselateShape("signalstubes program tube", BuildShape(stack, lit), out MeshData mesh, textures);
        return mesh;
    }

    public override void OnBeforeRender(ICoreClientAPI capi, ItemStack stack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
    {
        base.OnBeforeRender(capi, stack, target, ref renderinfo);
        string key = TubeVisuals.MeshKey(stack, false);
        if (!meshes.TryGetValue(key, out var mesh))
        {
            if (meshes.Count >= 128) DisposeMeshes();
            mesh = capi.Render.UploadMultiTextureMesh(BuildMesh(capi, stack, capi.Tesselator.GetTextureSource(this)));
            meshes[key] = mesh;
        }
        renderinfo.ModelRef = mesh;
    }

    // A named tube is known by its name; the kind moves down into the small print.
    public override string GetHeldItemName(ItemStack stack)
    {
        string name = stack.Attributes.GetString(TubeProgram.NameKey, "");
        return name.Length == 0 ? base.GetHeldItemName(stack) : TubeProgram.Vtml(name);
    }

    /// <summary>The held name in the given language, for texts the server builds for one player.</summary>
    public static string NameIn(ItemStack stack, string langCode)
    {
        string name = stack.Attributes.GetString(TubeProgram.NameKey, "");
        if (name.Length > 0) return TubeProgram.Vtml(name);
        var code = stack.Collectible.Code;
        return Lang.GetMatchingL(langCode, code.Domain + ":" + stack.Class.Name() + "-" + code.Path);
    }

    public override void GetHeldItemInfo(ItemSlot slot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        var stack = slot.Itemstack;
        if (stack.Attributes.GetString(TubeProgram.NameKey, "").Length > 0) dsc.AppendLine(base.GetHeldItemName(stack));
        base.GetHeldItemInfo(slot, dsc, world, withDebugInfo);
        AppendDetails(stack, dsc);
    }

    /// <summary>Description, pins, author, locks: the part shared by the tooltip and machine info.</summary>
    public static void AppendDetails(ItemStack stack, StringBuilder dsc)
    {
        dsc.Append(ErrorInfo(stack));
        string description = Description(stack);
        if (description != null) dsc.AppendLine(TubeProgram.Vtml(description));
        if (!TubeProgram.IsBlank(stack))
        {
            var pins = TubeProgram.Pins(stack);
            dsc.AppendLine(Lang.Get("signalstubes:programtube-pins", pins.Count));
            foreach (var pin in pins)
                dsc.AppendLine("  " + (TubeProgram.Vtml(TubeProgram.Resolve(pin.Name)) ?? Lang.Get("signalstubes:pin-" + Role(pin.Role), pin.Index + 1)) + (pin.Name == null ? "" : $" ({Lang.Get("signalstubes:role-" + Role(pin.Role))})"));
            string author = TubeProgram.AuthorName(stack);
            if (author != null) dsc.AppendLine(Lang.Get("signalstubes:programtube-author", TubeProgram.Vtml(author)));
            if (TubeProgram.LockCopy(stack)) dsc.AppendLine(Lang.Get("signalstubes:programtube-lockcopy"));
            if (TubeProgram.LockView(stack)) dsc.AppendLine(Lang.Get("signalstubes:programtube-lockview"));
            int soldered = TubeProgram.Soldered(stack, null).Count;
            if (soldered > 0) dsc.AppendLine(Lang.Get("signalstubes:programtube-soldered", soldered));
        }
    }

    private static string Role(circuit.PinRole role) => role.ToString().ToLowerInvariant();

    public static string ErrorInfo(ItemStack stack) => TubeProgram.HasError(stack)
        ? "<font color=\"#ff4040\">" + Lang.Get("signalstubes:tube-error-" + TubeProgram.Error(stack).ToString().ToLowerInvariant(),
            TubeProgram.Error(stack) == TubeProgramError.Depth ? circuit.CircuitLimits.MaxDepth : circuit.CircuitLimits.MaxExpandedSize) + "</font>\n" : "";

    /// <summary>Block info of a machine holding the tube: a blank line, "Tube: name", then the details.</summary>
    public static string FullInfo(ItemStack stack, IWorldAccessor world)
    {
        if (stack?.Collectible is not ItemProgramTube item) return "";
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine(Lang.Get("signalstubes:tube-held", item.GetHeldItemName(stack)));
        AppendDetails(stack, sb);
        return sb.ToString();
    }

    /// <summary>Free text the imprinter lets the author add; null when there is none.</summary>
    public static string Description(ItemStack stack)
    {
        string text = stack.Attributes.GetString(TubeProgram.DescriptionKey, "");
        return text.Length == 0 ? null : text;
    }

    public override void OnUnloaded(ICoreAPI api)
    {
        DisposeMeshes();
        base.OnUnloaded(api);
    }

    private void DisposeMeshes()
    {
        foreach (var mesh in meshes.Values) mesh.Dispose();
        meshes.Clear();
    }
}
