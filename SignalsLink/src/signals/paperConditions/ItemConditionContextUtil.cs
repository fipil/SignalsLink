using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.paperConditions
{
    public static class ItemConditionContextUtil
    {
        public static IDictionary<string, object> BuildContext(IWorldAccessor world, ItemStack stack)
        {
            var ctx = new Dictionary<string, object>();

            ctx["stackSize"] = stack?.StackSize ?? 0;

            if (stack?.Collectible == null) return ctx;

            if (stack.Block is BlockLiquidContainerBase liquidContainer)
            {
                ItemStack liquidStack = liquidContainer.GetContent(stack);
                bool hasLiquid = liquidStack != null && liquidStack.StackSize > 0;
                ctx["isLiquidContainer"] = true;
                ctx["liquidContainerEmpty"] = !hasLiquid;
                ctx["liquidContainerFilled"] = hasLiquid;
                ctx["liquidLitres"] = 0d;

                if (hasLiquid)
                {
                    ctx["liquidCode"] = liquidStack.Collectible?.Code?.ToString();
                    var liquidProps = BlockLiquidContainerBase.GetContainableProps(liquidStack);
                    if (liquidProps?.ItemsPerLitre > 0)
                    {
                        ctx["liquidLitres"] = liquidStack.StackSize / (double)liquidProps.ItemsPerLitre;
                    }
                }
            }

            // Virtu�ln� teplota
            try
            {
                float temp = stack.Collectible.GetTemperature(world, stack);
                ctx["temperature"] = temp;
            }
            catch
            {
                // Ignoruj, pokud collectible teplotu neum�
            }

            // Durability (pokud item/block podporuje trvanlivost)
            int maxDurability = stack.Collectible.Durability;
            if (maxDurability > 0)
            {
                int current = stack.Attributes?.GetInt("durability", maxDurability) ?? maxDurability;
                ctx["durability"] = current;
                ctx["durabilityMax"] = maxDurability;
                ctx["durabilityRatio"] = (double)current / maxDurability;
            }

            // Stav zka�en� (perish) jako 0..1
            try
            {
                TransitionableProperties[] transitionableProperties =
                    stack.Collectible.GetTransitionableProperties(world, stack, (Entity)null);

                if (transitionableProperties != null && transitionableProperties.Length > 0)
                {
                    // Vytvo�en� dummy slotu ze stacku
                    var dummySlot = new DummySlot(stack);

                    // Aktualizace a z�sk�n� transition stav�
                    var transitionStates = stack.Collectible.UpdateAndGetTransitionStates(world, dummySlot);

                    if (transitionStates != null)
                    {
                        // Najdi prvn� perish transition state (typicky "perish")
                        foreach (var tstate in transitionStates)
                        {
                            if (tstate == null) continue;

                            var perishState = tstate;
                            if (perishState.TransitionHours > 0)
                            {
                                float freshHoursLeft = perishState.FreshHoursLeft;
                                ctx["freshHoursLeft"] = freshHoursLeft;              
                                ctx["isSpoiling"] = freshHoursLeft<=0;
                            }

                            // Sta�� prvn� nalezen� perish state
                            break;
                        }
                    }
                }
            }
            catch
            {
                // pokud item nem� perish transition, nic se nep�id�
            }

            return ctx;
        }

        public static string BuildHintText(IWorldAccessor world, ItemStack stack)
        {
            var sb = new StringBuilder();

            // 1) Prvn� ��dek: pln� k�d
            AppendLineLf(sb, stack.Collectible.Code.ToString());

            // 2) Virtu�ln� hodnoty, kter� zn�me (nap�. temperature)
            var ctx = BuildContext(world, stack);
            foreach(var kvp in ctx)
            {
                AppendLineLf(sb, $"{kvp.Key}={kvp.Value}");
            }

            // 3) Skute�n� atributy stacku
            var attrs = stack.Attributes;
            if (attrs != null)
            {
                foreach (var attrPair in attrs)
                {
                    var key = attrPair.Key;
                    var attr = attrPair.Value;
                    if (attr == null) continue;

                    string line = null;

                    switch (attr)
                    {
                        case IntAttribute ia:
                            line = $"{key}={ia.value}";
                            break;
                        case LongAttribute la:
                            line = $"{key}={la.value}";
                            break;
                        case FloatAttribute fa:
                            line = $"{key}={fa.value}";
                            break;
                        case DoubleAttribute da:
                            line = $"{key}={da.value}";
                            break;
                        case StringAttribute sa:
                            // p�esko� pr�zdn� �et�zce
                            if (!string.IsNullOrEmpty(sa.value))
                            {
                                line = $"{key}={sa.value}";
                            }
                            break;
                        case BoolAttribute ba:
                            line = $"{key}={ba.value}";
                            break;
                    }

                    if (!string.IsNullOrEmpty(line))
                    {
                        AppendLineLf(sb, line);
                    }
                }
            }

            return sb.ToString().TrimEnd('\r', '\n');
        }

        private static void AppendLineLf(StringBuilder sb, string line)
        {
            sb.Append(line);
            sb.Append('\n');
        }
    }
}