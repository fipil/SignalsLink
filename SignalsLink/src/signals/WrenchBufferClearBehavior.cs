using Vintagestory.API.Common;

namespace SignalsLink.src.signals
{
    /// <summary>
    /// Sneak + wrench on a ManagedChute / hose Valve clears its pending Input buffer (and stops
    /// continuous mode). Attached to the vanilla wrench via a JSON patch. Mirrors the hose cutter
    /// behavior on shears.
    /// </summary>
    public class WrenchBufferClearBehavior : CollectibleBehavior
    {
        public WrenchBufferClearBehavior(CollectibleObject collObj) : base(collObj) { }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling, ref EnumHandling handling)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handHandling, ref handling);

            if (blockSel == null) return;
            if (!byEntity.Controls.ShiftKey) return; // sneak only, so a normal wrench click still rotates/passes

            ISignalBuffer be = byEntity.World.BlockAccessor.GetBlockEntity(blockSel.Position) as ISignalBuffer;
            if (be == null) return;

            if (byEntity.World.Side == EnumAppSide.Server)
            {
                be.ClearBuffer();
            }

            handHandling = EnumHandHandling.PreventDefault;
        }
    }
}
