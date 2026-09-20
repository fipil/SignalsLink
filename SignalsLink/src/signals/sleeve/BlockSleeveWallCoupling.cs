using signals.src.signalNetwork;
using SignalsLink.src.signals.link;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.sleeve;

/// <summary>A two-anchor sleeve passage embedded in a full wall block.</summary>
public class BlockSleeveWallCoupling : BlockSleeveCoupling
{
    private RetentionModeRegistry retentionModes;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        retentionModes = api.ModLoader.GetModSystem<RetentionModeRegistry>();
    }

    public override NodePos GetNodePosForLink(IWorldAccessor world, BlockSelection selection, NodePos posInit = null) =>
        selection == null ? null : base.GetNodePosForLink(world, selection, posInit);

    public override bool CanAttachLink(IWorldAccessor world, NodePos pos, NodePos posInit = null) =>
        pos != null && (pos.index == 0 || pos.index == 1);

    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
    {
        var text = new System.Text.StringBuilder();
        var selection = forPlayer?.CurrentBlockSelection;
        if (selection?.Position?.Equals(pos) == true && GetNodePosForLink(world, selection) != null)
            text.AppendLine(Lang.Get("signalslink:con-sleeve"));
        // The anchor base returns early for anchor selection. Always include the BE's mode,
        // whether the player is looking at the aperture or the material around it.
        world.BlockAccessor.GetBlockEntity(pos)?.GetBlockInfo(forPlayer, text);
        return text.ToString();
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer player) =>
        new[] { new WorldInteraction
        {
            ActionLangCode = "signalslink:sleevewallcoupling-cycle",
            MouseButton = EnumMouseButton.Right,
            HotKeyCode = "ctrl",
            Itemstacks = world.Items.Where(item => item.Tool == EnumTool.Wrench)
                .Select(item => new ItemStack(item)).ToArray()
        } };

    public override int GetRetention(BlockPos pos, BlockFacing facing, EnumRetentionType type) =>
        RetentionMode.RetentionValue(retentionModes?.Get(pos) ?? RetentionMode.Cooling);

    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack stack, BlockSelection selection, ref string failureCode)
    {
        // Keep old axis-based stacks usable; newly placed blocks use the Signals behavior.
        if (Variant["axis"] != null)
        {
            var canonical = world.BlockAccessor.GetBlock(new AssetLocation(Code.Domain,
                $"sleevewallcoupling-{Variant["type"]}-{Variant["material"]}-north-down"));
            return canonical != null && canonical.TryPlaceBlock(world, byPlayer, stack, selection, ref failureCode);
        }
        return base.TryPlaceBlock(world, byPlayer, stack, selection, ref failureCode);
    }
}
