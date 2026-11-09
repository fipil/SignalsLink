using System.Collections.Generic;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.managedchute.transporting
{
    /// <summary>
    /// Mash out of the press, through its top block, into an inventory. Conditions see the mash as
    /// it lies in the press (a copy, attributes included), also while the screw is down - that is
    /// when `juiceableLitresLeft&lt;0.01 / do release` has to hold. What actually leaves is the
    /// dry result once pressed out: the press converts it on the way out and may do so only once,
    /// so the room for it is checked first and the whole stack moves, or nothing does.
    /// </summary>
    public class PressMashToInventoryTransfer : InventorySourcedTransferBase, IItemTransfer
    {
        private readonly BlockEntityFruitPress press;
        private readonly IInventory targetInv;
        private readonly BlockPos targetPos;
        private readonly byte outputSlotSignal;

        public PressMashToInventoryTransfer(ICoreAPI api, BlockEntityFruitPress press, IInventory targetInv, BlockPos targetPos, byte outputSlotSignal, PaperConditionsEvaluator conditionsEvaluator)
            : base(api, new InventoryGeneric(1, "signalslink-mashpreview", api), 0, conditionsEvaluator)
        {
            this.press = press;
            this.targetInv = targetInv;
            this.targetPos = targetPos;
            this.outputSlotSignal = outputSlotSignal;
        }

        protected override IInventory TargetInventory => targetInv;

        protected override void AddConditionContext(IDictionary<string, object> ctx)
        {
            ctx["sourceBlockPos"] = press.Pos;
            ctx["targetInventory"] = targetInv;
            if (targetPos != null) ctx["targetBlockPos"] = targetPos;
        }

        private void RefreshPreview()
        {
            sourceInv[0].Itemstack = press?.MashSlot?.Itemstack?.Clone();
        }

        public new void EvaluateOutputs()
        {
            RefreshPreview();
            base.EvaluateOutputs();
        }

        public TransferOperationResult TryMove(ItemStackMoveOperation opTemplate)
        {
            RefreshPreview();
            return RunTransferPass(opTemplate, MoveSelected);
        }

        private TransferOperationResult MoveSelected()
        {
            TransferSelection selection = GetTransferSelection();
            if (selection?.SourceSlot?.Itemstack == null || !FruitPressMash.CanTake(press)) return TransferOperationResult.None;

            // What will come out, not what lies inside: dry mash once pressed out.
            ItemStack outgoing = FruitPressMash.PreviewTake(press);
            if (outgoing == null || outgoing.StackSize <= 0) return TransferOperationResult.None;

            int slotSignal = EffectiveTargetSlot(selection.Directives);
            if (RoomFor(outgoing, slotSignal) < outgoing.StackSize) return TransferOperationResult.None;

            ItemStack taken = FruitPressMash.Take(press);
            if (taken == null) return TransferOperationResult.None;

            int moved = PutInto(taken, slotSignal);
            if (taken.StackSize > 0)
            {
                // Room was checked; this is the last resort so nothing is ever lost.
                api.World.SpawnItemEntity(taken, press.Pos.ToVec3d().Add(0.5, 1.5, 0.5));
            }

            sourceInv[0].Itemstack = null;
            return moved > 0 ? new TransferOperationResult(moved, moved, false) : TransferOperationResult.None;
        }

        private int EffectiveTargetSlot(PaperConditionDirectives directives)
        {
            if (directives == null) return outputSlotSignal;
            if (directives.TargetLast) return targetInv.Count;
            return directives.TargetSlot ?? outputSlotSignal;
        }

        private IEnumerable<ItemSlot> TargetSlots(int slotSignal)
        {
            if (slotSignal > 0)
            {
                int index = slotSignal - 1;
                if (index >= 0 && index < targetInv.Count) yield return targetInv[index];
                yield break;
            }

            for (int i = 0; i < targetInv.Count; i++) yield return targetInv[i];
        }

        private int RoomFor(ItemStack stack, int slotSignal)
        {
            int room = 0;
            foreach (ItemSlot slot in TargetSlots(slotSignal)) room += RoomInSlot(slot, stack);
            return room;
        }

        private int RoomInSlot(ItemSlot slot, ItemStack stack)
        {
            if (slot == null || stack?.Collectible == null) return 0;
            DummySlot probe = new DummySlot(stack);
            if (!slot.CanHold(probe) || !slot.CanTakeFrom(probe, EnumMergePriority.DirectMerge)) return 0;
            if (slot.Empty) return System.Math.Min(stack.Collectible.MaxStackSize, slot.GetRemainingSlotSpace(stack));
            if (!slot.Itemstack.Equals(api.World, stack, Vintagestory.API.Config.GlobalConstants.IgnoredStackAttributes)) return 0;
            return System.Math.Max(0, System.Math.Min(stack.Collectible.MaxStackSize - slot.StackSize, slot.GetRemainingSlotSpace(stack)));
        }

        private int PutInto(ItemStack stack, int slotSignal)
        {
            int moved = 0;
            DummySlot carrier = new DummySlot(stack);
            foreach (ItemSlot slot in TargetSlots(slotSignal))
            {
                if (stack.StackSize <= 0) break;
                int n = carrier.TryPutInto(api.World, slot, stack.StackSize);
                if (n > 0) { moved += n; slot.MarkDirty(); }
            }
            return moved;
        }

        public int TryMoveOneItem(ItemStackMoveOperation opTemplate)
            => (int)TryMove(opTemplate).MovedAmount;
    }
}
