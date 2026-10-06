using signals.src.signalNetwork;
using SignalsTubes.src.programtube;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SignalsTubes.src.copier;

/// <summary>
/// A 1x2 multiblock: the cabinet below, the tubes sticking up into the block above (a vanilla proxy that asks here).
/// Selection boxes: 0 start pin, 1 state pin (Signals anchors), 2 green socket, 3 red socket, 4 the cabinet.
/// </summary>
public class BlockTubeCopier : BlockConnection, IMultiBlockColSelBoxes, IMultiBlockInteract
{
    private const int PinCount = 2;
    private Cuboidf[] boxes, upperIn, upperOut, upperBoth;
    public int RotationDegrees => Variant["side"] switch { "east" => 270, "south" => 180, "west" => 90, _ => 0 };

    private Cuboidf Rot(float x1, float y1, float z1, float x2, float y2, float z2) =>
        new Cuboidf(x1 / 16, y1 / 16, z1 / 16, x2 / 16, y2 / 16, z2 / 16).RotatedCopy(0, RotationDegrees, 0, new Vec3d(.5, .5, .5));

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        boxes = new[] {
            Rot(1.5f, 11.5f, 1.5f, 7.5f, 13, 7.5f),     // green socket, rear left of the plate
            Rot(8.5f, 11.5f, 8.5f, 14.5f, 13, 14.5f),   // red socket, front right
            Rot(1, 0, 1, 15, 11.5f, 15)       // the cabinet
        };
        var tubeIn = Rot(1.5f, 0, 1.5f, 7.5f, 3.5f, 7.5f); var tubeOut = Rot(8.5f, 0, 8.5f, 14.5f, 3.5f, 14.5f);
        upperIn = new[] { tubeIn }; upperOut = new[] { tubeOut }; upperBoth = new[] { tubeIn, tubeOut };
    }

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor accessor, BlockPos pos) =>
        base.GetSelectionBoxes(accessor, pos).Take(PinCount).Concat(boxes).ToArray();

    public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
    {
        bool placed = base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack);
        if (placed && world.BlockAccessor.GetBlockEntity(blockSel.Position) is BETubeCopier be) be.SetPlacer(byPlayer);
        return placed;
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer player, BlockSelection sel)
    {
        if (sel.SelectionBoxIndex < PinCount) return base.OnBlockInteractStart(world, player, sel);
        if (!world.Claims.TryAccess(player, sel.Position, EnumBlockAccessFlags.Use)) return false;
        if (world.BlockAccessor.GetBlockEntity(sel.Position) is not BETubeCopier be) return false;
        var hand = player.InventoryManager.ActiveHotbarSlot;
        switch (sel.SelectionBoxIndex - PinCount)
        {
            case 0: return be.Interact(BETubeCopier.In, player);
            case 1: return be.Interact(BETubeCopier.Out, player);
            default:
                if (be.AddCharge(hand, player)) return true;
                if (!hand.Empty) return false;
                if (world.Side == EnumAppSide.Server) be.Start(player.PlayerUID);
                return true;
        }
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection sel, IPlayer player)
    {
        if (sel.SelectionBoxIndex < PinCount) return base.GetPlacedBlockInteractionHelp(world, sel, player);
        var be = world.BlockAccessor.GetBlockEntity(sel.Position) as BETubeCopier;
        var gear = world.GetItem(new AssetLocation("game:gear-temporal"));
        return (sel.SelectionBoxIndex - PinCount) switch
        {
            0 => new[] { new WorldInteraction { ActionLangCode = be?.Original != null ? "signalstubes:copier-remove" : "signalstubes:copier-insert-in", MouseButton = EnumMouseButton.Right } },
            1 => new[] { new WorldInteraction { ActionLangCode = be?.Target != null ? "signalstubes:copier-remove" : "signalstubes:copier-insert-out", MouseButton = EnumMouseButton.Right } },
            _ => new[] {
                new WorldInteraction { ActionLangCode = "signalstubes:copier-start", MouseButton = EnumMouseButton.Right },
                new WorldInteraction { ActionLangCode = "signalstubes:copier-charge", MouseButton = EnumMouseButton.Right, Itemstacks = gear == null ? null : new[] { new ItemStack(gear) } } }
        };
    }

    // Looking at a socket names it and says what it is for instead of the block's own tooltip.
    private int PartSocket(BlockPos pos)
    {
        var sel = (api as ICoreClientAPI)?.World.Player?.CurrentBlockSelection;
        if (sel == null) return -1;
        if (!sel.Position.Equals(pos))
            return api.World.BlockAccessor.GetBlockEntity(pos) is BETubeCopier be ? UpperSocket(be, sel.SelectionBoxIndex) : -1;
        return sel.SelectionBoxIndex switch { 0 => PinStart, 1 => PinState, 2 => BETubeCopier.In, 3 => BETubeCopier.Out, _ => -1 };
    }

    private const int PinStart = 10, PinState = 11;   // part codes for the two Signals pins

    private static string PartKey(int part) => part switch { BETubeCopier.In => "copier-in", BETubeCopier.Out => "copier-out", PinStart => "copier-start", _ => "copier-state" };

    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos) =>
        PartSocket(pos) is int s and >= 0 ? Lang.Get("signalstubes:part-" + PartKey(s)) : base.GetPlacedBlockName(world, pos);

    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
    {
        int s = PartSocket(pos);
        if (s < 0) return base.GetPlacedBlockInfo(world, pos, forPlayer);
        string info = Lang.Get("signalstubes:part-" + PartKey(s) + "-desc");
        var be = world.BlockAccessor.GetBlockEntity(pos) as BETubeCopier;
        var stack = s == BETubeCopier.In ? be?.Original : be?.Target;
        if (stack != null) info += "\n" + ItemProgramTube.FullInfo(stack, world);
        return info;
    }

    // ---- the block above: only the tubes live there. The proxy hands over the offset *to* the controller.

    private BETubeCopier Controller(IBlockAccessor accessor, BlockPos pos, Vec3i offset) => accessor.GetBlockEntity(pos.AddCopy(offset)) as BETubeCopier;

    public Cuboidf[] MBGetCollisionBoxes(IBlockAccessor accessor, BlockPos pos, Vec3i offset) => Array.Empty<Cuboidf>();
    public Cuboidf[] MBGetSelectionBoxes(IBlockAccessor accessor, BlockPos pos, Vec3i offset)
    {
        var be = Controller(accessor, pos, offset);
        bool a = be?.Original != null, b = be?.Target != null;
        return a && b ? upperBoth : a ? upperIn : b ? upperOut : Array.Empty<Cuboidf>();
    }
    public bool MBDoPartialSelection(IWorldAccessor world, BlockPos pos, Vec3i offset) => true;

    // which socket a box in the block above belongs to: with one tube only, the single box is that tube's
    private static int UpperSocket(BETubeCopier be, int boxIndex) => be.Original != null && boxIndex == 0 ? BETubeCopier.In : BETubeCopier.Out;

    public bool MBOnBlockInteractStart(IWorldAccessor world, IPlayer player, BlockSelection sel, Vec3i offset)
    {
        var at = sel.Position.AddCopy(offset);
        if (!world.Claims.TryAccess(player, at, EnumBlockAccessFlags.Use)) return false;
        return world.BlockAccessor.GetBlockEntity(at) is BETubeCopier be && be.Interact(UpperSocket(be, sel.SelectionBoxIndex), player);
    }
    public bool MBOnBlockInteractStep(float seconds, IWorldAccessor world, IPlayer player, BlockSelection sel, Vec3i offset) => false;
    public void MBOnBlockInteractStop(float seconds, IWorldAccessor world, IPlayer player, BlockSelection sel, Vec3i offset) { }
    public bool MBOnBlockInteractCancel(float seconds, IWorldAccessor world, IPlayer player, BlockSelection sel, EnumItemUseCancelReason reason, Vec3i offset) => true;
    public ItemStack MBOnPickBlock(IWorldAccessor world, BlockPos pos, Vec3i offset) => OnPickBlock(world, pos.AddCopy(offset));
    public WorldInteraction[] MBGetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection sel, IPlayer player, Vec3i offset) =>
        new[] { new WorldInteraction { ActionLangCode = "signalstubes:copier-remove", MouseButton = EnumMouseButton.Right } };
    public BlockSounds MBGetSounds(IBlockAccessor accessor, BlockSelection sel, ItemStack stack, Vec3i offset) => Sounds;
}
