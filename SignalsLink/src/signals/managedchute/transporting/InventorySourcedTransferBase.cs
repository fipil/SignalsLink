using SignalsLink.src.signals.paperConditions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.managedchute.transporting
{
    public class InventorySourcedTransferBase
    {
        protected readonly ICoreAPI api;

        protected readonly IInventory sourceInv;
        protected readonly byte inputSlotSignal;
        protected readonly PaperConditionsEvaluator conditionsEvaluator;

        public InventorySourcedTransferBase(ICoreAPI api, IInventory sourceInv, byte inputSlotSignal, PaperConditionsEvaluator conditionsEvaluator)
        {
            this.api = api;
            this.sourceInv = sourceInv;
            this.inputSlotSignal = inputSlotSignal;
            this.conditionsEvaluator = conditionsEvaluator;
        }

        /// <summary>
        /// The host's Output pin, when it has one. Null for a host without one, which then simply
        /// never hears what the output rail computed.
        /// </summary>
        public IConditionOutputSink OutputSink { get; set; }

        public virtual bool UsesAmountAsTriggerOnly => false;

        protected virtual bool AllowsLiquidContainers => false;

        protected ItemSlot GetSourceSlot()
        {
            return GetTransferSelection()?.SourceSlot;
        }

        protected TransferSelection GetTransferSelection()
        {
            RunPass(false, out TransferSelection selection);
            return selection;
        }

        /// <summary>
        /// One pass with the action rail closed. The host calls this on ticks where it cannot move
        /// anything — no input credit, no turn on the line, nothing to carry — so that the Output
        /// pin keeps reporting the current state instead of freezing on the last thing that
        /// happened to change it.
        /// </summary>
        public void EvaluateOutputs()
        {
            RunPass(true, out _);
        }

        /// <summary>
        /// One evaluation pass over the paper (see paper-conditions-rules-v2.md): a single walk in
        /// the order the blocks are written, carrying the output rail and the action rail at once.
        ///
        /// The loops are nested paper-outer / slots-inner, which is the change the whole revision
        /// rests on. Before, the paper was walked once per source slot, so the chest layout decided
        /// which block won — a block further down would beat one above it merely because its
        /// material happened to sit in a lower slot. Now a block searches every slot it is allowed
        /// to before handing over to the next block, so the paper decides.
        /// </summary>
        /// <param name="actionsBlocked">Closes the action rail; the output rail runs regardless.</param>
        /// <param name="selection">The slot and directives to transfer with, or null.</param>
        public DriverResult RunPass(bool actionsBlocked, out TransferSelection selection)
        {
            selection = null;
            actingBlock = null;

            IReadOnlyList<ConditionBlock> blocks = conditionsEvaluator?.GetBlocks();

            if (blocks == null || blocks.Count == 0)
            {
                // No paper at all: nothing can drive the pin, so it reads 0, and the slot is picked
                // by the Source pin alone, exactly as it always was.
                if (ConditionDebug.Enabled) ConditionDebug.Log("pass: no paper -> output 0");
                if (!actionsBlocked) selection = SelectWithoutPaper();
                ApplyOutput(DriverResult.Nothing);
                return DriverResult.Nothing;
            }

            IDictionary<string, object> outputCtx = null;
            IDictionary<string, object> directiveCtx = null;
            TransferSelection picked = null;

            DriverResult result = ConditionDriver.Run(
                blocks,
                actionsBlocked,
                block =>
                {
                    // Output blocks report on the target and are asked with no stack at all -
                    // there is nothing being carried when the action rail is closed.
                    outputCtx ??= BuildConditionContext(null);
                    bool holds = block.OutputConditionsHold(outputCtx);

                    if (ConditionDebug.Enabled)
                    {
                        ConditionDebug.Log("  output block value=" + block.OutputValue + " holds=" + holds
                            + " | " + ConditionDebug.Describe(outputCtx, "targetInventory"));
                    }

                    return holds;
                },
                block =>
                {
                    directiveCtx ??= BuildDirectiveContext();

                    // A block that says WHAT to carry gets to carry it. Its own actions are not
                    // run here but after the move (see RunActionsAfterTransfer), so that
                    // `game:water / do seal` fills the barrel before it seals it.
                    if (block.CanSelectSource)
                    {
                        TransferSelection candidate = SelectForBlock(block, directiveCtx);

                        if (candidate != null)
                        {
                            picked = candidate;
                            actingBlock = block;
                            return true;
                        }
                    }

                    // Nothing to carry, or nothing it could carry right now - so do what else the
                    // block says. That second half is the point: a paper that puts the kettle out
                    // BECAUSE nothing is moving must not be stopped by nothing moving.
                    return ConditionActions.RunOn(block, directiveCtx);
                });

            selection = picked;

            if (ConditionDebug.Enabled)
            {
                ConditionDebug.Log("pass actionsBlocked=" + actionsBlocked + " blocks=" + blocks.Count
                    + " -> output=" + result.GetOutput() + " claimed=" + result.HasOutput()
                    + " selection=" + (picked == null ? "<none>" : picked.SourceSlot?.Itemstack?.Collectible?.Code?.ToString() ?? "?"));
            }

            ApplyOutput(result);
            return result;
        }

        /// <summary>
        /// The block the pass chose to carry with, kept so that its own actions can be run once
        /// the goods have actually moved.
        /// </summary>
        private ConditionBlock actingBlock;

        /// <summary>
        /// The actions of the block that just carried something, run now that it has. A block
        /// that both fills and seals means fill THEN seal, and there is no way to say that from
        /// inside the pass - the goods have not moved yet while the pass is running.
        /// </summary>
        protected void RunActionsAfterTransfer()
        {
            ConditionActions.RunOn(actingBlock, BuildDirectiveContext());
        }

        /// <summary>
        /// The slots one action block may take from, in the order it tries them.
        /// </summary>
        private IEnumerable<int> CandidateSlots(PaperConditionDirectives directives)
        {
            // `source last` names the end of the inventory, whatever its size.
            if (directives.SourceLast)
            {
                if (sourceInv.Count > 0) yield return sourceInv.Count - 1;
                yield break;
            }

            // `source N` names one slot and overrides the Source pin.
            if (directives.SourceSlot.HasValue)
            {
                yield return directives.SourceSlot.Value - 1;
                yield break;
            }

            // Source pin: 1..14 = that slot, 15 = the last one, 0 = every slot in order.
            if (inputSlotSignal > 0 && inputSlotSignal < 15)
            {
                yield return inputSlotSignal - 1;
                yield break;
            }

            if (inputSlotSignal == 15)
            {
                if (sourceInv.Count > 0) yield return sourceInv.Count - 1;
                yield break;
            }

            for (int i = 0; i < sourceInv.Count; i++) yield return i;
        }

        private TransferSelection SelectForBlock(ConditionBlock block, IDictionary<string, object> directiveCtx)
        {
            foreach (int index in CandidateSlots(block.Directives))
            {
                ItemSlot slot = GetUsableSlot(index);
                if (slot == null) continue;

                IDictionary<string, object> ctx = BuildConditionContext(slot.Itemstack);

                if (!block.TryMatch(slot.Itemstack, ctx)) continue;
                if (!block.Directives.Evaluate(directiveCtx)) continue;

                // `keep N` is a level, so it is also a gate: at the level this block has nothing
                // to do and the walk carries on to the one below it.
                decimal? room = RoomFor(block, directiveCtx);
                if (room is <= 0) continue;

                if (!CanTransferSelection(slot, block.Directives)) continue;

                return new TransferSelection(slot, block.Directives, room);
            }

            return null;
        }

        /// <summary>
        /// The batch, held down to what `keep N` still wants. A level is not a batch: it never
        /// waits for a full one and it never overshoots.
        /// </summary>
        protected static int CappedByKeep(int quantity, TransferSelection selection)
        {
            if (selection?.Room == null) return quantity;
            if (selection.Room.Value <= 0) return 0;

            int room = (int)decimal.Truncate(selection.Room.Value);

            return quantity > room ? room : quantity;
        }

        /// <summary>What `keep N` still wants in the target, or null when the block said no such thing.</summary>
        private static decimal? RoomFor(ConditionBlock block, IDictionary<string, object> ctx)
        {
            if (!block.Directives.HasKeep) return null;
            if (ctx == null || !ctx.TryGetValue("targetInventory", out object obj)) return null;

            return block.Directives.Keep.Value - block.CountInTarget(obj as IInventory, ctx);
        }

        private TransferSelection SelectWithoutPaper()
        {
            foreach (int index in CandidateSlots(PaperConditionDirectives.Empty))
            {
                ItemSlot slot = GetUsableSlot(index);
                if (slot == null) continue;
                if (!CanTransferSelection(slot, PaperConditionDirectives.Empty)) continue;

                return new TransferSelection(slot, PaperConditionDirectives.Empty);
            }

            return null;
        }

        private ItemSlot GetUsableSlot(int index)
        {
            if (index < 0 || index >= sourceInv.Count) return null;

            ItemSlot slot = sourceInv[index];
            if (slot == null || slot.Empty) return null;
            if (IsLiquidContainer(slot.Itemstack) && !AllowsLiquidContainers) return null;

            return slot;
        }

        private void ApplyOutput(DriverResult result)
        {
            OutputSink?.ApplyOutput(0, result.GetOutput());
        }

        protected bool IsLiquidContainer(ItemStack stack)
        {
            if (stack?.Collectible == null) return false;

            // Sudy, džbány, kbelíky atd.
            if (stack.Collectible is BlockLiquidContainerBase) return true;

            // Obecné liquid rozhraní (pro jistotu)
            if (stack.Collectible is ILiquidInterface) return true;

            // ItemLiquidPortion is internal, so check by type name string instead
            if (stack.Collectible.GetType().Name == "ItemLiquidPortion") return true;

            return false;
        }

        protected bool IsConditionMet(ItemStack stack)
        {
            return TryGetMatchedDirectives(stack, out _);
        }

        protected virtual bool CanTransferSelection(ItemSlot slot, PaperConditionDirectives directives)
        {
            return true;
        }

        protected bool TryGetMatchedDirectives(ItemStack stack, out PaperConditionDirectives directives)
        {
            return TryGetMatchedDirectives(stack, null, out directives);
        }

        protected bool TryGetMatchedDirectives(ItemStack stack, System.Func<PaperConditionDirectives, bool> canUse, out PaperConditionDirectives directives)
        {
            directives = PaperConditionDirectives.Empty;

            // With no paper everything is accepted, so there is no context worth building.
            if (conditionsEvaluator == null || !conditionsEvaluator.HasConditions) return true;

            return ConditionResolution.ResolveActionDirectives(
                conditionsEvaluator, stack, BuildConditionContext(stack), out directives, canUse);
        }

        protected virtual void AddConditionContext(IDictionary<string, object> ctx)
        {
        }

        private IDictionary<string, object> BuildConditionContext(ItemStack stack)
        {
            IDictionary<string, object> ctx = ConditionContext.Build(api, stack, sourceInv);

            // The subclass knows what its target end is; everything that follows from it is derived.
            AddConditionContext(ctx);
            ConditionContext.Complete(api, ctx);

            return ctx;
        }

        /// <summary>
        /// Directives are answered against the same context as conditions. They used to get a
        /// smaller one of their own, which is the kind of difference that makes a paper mean two
        /// things at once.
        /// </summary>
        protected IDictionary<string, object> BuildDirectiveContext()
        {
            return BuildConditionContext(null);
        }
    }
}
