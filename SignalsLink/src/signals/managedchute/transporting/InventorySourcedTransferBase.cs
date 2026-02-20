using SignalsLink.src.signals.paperConditions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
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

        protected ItemSlot GetSourceSlot()
        {
            // 3) Konkrétní slot: 1–14 -> index (signal-1)
            if (inputSlotSignal > 0 && inputSlotSignal < 15)
            {
                int index = inputSlotSignal - 1;
                if (index >= 0 && index < sourceInv.Count)
                {
                    ItemSlot slot = sourceInv[index];
                    if (slot != null && !slot.Empty && IsConditionMet(slot.Itemstack) && !IsLiquidContainer(slot.Itemstack))
                    {
                        return slot;
                    }
                }
                return null;
            }

            // 2) 15 -> vždy POSLEDNÍ slot inventáře
            if (inputSlotSignal == 15)
            {
                if (sourceInv.Count == 0) return null;
                var slot = sourceInv[sourceInv.Count - 1];
                return IsLiquidContainer(slot.Itemstack) || !IsConditionMet(slot.Itemstack) ? null : slot;
            }

            // 1) 0 -> „vysávej všechny sloty“ = první NEprázdný, který není liquid container
            for (int i = 0; i < sourceInv.Count; i++)
            {
                ItemSlot slot = sourceInv[i];
                if (slot != null && !slot.Empty && IsConditionMet(slot.Itemstack) && !IsLiquidContainer(slot.Itemstack))
                {
                    return slot;
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
            if (conditionsEvaluator.HasConditions)
            {
                var ctx = ItemConditionContextUtil.BuildContext(api.World, stack);
                ctx["inventory"] = sourceInv;
                return conditionsEvaluator.Evaluate(stack, ctx, out byte blockIndex);
            }
            return true;
        }
    }
}
