using SignalsTubes.src.programtube;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SignalsTubes.src.imprint;

/// <summary>
/// A 1x2 multiblock: the cabinet below, the tube sticking up into the block above (a vanilla proxy that asks here).
/// Selection boxes: 0 = plug cradle, 1 = tube socket on the top plate, 2 = probe stand, 3+ = the rest of the cabinet.
/// </summary>
public class BlockImprinter : Block, IMultiBlockColSelBoxes, IMultiBlockInteract
{
    private const int CradleBox = 0, SocketBox = 1, StandBox = 2;
    private Cuboidf[] boxes, upperTube;
    public int RotationDegrees => Variant["side"] switch { "east" => 270, "south" => 180, "west" => 90, _ => 0 };

    private Cuboidf Rot(float x1, float y1, float z1, float x2, float y2, float z2) =>
        new Cuboidf(x1 / 16, y1 / 16, z1 / 16, x2 / 16, y2 / 16, z2 / 16).RotatedCopy(0, RotationDegrees, 0, new Vec3d(.5, .5, .5));

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        // the cradles lie on the 45 degree slope between the front block (top 7.5) and the rear body (top 12.5)
        boxes = new[] {
            Rot(3.5f, 8.5f, 10, 8, 13, 15),          // plug cradle
            Rot(5, 14.5f, 5, 11, 16, 11),            // socket on the plate
            Rot(10, 8.5f, 10, 13, 12.5f, 15),        // probe stand
            Rot(1, 0, 1, 15, 12.5f, 10),             // rear body
            Rot(1.5f, 12.5f, 1.5f, 14.5f, 14.5f, 11.5f),   // top plate and neck
            Rot(1, 0, 10, 15, 8.5f, 15),             // front block
            Rot(8, 8.5f, 10, 10, 12.5f, 15),         // slope between the cradles
            Rot(1, 8.5f, 10, 3.5f, 12, 15),          // slope edges
            Rot(13, 8.5f, 10, 15, 12, 15)
        };
        upperTube = new[] { Rot(5, 0, 5, 11, 6.5f, 11) };
    }

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor accessor, BlockPos pos) => boxes;

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
            case CradleBox:
                if (!hand.Empty || be.PlugOut) return false;
                if (world.Side == EnumAppSide.Server)
                {
                    if (be.PlugHome) be.TakePlug((IServerPlayer)player);
                    else be.ReturnPlug();   // pulls the cable out of the socket
                }
                return true;
            case SocketBox:
                return be.InteractTube(player);
            case StandBox:
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
        return sel.SelectionBoxIndex switch
        {
            CradleBox => new[] { new WorldInteraction { ActionLangCode = be is { PlugHome: false } ? "signalstubes:imprinter-return-plug" : "signalstubes:imprinter-take-plug", MouseButton = EnumMouseButton.Right } },
            SocketBox => TubeHelp(world, be),
            StandBox => new[] { new WorldInteraction { ActionLangCode = be is { ProbeHome: false } ? "signalstubes:imprinter-return-probe" : "signalstubes:imprinter-take-probe", MouseButton = EnumMouseButton.Right } },
            _ => new[] { new WorldInteraction { ActionLangCode = "signalstubes:imprinter-open", MouseButton = EnumMouseButton.Right } }
        };
    }

    private static WorldInteraction[] TubeHelp(IWorldAccessor world, BEImprinter be)
    {
        var tube = world.GetItem(new AssetLocation("signalstubes:programtube-fire"));
        return new[] { new WorldInteraction {
            ActionLangCode = be is { HasTube: true } ? "signalstubes:imprinter-remove" : "signalstubes:imprinter-insert",
            MouseButton = EnumMouseButton.Right,
            Itemstacks = be is { HasTube: true } || tube == null ? null : new[] { new ItemStack(tube) } } };
    }

    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
    {
        string info = base.GetPlacedBlockInfo(world, pos, forPlayer);
        if (world.BlockAccessor.GetBlockEntity(pos) is BEImprinter be && be.HasTube) info += ItemProgramTube.FullInfo(be.Tube, world);
        return info;
    }

    // ---- the block above: only the tube lives there. The proxy hands over the offset *to* the controller.

    private BEImprinter Controller(IBlockAccessor accessor, BlockPos pos, Vec3i offset) => accessor.GetBlockEntity(pos.AddCopy(offset)) as BEImprinter;

    public Cuboidf[] MBGetCollisionBoxes(IBlockAccessor accessor, BlockPos pos, Vec3i offset) => Array.Empty<Cuboidf>();
    public Cuboidf[] MBGetSelectionBoxes(IBlockAccessor accessor, BlockPos pos, Vec3i offset) =>
        Controller(accessor, pos, offset) is { HasTube: true } ? upperTube : Array.Empty<Cuboidf>();
    public bool MBDoPartialSelection(IWorldAccessor world, BlockPos pos, Vec3i offset) => true;

    public bool MBOnBlockInteractStart(IWorldAccessor world, IPlayer player, BlockSelection sel, Vec3i offset)
    {
        var at = sel.Position.AddCopy(offset);
        if (!world.Claims.TryAccess(player, at, EnumBlockAccessFlags.Use)) return false;
        return world.BlockAccessor.GetBlockEntity(at) is BEImprinter be && be.InteractTube(player);
    }
    public bool MBOnBlockInteractStep(float seconds, IWorldAccessor world, IPlayer player, BlockSelection sel, Vec3i offset) => false;
    public void MBOnBlockInteractStop(float seconds, IWorldAccessor world, IPlayer player, BlockSelection sel, Vec3i offset) { }
    public bool MBOnBlockInteractCancel(float seconds, IWorldAccessor world, IPlayer player, BlockSelection sel, EnumItemUseCancelReason reason, Vec3i offset) => true;
    public ItemStack MBOnPickBlock(IWorldAccessor world, BlockPos pos, Vec3i offset) => OnPickBlock(world, pos.AddCopy(offset));
    public WorldInteraction[] MBGetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection sel, IPlayer player, Vec3i offset) =>
        TubeHelp(world, Controller(world.BlockAccessor, sel.Position, offset));
    public BlockSounds MBGetSounds(IBlockAccessor accessor, BlockSelection sel, ItemStack stack, Vec3i offset) => Sounds;
}
