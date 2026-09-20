using SignalsLink.src.signals.link;
using Vintagestory.API.Common;

namespace SignalsLink.src.signals
{
    /// <summary>
    /// The wrench's modifier clicks on SignalsLink blocks, attached to the vanilla wrench via a
    /// JSON patch. A plain click is left alone so the wrench still does its own thing.
    ///
    /// <list type="bullet">
    /// <item><b>Sneak + wrench</b> clears a pending Input buffer (ManagedChute, Valve, Damper).</item>
    /// <item><b>Ctrl + wrench</b> cycles the room sealing mode of a ceiling Damper or wall coupling.</item>
    /// </list>
    /// </summary>
    public class WrenchActionsBehavior : CollectibleBehavior
    {
        public WrenchActionsBehavior(CollectibleObject collObj) : base(collObj) { }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling, ref EnumHandling handling)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handHandling, ref handling);

            if (blockSel == null) return;

            BlockEntity be = byEntity.World.BlockAccessor.GetBlockEntity(blockSel.Position);
            if (be == null) return;

            bool server = byEntity.World.Side == EnumAppSide.Server;

            if (byEntity.Controls.CtrlKey)
            {
                if (be is not IRetentionModeHost host || !host.SupportsRetentionMode) return;

                if (server) host.CycleRetentionMode();
                handHandling = EnumHandHandling.PreventDefault;
                return;
            }

            if (byEntity.Controls.ShiftKey)
            {
                if (be is not ISignalBuffer buffer) return;

                if (server) buffer.ClearBuffer();
                handHandling = EnumHandHandling.PreventDefault;
            }
        }
    }
}
