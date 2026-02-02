using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace SignalsLink.src.signals.paperConditions
{
    public static class ItemConditionContextUtil
    {
        public static IDictionary<string, object> BuildContext(IWorldAccessor world, ItemStack stack)
        {
            var ctx = new Dictionary<string, object>();

            if (stack?.Collectible == null) return ctx;

            // Virtuální teplota
            try
            {
                float temp = stack.Collectible.GetTemperature(world, stack);
                ctx["temperature"] = temp;
            }
            catch
            {
                // Ignoruj, pokud collectible teplotu neumí
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

            // Stav zkažení (perish) jako 0..1
            try
            {
                TransitionableProperties[] transitionableProperties =
                    stack.Collectible.GetTransitionableProperties(world, stack, (Entity)null);

                if (transitionableProperties != null && transitionableProperties.Length > 0)
                {
                    // Vytvoøení dummy slotu ze stacku
                    var dummySlot = new DummySlot(stack);

                    // Aktualizace a získání transition stavù
                    var transitionStates = stack.Collectible.UpdateAndGetTransitionStates(world, dummySlot);

                    if (transitionStates != null)
                    {
                        // Najdi první perish transition state (typicky "perish")
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

                            // Staèí první nalezený perish state
                            break;
                        }
                    }
                }
            }
            catch
            {
                // pokud item nemá perish transition, nic se nepøidá
            }

            return ctx;
        }

        public static string BuildHintText(IWorldAccessor world, ItemStack stack)
        {
            var sb = new StringBuilder();

            // 1) První øádek: plný kód
            AppendLineLf(sb, stack.Collectible.Code.ToString());

            // 2) Virtuální hodnoty, které známe (napø. temperature)
            var ctx = BuildContext(world, stack);
            foreach(var kvp in ctx)
            {
                AppendLineLf(sb, $"{kvp.Key}={kvp.Value}");
            }

            // 3) Skuteèné atributy stacku
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
                            // pøeskoè prázdné øetìzce
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