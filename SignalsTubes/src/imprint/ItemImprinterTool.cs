using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SignalsTubes.src.imprint;

/// <summary>
/// A tool tethered to an imprinter: the plug or the probe. It exists only while it is out; the
/// imprinter revokes it when the holder walks away or logs off, and a dropped one vanishes.
/// The probe's marking click is handled by <see cref="ProbeInput"/>, ahead of the block it points at.
/// </summary>
public class ItemImprinterTool : Item
{
    private const string PosKey = "imprinter";

    public static ItemStack Create(Item tool, BlockPos imprinter)
    {
        var stack = new ItemStack(tool);
        stack.Attributes.SetInt(PosKey + "X", imprinter.X);
        stack.Attributes.SetInt(PosKey + "Y", imprinter.Y);
        stack.Attributes.SetInt(PosKey + "Z", imprinter.Z);
        return stack;
    }

    public static BlockPos ImprinterOf(ItemStack stack) =>
        stack?.Collectible is ItemImprinterTool && stack.Attributes.HasAttribute(PosKey + "X")
            ? new BlockPos(stack.Attributes.GetInt(PosKey + "X"), stack.Attributes.GetInt(PosKey + "Y"), stack.Attributes.GetInt(PosKey + "Z"))
            : null;

    public static bool BelongsTo(ItemStack stack, string code, BlockPos imprinter) =>
        stack?.Collectible.Code.ToString() == code && ImprinterOf(stack)?.Equals(imprinter) == true;

    public static bool IsPlug(ItemStack stack) => stack?.Collectible.Code.ToString() == BEImprinter.PlugCode;
    public static bool IsProbe(ItemStack stack) => stack?.Collectible.Code.ToString() == BEImprinter.ProbeCode;

    /// <summary>Blocks the probe can mark for a pin.</summary>
    public static bool IsMarkable(Block block) =>
        block?.Code.Domain == "signals" && block.Code.Path.Split('-')[0] is "knifeswitch" or "blockdelay";

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot) =>
        IsProbe(inSlot.Itemstack)
            ? new[] { new WorldInteraction { ActionLangCode = "signalstubes:plug-mark", MouseButton = EnumMouseButton.Right } }
            : base.GetHeldInteractionHelp(inSlot);

    // A tool on the ground is a lost tool; the imprinter notices it is gone and puts a new one on the stand.
    public override void OnGroundIdle(EntityItem entityItem)
    {
        base.OnGroundIdle(entityItem);
        if (entityItem.World.Side == EnumAppSide.Server) entityItem.Die();
    }
}
