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
    public MeshData BuildMesh(ICoreClientAPI capi, ItemStack stack, ITexPositionSource textures)
    {
        capi.Tesselator.TesselateShape("signalstubes program tube", BuildShape(stack, false), out MeshData mesh, textures);
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

    public override string GetHeldItemName(ItemStack stack)
    {
        string name = stack.Attributes.GetString("programName", "");
        return name.Length == 0 ? base.GetHeldItemName(stack) : Lang.Get("signalstubes:programtube-named", name);
    }

    public override void GetHeldItemInfo(ItemSlot slot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(slot, dsc, world, withDebugInfo);
        dsc.AppendLine(Lang.Get("signalstubes:programtube-pins", TubeVisuals.PinCount(slot.Itemstack)));
        string description = Description(slot.Itemstack);
        if (description != null) dsc.AppendLine(description);
    }

    /// <summary>Free text the imprinter lets the author add; null when there is none.</summary>
    public static string Description(ItemStack stack)
    {
        string text = stack.Attributes.GetString("programDescription", "");
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
