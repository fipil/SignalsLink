using SignalsTubes.src.programtube;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SignalsTubes.src.imprint;

/// <summary>Selection boxes: 0 = plug cradle, 1 = tube socket on top, 2 = probe stand, 3 = the rest of the cabinet.</summary>
public class BlockImprinter : Block
{
    private Cuboidf[] boxes;
    public int RotationDegrees => Variant["side"] switch { "east" => 270, "south" => 180, "west" => 90, _ => 0 };

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        boxes = new[] {
            new Cuboidf(5/16f, 2.5f/16, 14/16f, 11/16f, 7.5f/16, 16.6f/16),
            new Cuboidf(5/16f, 10.6f/16, 5/16f, 11/16f, 11.6f/16, 11/16f),
            new Cuboidf(14/16f, 3.5f/16, 6.5f/16, 15.5f/16, 11.8f/16, 9.5f/16),
            new Cuboidf(1/16f, 0, 1/16f, 14/16f, 10.6f/16, 15/16f)
        }.Select(b => b.RotatedCopy(0, RotationDegrees, 0, new Vec3d(.5, .5, .5))).ToArray();
    }

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor accessor, BlockPos pos)
    {
        var be = accessor.GetBlockEntity(pos) as BEImprinter;
        if (be is { HasTube: true })
        {
            var copy = boxes.Select(b => b.Clone()).ToArray();
            copy[1] = new Cuboidf(5/16f, 10.6f/16, 5/16f, 11/16f, 18/16f, 11/16f).RotatedCopy(0, RotationDegrees, 0, new Vec3d(.5, .5, .5));
            return copy;
        }
        return boxes;
    }

    public override bool DoPartialSelection(IWorldAccessor world, BlockPos pos) => true;

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer player, BlockSelection sel)
    {
        if (!world.Claims.TryAccess(player, sel.Position, EnumBlockAccessFlags.Use)) return false;
        if (world.BlockAccessor.GetBlockEntity(sel.Position) is not BEImprinter be) return false;
        var hand = player.InventoryManager.ActiveHotbarSlot;
        if (ItemImprinterTool.BelongsTo(hand.Itemstack, BEImprinter.PlugCode, sel.Position))
        {
            if (world.Side == EnumAppSide.Server) be.ReturnPlug();
            return true;
        }
        if (ItemImprinterTool.BelongsTo(hand.Itemstack, BEImprinter.ProbeCode, sel.Position))
        {
            if (world.Side == EnumAppSide.Server) be.ReturnProbe();
            return true;
        }
        switch (sel.SelectionBoxIndex)
        {
            case 0:
                if (!hand.Empty || be.PlugOut) return false;
                if (world.Side == EnumAppSide.Server)
                {
                    if (be.PlugHome) be.TakePlug((IServerPlayer)player);
                    else be.ReturnPlug();   // pulls the cable out of the socket
                }
                return true;
            case 1:
                return be.InteractTube(player);
            case 2:
                if (!hand.Empty || !be.ProbeHome) return false;
                if (world.Side == EnumAppSide.Server) be.TakeProbe((IServerPlayer)player);
                return true;
            default:
                if (!hand.Empty) return false;
                if (world.Side == EnumAppSide.Client) ImprinterNetwork.OpenDialog((ICoreClientAPI)api, sel.Position);
                return true;
        }
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection sel, IPlayer player)
    {
        var be = world.BlockAccessor.GetBlockEntity(sel.Position) as BEImprinter;
        var tube = world.GetItem(new AssetLocation("signalstubes:programtube-fire"));
        return sel.SelectionBoxIndex switch
        {
            0 => new[] { new WorldInteraction { ActionLangCode = be is { PlugHome: false } ? "signalstubes:imprinter-return-plug" : "signalstubes:imprinter-take-plug", MouseButton = EnumMouseButton.Right } },
            1 => new[] { new WorldInteraction {
                ActionLangCode = be is { HasTube: true } ? "signalstubes:imprinter-remove" : "signalstubes:imprinter-insert",
                MouseButton = EnumMouseButton.Right,
                Itemstacks = be is { HasTube: true } || tube == null ? null : new[] { new ItemStack(tube) } } },
            2 => new[] { new WorldInteraction { ActionLangCode = be is { ProbeHome: false } ? "signalstubes:imprinter-return-probe" : "signalstubes:imprinter-take-probe", MouseButton = EnumMouseButton.Right } },
            _ => new[] { new WorldInteraction { ActionLangCode = "signalstubes:imprinter-open", MouseButton = EnumMouseButton.Right } }
        };
    }

    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
    {
        string info = base.GetPlacedBlockInfo(world, pos, forPlayer);
        if (world.BlockAccessor.GetBlockEntity(pos) is BEImprinter be && be.HasTube) info += ItemProgramTube.FullInfo(be.Tube, world);
        return info;
    }
}
