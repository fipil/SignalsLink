using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsMachines.src.craftingmachine;

/// <summary>
/// Where the leak cloud may pass, by the game's rule for liquids: a block stops it only on a side that holds
/// liquid to the top (solid side or <c>liquidBarrierOnSides</c>), not by its collision box.
/// </summary>
public static class CloudSides
{
    /// <summary>The <paramref name="face"/> side of the block at <paramref name="pos"/> lets the cloud through.</summary>
    public static bool Open(IBlockAccessor accessor, BlockPos pos, BlockFacing face)
    {
        var block = accessor.GetBlock(pos, BlockLayersAccess.Solid);
        if (block == null || block.Id == 0) return true;
        if (IsMachine(accessor, block, pos)) return false;   // its sides are not solid, but the chamber is closed to the cloud
        return block.GetLiquidBarrierHeightOnSide(face, pos) < 1f;
    }

    // The machine block or the vanilla multiblock filler above it.
    private static bool IsMachine(IBlockAccessor accessor, Block block, BlockPos pos) =>
        block is BlockCraftingMachine
        || block is Vintagestory.GameContent.BlockMultiblock filler && accessor.GetBlock(pos.AddCopy(filler.OffsetInv)) is BlockCraftingMachine;

    /// <summary>The cloud may pass from <paramref name="pos"/> through its <paramref name="face"/> side into the neighbour.</summary>
    public static bool Flows(IBlockAccessor accessor, BlockPos pos, BlockFacing face) =>
        Open(accessor, pos, face) && Open(accessor, pos.AddCopy(face), face.Opposite);
}
