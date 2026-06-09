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

        protected bool canTransferLiquids = false;

        public InventorySourcedTransferBase(ICoreAPI api, IInventory sourceInv, byte inputSlotSignal, PaperConditionsEvaluator conditionsEvaluator)
        {
            this.api = api;
            this.sourceInv = sourceInv;
            this.inputSlotSignal = inputSlotSignal;
            this.conditionsEvaluator = conditionsEvaluator;
        }

        public virtual bool UsesAmountAsTriggerOnly => false;

        protected ItemSlot GetSourceSlot()
        {
            return GetTransferSelection()?.SourceSlot;
        }

        protected TransferSelection GetTransferSelection()
        {
            // 3) Konkrétní slot: 1–14 -> index (signal-1)
            if (inputSlotSignal > 0 && inputSlotSignal < 15)
            {
                int index = inputSlotSignal - 1;
                if (index >= 0 && index < sourceInv.Count)
                {
                    ItemSlot slot = sourceInv[index];
                    if (TryCreateTransferSelection(slot, out TransferSelection selection))
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
                return TryCreateTransferSelection(slot, out TransferSelection selection) ? selection : null;
            }

            // 1) 0 -> „vysávej všechny sloty“ = první NEprázdný, který není liquid container
            for (int i = 0; i < sourceInv.Count; i++)
            {
                ItemSlot slot = sourceInv[i];
                if (TryCreateTransferSelection(slot, out TransferSelection selection))
                {
                    return selection;
                }
            }

            return null;
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
            directives = PaperConditionDirectives.Empty;
            var ctx = ItemConditionContextUtil.BuildContext(api.World, stack);
            ctx["inventory"] = sourceInv;
            AddConditionContext(ctx);

            if (conditionsEvaluator.HasConditions)
            {
                return conditionsEvaluator.Evaluate(stack, ctx, out byte blockIndex, out directives);
            }
            return true;
        }

        protected virtual void AddConditionContext(IDictionary<string, object> ctx)
        {
        }

        private bool TryCreateTransferSelection(ItemSlot slot, out TransferSelection selection)
        {
            selection = null;

            if (slot == null || slot.Empty) return false;
            if (IsLiquidContainer(slot.Itemstack) && !canTransferLiquids) return false;
            if (!TryGetMatchedDirectives(slot.Itemstack, out PaperConditionDirectives directives)) return false;
            if (!CanTransferSelection(slot, directives)) return false;

            selection = new TransferSelection(slot, directives);
            return true;
        }
    }
}
