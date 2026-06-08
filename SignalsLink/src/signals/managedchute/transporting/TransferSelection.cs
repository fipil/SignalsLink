using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;

namespace SignalsLink.src.signals.managedchute.transporting
{
    public sealed class TransferSelection
    {
        public ItemSlot SourceSlot { get; }
        public PaperConditionDirectives Directives { get; }

        public TransferSelection(ItemSlot sourceSlot, PaperConditionDirectives directives)
        {
            SourceSlot = sourceSlot;
            Directives = directives ?? PaperConditionDirectives.Empty;
        }
    }
}
