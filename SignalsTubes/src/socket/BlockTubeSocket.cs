using signals.src.signalNetwork;
using SignalsTubes.src.programtube;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SignalsTubes.src.socket;

/// <summary>
/// Eight-pin socket a tube runs in. Selection boxes 0-7 are the wire anchors (Signals convention:
/// anchor index = box index), then the tube bed in the middle and the plate around it; both insert and
/// remove a tube, but a tube going into the plate turns the socket towards the aimed side first.
/// Pins the installed tube does not use are hidden and refuse wires.
/// </summary>
public class BlockTubeSocket : BlockConnection, Vintagestory.GameContent.IWrenchOrientable
{
    public const int PinCount = 8, BedBox = PinCount, PlateBox = PinCount + 1;

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor world, BlockPos pos)
    {
        var anchors = base.GetSelectionBoxes(world, pos).Take(PinCount).ToArray();
        // while the insertion preview shows, only its solid pins can be aimed at; where a hidden one stood is plate
        int mask = world.GetBlockEntity(pos) is BETubeSocket { PreviewPins: >= 0 } p ? p.PreviewPins : VisiblePins(world, pos);
        for (int i = 0; i < PinCount && i < anchors.Length; i++)
            if ((mask >> i & 1) == 0) anchors[i] = new Cuboidf();  // zero size: cannot be aimed at
        bool hasTube = world.GetBlockEntity(pos) is BETubeSocket { HasTube: true };
        var bed = new Cuboidf(5 / 16f, 1 / 16f, 5 / 16f, 11 / 16f, (hasTube ? 9.5f : 2.5f) / 16, 11 / 16f);
        var plate = new Cuboidf(1 / 16f, 0, 1 / 16f, 15 / 16f, 1 / 16f, 15 / 16f);
        return anchors.Append(Turned(bed)).Append(Turned(plate)).ToArray();
    }

    private Cuboidf Turned(Cuboidf local) => local.RotatedCopy(Shape.rotateX, Shape.rotateY, Shape.rotateZ, new Vec3d(.5, .5, .5));

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
        return be != null && be.Interact(byPlayer, blockSel);
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
        if (be is { PreviewOrientation: not null, PreviewTube: not null })
        {
            var variant = world.GetBlock(CodeWithVariant("orientation", be.PreviewOrientation.Code)) ?? this;
            foreach (var pin in TubeProgram.Pins(be.PreviewTube).OrderBy(p => p.Index))
            {
                string name = TubeProgram.Vtml(pin.Name) ?? Vintagestory.API.Config.Lang.Get("signalstubes:pin-" + BETubeSocket.RoleOf(be.PreviewTube, pin.Index), pin.Index + 1);
                var side = SocketOrientation.WorldSide(variant, SocketOrientation.LocalSide[pin.Index]);
                info += Vintagestory.API.Config.Lang.Get("signalstubes:preview-pin", name, Vintagestory.API.Config.Lang.Get("signalstubes:side-" + side.Code)) + "\n";
            }
        }
        // A tube in a socket shows just its description; the full tooltip belongs to the item and to machines.
        // Aiming at one of its pins shows nothing but that pin (its name is the title then).
        if (anchor != null && be is { HasTube: true }) return "";
        if (be is { HasTube: true } && ItemProgramTube.Description(be.Tube) is string description) info += TubeProgram.Vtml(description) + "\n";
        return info;
    }

    // A loaded socket is named after its tube; aimed at one of its pins, after that pin.
    public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos)
    {
        if (world.BlockAccessor.GetBlockEntity(pos) is not BETubeSocket { HasTube: true } be) return base.GetPlacedBlockName(world, pos);
        var sel = (world.Api as ICoreClientAPI)?.World.Player?.CurrentBlockSelection;
        if (sel != null && sel.Position.Equals(pos) && GetAnchorName(world, sel) != null)
            return be.NameOf(sel.SelectionBoxIndex) ?? Vintagestory.API.Config.Lang.Get("signalstubes:pin-" + be.RoleOf(sel.SelectionBoxIndex), sel.SelectionBoxIndex + 1);
        return be.Tube.Attributes.GetString(TubeProgram.NameKey, "") is { Length: > 0 } name ? name : be.Tube.GetName();   // plain text here, not VTML
    }

    public Vec3f ShapeRotation => new(Shape.rotateX, Shape.rotateY, Shape.rotateZ);

    /// <summary>The game's wrench: a quarter turn in the plate, tube and all; the wires jump to the pins' new places.</summary>
    public void Rotate(EntityAgent byEntity, BlockSelection blockSel, int dir)
    {
        if (api.World.BlockAccessor.GetBlockEntity(blockSel.Position) is not BETubeSocket be) return;
        var n = SocketOrientation.SideOf(this).Normali;
        var o = SocketOrientation.OrientationOf(this).Normali;
        // the next direction around the surface normal: n x o one way, o x n the other
        var next = dir >= 0
            ? BlockFacing.FromVector(n.Y * o.Z - n.Z * o.Y, n.Z * o.X - n.X * o.Z, n.X * o.Y - n.Y * o.X)
            : BlockFacing.FromVector(o.Y * n.Z - o.Z * n.Y, o.Z * n.X - o.X * n.Z, o.X * n.Y - o.Y * n.X);
        be.TurnTo(next);
    }

    /// <summary>World point where the imprinter cable meets the plug head, whatever way the socket faces.</summary>
    public static Vec3d PlugCableEnd(IBlockAccessor accessor, BlockPos pos)
    {
        var block = accessor.GetBlock(pos) as BlockTubeSocket;
        var local = new Cuboidf(8 / 16f, 4.25f / 16, 3 / 16f, 8 / 16f, 4.25f / 16, 3 / 16f);
        if (block != null) local = local.RotatedCopy(block.Shape.rotateX, block.Shape.rotateY, block.Shape.rotateZ, new Vec3d(.5, .5, .5));
        return pos.ToVec3d().Add(local.MidX, local.MidY, local.MidZ);
    }
}
