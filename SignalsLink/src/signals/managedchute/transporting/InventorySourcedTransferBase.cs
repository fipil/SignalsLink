using SignalsLink.src.signals.paperConditions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent; // nahoře v souboru

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
        /// The host's Output pin, when it has one. Null for the ManagedChute, which has none — and
        /// with it null this class resolves conditions exactly as it always did.
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
            if (conditionsEvaluator.HasConditions)
            {
                for (int i = 0; i < sourceInv.Count; i++)
                {
                    if (TryCreateTransferSelection(sourceInv[i], i, true, out TransferSelection selection))
                    {
                        return selection;
                    }
                }
            }

            // 3) Konkrétní slot: 1–14 -> index (signal-1)
            if (inputSlotSignal > 0 && inputSlotSignal < 15)
            {
                int index = inputSlotSignal - 1;
                if (index >= 0 && index < sourceInv.Count)
                {
                    ItemSlot slot = sourceInv[index];
                    if (TryCreateTransferSelection(slot, index, false, out TransferSelection selection))
                    {
                        return selection;
                    }
                }
                return null;
            }

            // 2) 15 -> vždy POSLEDNÍ slot inventáře
            if (inputSlotSignal == 15)
            {
                if (sourceInv.Count == 0) return null;
                var slot = sourceInv[sourceInv.Count - 1];
                return TryCreateTransferSelection(slot, sourceInv.Count - 1, false, out TransferSelection selection) ? selection : null;
            }

            // 1) 0 -> „vysávej všechny sloty“ = první NEprázdný, který není liquid container
            for (int i = 0; i < sourceInv.Count; i++)
            {
                ItemSlot slot = sourceInv[i];
                if (TryCreateTransferSelection(slot, i, false, out TransferSelection selection))
                {
                    return selection;
                }
            }

            RunOutputOnlyPass();
            return null;
        }

        /// <summary>
        /// Conditions are normally evaluated against a source stack, one slot at a time — so when
        /// there is nothing left to carry, nothing gets evaluated and the Output pin freezes on
        /// whatever it last said. That is wrong for a block that reports on its TARGET: an emptied
        /// column should be able to announce that it is empty even though the source ran dry too.
        ///
        /// So when no slot yielded a transfer, the blocks get one more pass with no stack at all.
        /// Only <c>output</c> blocks can win it — there is nothing to move, which is exactly what
        /// the always-false predicate says.
        /// </summary>
        public void EvaluateOutputs()
        {
            RunOutputOnlyPass();
        }

        private void RunOutputOnlyPass()
        {
            if (OutputSink == null || conditionsEvaluator == null || !conditionsEvaluator.HasConditions) return;

            var ctx = ItemConditionContextUtil.BuildContext(api.World, null);
            ctx["sourceInventory"] = sourceInv;
            ctx["inventory"] = sourceInv;
            AddConditionContext(ctx);

            ConditionResolution.ResolveDirectives(conditionsEvaluator, OutputSink, null, ctx, out _, _ => false);
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
            var ctx = ItemConditionContextUtil.BuildContext(api.World, stack);
            ctx["sourceInventory"] = sourceInv;
            ctx["inventory"] = sourceInv;
            AddConditionContext(ctx);

            return ConditionResolution.ResolveDirectives(conditionsEvaluator, OutputSink, stack, ctx, out directives, canUse);
        }

        protected virtual void AddConditionContext(IDictionary<string, object> ctx)
        {
        }

        private bool TryCreateTransferSelection(ItemSlot slot, int slotIndex, bool requireExplicitSource, out TransferSelection selection)
        {
            selection = null;

            if (slot == null || slot.Empty) return false;
            if (IsLiquidContainer(slot.Itemstack) && !AllowsLiquidContainers) return false;

            bool CanUse(PaperConditionDirectives d)
            {
                if (requireExplicitSource)
                {
                    if (d.SourceSlot != slotIndex + 1) return false;
                }
                else if (d.SourceSlot.HasValue)
                {
                    return false;
                }

                if (!d.Evaluate(BuildDirectiveContext())) return false;
                return CanTransferSelection(slot, d);
            }

            // Handed to the walk so a block that cannot move anything falls through to the next one
            // instead of killing the whole slot. The sink-less path (ManagedChute) does not walk, so
            // there the same checks run once, afterwards, exactly as they always did.
            if (!TryGetMatchedDirectives(slot.Itemstack, CanUse, out PaperConditionDirectives directives)) return false;
            if (OutputSink == null && !CanUse(directives)) return false;

            selection = new TransferSelection(slot, directives);
            return true;
        }

        protected IDictionary<string, object> BuildDirectiveContext()
        {
            var ctx = new Dictionary<string, object>();
            // Directives that ask about a block (target firepit) need something to ask with.
            ctx["world"] = api.World;
            AddConditionContext(ctx);
            return ctx;
        }
    }
}
