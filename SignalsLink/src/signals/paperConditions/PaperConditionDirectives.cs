namespace SignalsLink.src.signals.paperConditions
{
    public sealed class PaperConditionDirectives
    {
        public static readonly PaperConditionDirectives Empty = new PaperConditionDirectives(null, null, false, null, false, 1, false);

        public byte? SourceSlot { get; }
        public byte? TargetSlot { get; }
        public bool TargetGround { get; }
        /// <summary>How many blocks up the ground column may grow, counting the target block
        /// itself. 1 = only the target block (plain `target ground`), N = `target ground N`.</summary>
        public int TargetGroundHeight { get; }
        public decimal? Amount { get; }
        public bool RequireTargetEmpty { get; }

        /// <summary>
        /// `target firepit` — build a firepit at the target instead of putting things into it.
        /// Deliberately its own directive rather than something that just happens when dry grass
        /// meets open air: dropping grass on the ground is a perfectly ordinary thing to want, and
        /// it should not silently turn into construction work.
        /// </summary>
        public bool TargetFirepit { get; }

        public bool HasTargetOverride => TargetSlot.HasValue || TargetGround || TargetFirepit;
        public bool HasAmountOverride => Amount.HasValue;

        public PaperConditionDirectives(byte? sourceSlot, byte? targetSlot, bool targetGround, decimal? amount, bool requireTargetEmpty, int targetGroundHeight = 1, bool targetFirepit = false)
        {
            SourceSlot = sourceSlot;
            TargetSlot = targetSlot;
            TargetGround = targetGround;
            TargetGroundHeight = targetGroundHeight < 1 ? 1 : targetGroundHeight;
            Amount = amount;
            RequireTargetEmpty = requireTargetEmpty;
            TargetFirepit = targetFirepit;
        }

        public bool Evaluate(IDictionary<string, object> ctx)
        {
            // A `target firepit` block is only valid while there is a firepit to build. Once it
            // stands, the block stops matching and evaluation moves on to the next one - the same
            // rule `target N ifEmpty` follows when its slot fills up.
            if (TargetFirepit && !CanBuildFirepit(ctx)) return false;

            if (!RequireTargetEmpty) return true;
            if (TargetGround || TargetFirepit || !TargetSlot.HasValue || TargetSlot.Value <= 0) return false;
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

        private static bool CanBuildFirepit(IDictionary<string, object> ctx)
        {
            if (ctx == null) return false;
            if (!ctx.TryGetValue("world", out var worldObj) || worldObj is not Vintagestory.API.Common.IWorldAccessor world) return false;
            if (!ctx.TryGetValue("targetBlockPos", out var posObj) || posObj is not Vintagestory.API.MathTools.BlockPos pos) return false;

            return SignalsLink.src.signals.managedchute.transporting.FirepitConstruction.CanBuildAt(world, pos);
        }
    }
}
