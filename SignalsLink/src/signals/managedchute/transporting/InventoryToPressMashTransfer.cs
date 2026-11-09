using System.Collections.Generic;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.managedchute.transporting
{
    /// <summary>
    /// Fruit into the press, through its top block. The press decides what fits (kind, litres,
    /// screw up); `amount N` is a batch gathered from every matching source slot, atomic unless
    /// it says `N-`. `in target` sees the mash slot alone.
    /// </summary>
    public class InventoryToPressMashTransfer : InventorySourcedTransferBase, IItemTransfer
    {
        private readonly BlockEntityFruitPress press;
        private SingleSlotView mashView;

        public InventoryToPressMashTransfer(ICoreAPI api, IInventory sourceInv, BlockEntityFruitPress press, byte inputSlotSignal, PaperConditionsEvaluator conditionsEvaluator)
            : base(api, sourceInv, inputSlotSignal, conditionsEvaluator)
        {
            this.press = press;
        }

        protected override IInventory TargetInventory => mashView ??= new SingleSlotView(press.MashSlot, api);

        protected override void AddConditionContext(IDictionary<string, object> ctx)
        {
            ctx["targetInventory"] = TargetInventory;
            ctx["targetBlockPos"] = press.Pos;
        }

        protected override bool CanTransferSelection(ItemSlot slot, PaperConditionDirectives directives)
            => FruitPressMash.CanInsert(press, slot?.Itemstack);

        public TransferOperationResult TryMove(ItemStackMoveOperation opTemplate)
            => RunTransferPass(opTemplate, () => MoveSelected(opTemplate));

        private TransferOperationResult MoveSelected(ItemStackMoveOperation opTemplate)
        {
            TransferSelection selection = GetTransferSelection();
            ItemSlot src = selection?.SourceSlot;
            if (src == null || src.Empty) return TransferOperationResult.None;

            PaperConditionDirectives directives = selection.Directives;
            int wanted = System.Math.Max(1, (int)(directives.Amount ?? opTemplate.RequestedQuantity));

            List<ItemSlot> sources = MatchingSourceSlots(src);
            int available = 0;
            foreach (ItemSlot s in sources) available += s.StackSize;

            JuiceableProperties props = press.getJuiceableProps(src.Itemstack);
            int fits = props?.LitresPerItem.HasValue == true
                ? FruitPressMash.FitCount(wanted, props.LitresPerItem.Value,
                    FruitPressMash.LitresLeft(press.MashSlot.Itemstack) + FruitPressMash.LitresTransfered(press.MashSlot.Itemstack),
                    FruitPressMash.Capacity(press))
                : 1;

            // A floor waits for the whole batch to be there and to fit; a ceiling takes what it can.
            if (directives.IsAtomicAmount && (available < wanted || fits < wanted)) return TransferOperationResult.None;

            int remaining = System.Math.Min(wanted, System.Math.Min(available, fits));
            int moved = 0;
            foreach (ItemSlot s in sources)
            {
                if (remaining <= 0) break;
                int n = FruitPressMash.Insert(press, s, remaining);
                if (n <= 0) break;
                moved += n;
                remaining -= n;
            }

            return moved > 0 ? new TransferOperationResult(moved, moved, false) : TransferOperationResult.None;
        }

        /// <summary>The selected slot first, then every other candidate slot holding the same thing.</summary>
        private List<ItemSlot> MatchingSourceSlots(ItemSlot initial)
        {
            List<ItemSlot> list = new List<ItemSlot> { initial };
            ItemStack reference = initial.Itemstack;

            foreach (int i in CandidateSlots(ActiveDirectives))
            {
                if (i < 0 || i >= sourceInv.Count) continue;
                ItemSlot slot = sourceInv[i];
                if (slot == null || ReferenceEquals(slot, initial) || slot.Empty) continue;
                if (!slot.Itemstack.Equals(api.World, reference, GlobalConstants.IgnoredStackAttributes)) continue;
                if (!MatchesActiveBlock(slot.Itemstack)) continue;
                list.Add(slot);
            }

            return list;
        }

        public int TryMoveOneItem(ItemStackMoveOperation opTemplate)
            => (int)TryMove(opTemplate).MovedAmount;
    }
}
