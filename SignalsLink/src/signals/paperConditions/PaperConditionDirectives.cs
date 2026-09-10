namespace SignalsLink.src.signals.paperConditions
{
    /// <summary>
    /// What the number after <c>amount</c> is: a figure, a ceiling, or a floor.
    ///
    /// The marks are not new vocabulary. A condition has read <c>game:firewood 96+</c> as
    /// "ninety-six or more" from the start, so the same mark on an amount says the same thing
    /// about the same number - only as an instruction rather than a question.
    /// </summary>
    public enum AmountMode
    {
        /// <summary><c>amount 10</c> - ten, or nothing at all.</summary>
        Exactly,

        /// <summary><c>amount 10-</c> - ten at the most, and whatever is there will do.</summary>
        AtMost,

        /// <summary><c>amount 10+</c> - ten at the least, and if there is more, all of it at once.</summary>
        AtLeast,
    }

    public sealed class PaperConditionDirectives
    {
        public static readonly PaperConditionDirectives Empty = new PaperConditionDirectives(null, null, false, null, false, 1, false);

        /// <summary>
        /// Slot numbers are 1-based and have no upper bound: a composite inventory of several
        /// wagons is hundreds of slots, so the old 1-14 cap - which only ever mirrored what a
        /// four-bit signal pin can count to - would have quietly cut them off.
        /// </summary>
        public int? SourceSlot { get; }
        public int? TargetSlot { get; }

        /// <summary>
        /// `source last` / `target last` - the last slot of the inventory, whatever its size.
        ///
        /// Named rather than numbered on purpose. The Source PIN uses 15 for "last", so writing
        /// `source 15` on paper would mean slot fifteen while 15 on the wire means the last one:
        /// the same number meaning two things in two channels. With a word, numbers on paper are
        /// always literal and the pin encoding needs no change.
        /// </summary>
        public bool SourceLast { get; }
        public bool TargetLast { get; }
        public bool TargetGround { get; }
        /// <summary>How many blocks up the ground column may grow, counting the target block
        /// itself. 1 = only the target block (plain `target ground`), N = `target ground N`.</summary>
        public int TargetGroundHeight { get; }
        public decimal? Amount { get; }

        /// <summary>How the number is meant: exactly, at most, or at least. See <see cref="AmountMode"/>.</summary>
        public AmountMode AmountMode { get; }

        /// <summary>
        /// Must the whole amount be there before anything moves?
        ///
        /// True for <c>amount N</c> and <c>amount N+</c>, which both name a floor - a recipe that
        /// wants four hides wants four hides, and waiting is the point of writing it. False only
        /// for <c>amount N-</c>, which names a ceiling: fewer than ten is fewer than ten.
        /// </summary>
        public bool IsAtomicAmount => HasAmountOverride && AmountMode != AmountMode.AtMost;

        /// <summary>
        /// Does the number stop at itself, or is it a floor to reach past? Only <c>amount N+</c>
        /// takes everything it can once the floor is met - that is what makes it "clear the lot in
        /// one go" rather than "ten at a time".
        /// </summary>
        public bool TakesEverythingAvailable => HasAmountOverride && AmountMode == AmountMode.AtLeast;

        public bool RequireTargetEmpty { get; }

        /// <summary>
        /// `target firepit` — build a firepit at the target instead of putting things into it.
        /// Deliberately its own directive rather than something that just happens when dry grass
        /// meets open air: dropping grass on the ground is a perfectly ordinary thing to want, and
        /// it should not silently turn into construction work.
        /// </summary>
        public bool TargetFirepit { get; }

        public bool HasTargetOverride => TargetSlot.HasValue || TargetLast || TargetGround || TargetFirepit;
        public bool HasAmountOverride => Amount.HasValue;

        public PaperConditionDirectives(int? sourceSlot, int? targetSlot, bool targetGround, decimal? amount, bool requireTargetEmpty, int targetGroundHeight = 1, bool targetFirepit = false, bool sourceLast = false, bool targetLast = false, AmountMode amountMode = AmountMode.Exactly)
        {
            AmountMode = amountMode;
            SourceSlot = sourceSlot;
            TargetSlot = targetSlot;
            SourceLast = sourceLast;
            TargetLast = targetLast;
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
