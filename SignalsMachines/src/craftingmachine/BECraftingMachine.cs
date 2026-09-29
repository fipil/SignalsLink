using SignalsTubes.src.programtube;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SignalsMachines.src.craftingmachine;

public class BECraftingMachine : BlockEntity
{
    private ItemStack tube;
    private MeshData tubeMesh;
    public bool HasTube => tube != null;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        tube?.ResolveBlockOrItem(api.World);
        RebuildMesh();
    }

    public bool Interact(IPlayer player)
    {
        var hand = player.InventoryManager.ActiveHotbarSlot;
        bool insert = tube == null && hand.Itemstack?.Collectible is ItemProgramTube;
        bool remove = tube != null && hand.Empty;
        if (!insert && !remove) return false;
        // Only the server transfers items. Creative also consumes this one stack, so removing
        // the tube cannot duplicate a player's programmed item.
        if (Api.Side == EnumAppSide.Client) return true;
        if (insert)
        {
            tube = hand.TakeOut(1);
            hand.MarkDirty();
        }
        else
        {
            ItemStack taken = tube;
            tube = null;
            if (!player.InventoryManager.TryGiveItemstack(taken, true))
                Api.World.SpawnItemEntity(taken, Pos.ToVec3d().Add(.5, .5, .5));
        }
        MarkDirty(true);
        Api.World.PlaySoundAt(new AssetLocation("signalsmachines:sounds/tube-click"), Pos.X + .5, Pos.Y + .15, Pos.Z + .5, null, false, 12, .65f);
        return true;
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        if (tube != null) tree.SetItemstack("programTube", tube);
        else tree.RemoveAttribute("programTube");
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        tube = tree.GetItemstack("programTube");
        tube?.ResolveBlockOrItem(worldForResolving);
        if (Api is ICoreClientAPI)
        {
            RebuildMesh();
            MarkDirty(true);
        }
    }

    private void RebuildMesh()
    {
        tubeMesh = null;
        if (Api is not ICoreClientAPI capi || tube?.Collectible is not ItemProgramTube item) return;
        MeshData mesh = item.BuildMesh(capi, tube, capi.Tesselator.GetTextureSource(Block));
        mesh.Scale(new Vec3f(), .5f, .5f, .5f);
        mesh.Translate(4f / 16, 1f / 16, 8f / 16);
        mesh.Rotate(new Vec3f(.5f, .5f, .5f), 0, ((BlockCraftingMachine)Block).RotationRadians, 0);
        tubeMesh = mesh;
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        var mesh = tubeMesh;
        if (mesh != null) mesher.AddMeshData(mesh.Clone());
        return false; // Keep the ordinary block mesh, adding only the installed tube.
    }

    public override void OnBlockBroken(IPlayer byPlayer = null)
    {
        if (Api.Side == EnumAppSide.Server && tube != null)
        {
            Api.World.SpawnItemEntity(tube, Pos.ToVec3d().Add(.5, .5, .5));
            tube = null;
        }
        base.OnBlockBroken(byPlayer);
    }
}
