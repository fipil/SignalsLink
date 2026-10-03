using signals.src.signalNetwork;
using SignalsTubes.src.programtube;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsTubes.src.socket;

/// <summary>
/// Eight-pin socket a tube runs in. Selection boxes 0-7 are the wire anchors (Signals convention:
/// anchor index = box index), the box after them is the socket body used to insert and remove a tube.
/// Pins the installed tube does not use are hidden and refuse wires.
/// </summary>
public class BlockTubeSocket : BlockConnection
{
    public const int PinCount = 8;

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor world, BlockPos pos)
    {
        var boxes = base.GetSelectionBoxes(world, pos);
        int mask = VisiblePins(world, pos);
        for (int i = 0; i < PinCount && i < boxes.Length; i++)
            if ((mask >> i & 1) == 0) boxes[i] = new Cuboidf();  // zero size: cannot be aimed at
        return boxes;
    }

    public static int VisiblePins(IBlockAccessor world, BlockPos pos) =>
        world.GetBlockEntity(pos) is BETubeSocket { HasTube: true } be ? be.PinMask : (1 << PinCount) - 1;

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (blockSel.SelectionBoxIndex < PinCount)
        {
            if ((VisiblePins(world.BlockAccessor, blockSel.Position) >> blockSel.SelectionBoxIndex & 1) == 0) return false;
            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }
        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use)) return false;
        var hand = byPlayer.InventoryManager.ActiveHotbarSlot;
        var be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BETubeSocket;
        if (imprint.ItemImprinterTool.IsPlug(hand.Itemstack))
        {
            if (be is { HasTube: true }) return false;
            var imprinterPos = imprint.ItemImprinterTool.ImprinterOf(hand.Itemstack);
            if (world.Side == EnumAppSide.Server && world.BlockAccessor.GetBlockEntity(imprinterPos) is imprint.BEImprinter imprinter)
                imprinter.LinkTo((Vintagestory.API.Server.IServerPlayer)byPlayer, blockSel.Position);
            return true;
        }
        if (be?.Imprinter != null)
        {
            // The plug sits here: an empty hand pulls it out again, nothing else fits.
            if (!hand.Empty) return false;
            if (world.Side == EnumAppSide.Server && world.BlockAccessor.GetBlockEntity(be.Imprinter) is imprint.BEImprinter imprinter)
                imprinter.UnplugToHand((Vintagestory.API.Server.IServerPlayer)byPlayer);
            return true;
        }
        return be != null && be.Interact(byPlayer);
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        if (selection.SelectionBoxIndex < PinCount) return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
        if (imprint.ItemImprinterTool.IsPlug(forPlayer.InventoryManager.ActiveHotbarSlot.Itemstack))
            return new[] { new WorldInteraction { ActionLangCode = "signalstubes:socket-plug", MouseButton = EnumMouseButton.Right } };
        if (world.BlockAccessor.GetBlockEntity(selection.Position) is BETubeSocket { Imprinter: not null })
            return new[] { new WorldInteraction { ActionLangCode = "signalstubes:socket-unplug", MouseButton = EnumMouseButton.Right } };
        bool hasTube = world.BlockAccessor.GetBlockEntity(selection.Position) is BETubeSocket { HasTube: true };
        var tube = world.GetItem(new AssetLocation("signalstubes:programtube-fire"));
        return new[] { new WorldInteraction {
            ActionLangCode = hasTube ? "signalstubes:socket-remove" : "signalstubes:socket-insert",
            MouseButton = EnumMouseButton.Right,
            Itemstacks = hasTube || tube == null ? null : new[] { new ItemStack(tube) }
        } };
    }

    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
    {
        string info = base.GetPlacedBlockInfo(world, pos, forPlayer);
        var be = world.BlockAccessor.GetBlockEntity(pos) as BETubeSocket;
        // The base line names the anchor in the signals domain; replace it with the pin's role.
        var sel = forPlayer.Entity.BlockSelection;
        string anchor = GetAnchorName(world, sel);
        if (anchor != null)
        {
            info = info.Replace(Vintagestory.API.Config.Lang.Get("signals:" + anchor) + "\r\n", "");
            // Only the pin's name; the cap colour tells the role.
            string role = Vintagestory.API.Config.Lang.Get("signalstubes:pin-" + (be?.RoleOf(sel.SelectionBoxIndex) ?? "free"), sel.SelectionBoxIndex + 1);
            info += (TubeProgram.Vtml(be?.NameOf(sel.SelectionBoxIndex)) ?? role) + "\n";
        }
        // A tube in a socket shows just its description; the full tooltip belongs to the item and to machines.
        if (be is { HasTube: true } && ItemProgramTube.Description(be.Tube) is string description) info += TubeProgram.Vtml(description) + "\n";
        return info;
    }

    // A loaded socket is named after its tube.
    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos) =>
        world.BlockAccessor.GetBlockEntity(pos) is BETubeSocket { HasTube: true } be
            ? (be.Tube.Attributes.GetString(TubeProgram.NameKey, "") is { Length: > 0 } name ? name : be.Tube.GetName())   // plain text here, not VTML
            : base.GetPlacedBlockName(world, pos);

    public Vec3f ShapeRotation => new(Shape.rotateX, Shape.rotateY, Shape.rotateZ);

    /// <summary>World point where the imprinter cable meets the plug head, whatever way the socket faces.</summary>
    public static Vec3d PlugCableEnd(IBlockAccessor accessor, BlockPos pos)
    {
        var block = accessor.GetBlock(pos) as BlockTubeSocket;
        var local = new Cuboidf(8 / 16f, 7 / 16f, 8 / 16f, 8 / 16f, 7 / 16f, 8 / 16f);
        if (block != null) local = local.RotatedCopy(block.Shape.rotateX, block.Shape.rotateY, block.Shape.rotateZ, new Vec3d(.5, .5, .5));
        return pos.ToVec3d().Add(local.MidX, local.MidY, local.MidZ);
    }
}
