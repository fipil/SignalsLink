using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;

namespace SignalsLink.src.signals.managedchute.transporting
{
    public sealed class TransferSelection
    {
        public ItemSlot SourceSlot { get; }
        public PaperConditionDirectives Directives { get; }

        /// <summary>
        /// How much `keep N` still wants in the target, worked out when the block was chosen.
        /// Null when the block said no such thing; never zero or less, because a block already at
        /// its level is not chosen at all.
        /// </summary>
        public decimal? Room { get; }

        public TransferSelection(ItemSlot sourceSlot, PaperConditionDirectives directives, decimal? room = null)
        {
            SourceSlot = sourceSlot;
            Directives = directives ?? PaperConditionDirectives.Empty;
            Room = room;
        }
    }
}
