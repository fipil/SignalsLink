using Vintagestory.API.Common;
using Vintagestory.GameContent.Mechanics;

namespace SignalsMachines.src.craftingmachine;

/// <summary>
/// The machine's input axle as a mechanical power consumer. Two vanilla quirks are worked around: the
/// renderer takes the part shape before the consumer reads mechPartShape, so GetShape() supplies it; and
/// without a mechPartShape in the properties the consumer rewrites the path of Block.Shape itself, which
/// makes the block invisible, so the blocktype always carries one.
/// </summary>
public class BEBehaviorMPMachineAxle : BEBehaviorMPConsumer
{
    public BEBehaviorMPMachineAxle(BlockEntity blockentity) : base(blockentity) { }

    public override void Initialize(ICoreAPI api, Vintagestory.API.Datastructures.JsonObject properties)
    {
        base.Initialize(api, properties);
        Shape = GetShape();   // the consumer replaced it with the unrotated one from the properties
    }

    // The consumer answers "true" = skip the block mesh; the machine body must stay.
    public override bool OnTesselation(Vintagestory.API.Client.ITerrainMeshPool mesher, Vintagestory.API.Client.ITesselatorAPI tesselator)
    {
        base.OnTesselation(mesher, tesselator);
        return false;
    }

    protected override CompositeShape GetShape() => new()
    {
        Base = new AssetLocation("signalsmachines", "shapes/block/craftingmachine-axle.json"),
        rotateY = (Block as BlockCraftingMachine)?.RotationDegrees ?? 0
    };
}
