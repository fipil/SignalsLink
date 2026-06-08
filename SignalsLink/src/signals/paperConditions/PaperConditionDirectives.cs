namespace SignalsLink.src.signals.paperConditions
{
    public sealed class PaperConditionDirectives
    {
        public static readonly PaperConditionDirectives Empty = new PaperConditionDirectives(null, null, false);

        public byte? TargetSlot { get; }
        public decimal? Amount { get; }
        public bool RequireTargetEmpty { get; }

        public bool HasTargetOverride => TargetSlot.HasValue;
        public bool HasAmountOverride => Amount.HasValue;

        public PaperConditionDirectives(byte? targetSlot, decimal? amount, bool requireTargetEmpty)
        {
            TargetSlot = targetSlot;
            Amount = amount;
            RequireTargetEmpty = requireTargetEmpty;
        }

        public bool Evaluate(IDictionary<string, object> ctx)
        {
            if (!RequireTargetEmpty) return true;
            if (!TargetSlot.HasValue || TargetSlot.Value <= 0) return false;
            if (ctx == null) return false;
            if (!ctx.TryGetValue("targetInventory", out var obj) || obj is not Vintagestory.API.Common.IInventory targetInventory) return false;

            int slotIndex = TargetSlot.Value - 1;
            if (slotIndex < 0 || slotIndex >= targetInventory.Count) return false;

            if (targetInventory is Vintagestory.GameContent.InventorySmelting smeltingInventory && slotIndex >= 3 && slotIndex <= 6 && !smeltingInventory.HaveCookingContainer)
            {
                return false;
            }

            var slot = targetInventory[slotIndex];
            return slot?.Empty == true;
        }
    }
}
