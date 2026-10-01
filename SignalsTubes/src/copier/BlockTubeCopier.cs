using signals.src.signalNetwork;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsTubes.src.copier;

/// <summary>
/// Selection boxes: 0 start pin, 1 state pin (Signals anchors), 2 green socket, 3 red socket, 4 the cabinet.
/// </summary>
public class BlockTubeCopier : BlockConnection
{
    private const int PinCount = 2;
    private Cuboidf[] boxes;
    public int RotationDegrees => Variant["side"] switch { "east" => 270, "south" => 180, "west" => 90, _ => 0 };

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        boxes = new[] {
            new Cuboidf(1/16f, 10.6f/16, 5/16f, 7/16f, 11.6f/16, 11/16f),
            new Cuboidf(9/16f, 10.6f/16, 5/16f, 15/16f, 11.6f/16, 11/16f),
            new Cuboidf(1/16f, 0, 1/16f, 15/16f, 10.6f/16, 15/16f)
        }.Select(b => b.RotatedCopy(0, RotationDegrees, 0, new Vec3d(.5, .5, .5))).ToArray();
    }

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor accessor, BlockPos pos)
    {
        var anchors = base.GetSelectionBoxes(accessor, pos).Take(PinCount);
        var be = accessor.GetBlockEntity(pos) as BETubeCopier;
        var sockets = boxes.Select(b => b.Clone()).ToArray();
        // a socket with a tube in it reaches up to the tube's top
        if (be?.Original != null) sockets[0] = new Cuboidf(1/16f, 10.6f/16, 5/16f, 7/16f, 18/16f, 11/16f).RotatedCopy(0, RotationDegrees, 0, new Vec3d(.5, .5, .5));
        if (be?.Target != null) sockets[1] = new Cuboidf(9/16f, 10.6f/16, 5/16f, 15/16f, 18/16f, 11/16f).RotatedCopy(0, RotationDegrees, 0, new Vec3d(.5, .5, .5));
        return anchors.Concat(sockets).ToArray();
    }

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
}
