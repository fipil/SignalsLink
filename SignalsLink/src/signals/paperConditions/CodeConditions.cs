using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace SignalsLink.src.signals.paperConditions
{
    public class CodeGlobCondition : ICondition
    {
        private readonly Regex regex;

        public CodeGlobCondition(string glob)
        {
            var pattern = "^" + Regex.Escape(glob).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            regex = new Regex(pattern, RegexOptions.Compiled);
        }

        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx)
        {
            foreach (string code in ConditionCodeHelper.GetCodes(stack))
            {
                if (regex.IsMatch(code)) return true;
            }

            return false;
        }
    }

    public class CodeRegexCondition : ICondition
    {
        private readonly Regex regex;

        public CodeRegexCondition(Regex regex)
        {
            this.regex = regex;
        }

        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx)
        {
            foreach (string code in ConditionCodeHelper.GetCodes(stack))
            {
                if (regex.IsMatch(code)) return true;
            }

            return false;
        }
    }

    public static class ConditionCodeHelper
    {
        public static IEnumerable<string> GetCodes(ItemStack stack)
        {
            if (stack?.Collectible?.Code == null) yield break;

            string collectibleCode = stack.Collectible.Code.ToString();
            if (!string.IsNullOrEmpty(collectibleCode))
            {
                yield return collectibleCode;
            }

            foreach (string spilledCode in GetLiquidSpilledCodes(stack))
            {
                if (!string.IsNullOrEmpty(spilledCode) && !string.Equals(spilledCode, collectibleCode, StringComparison.Ordinal))
                {
                    yield return spilledCode;
                }
            }
        }

        private static IEnumerable<string> GetLiquidSpilledCodes(ItemStack stack)
        {
            var whenSpilled = stack?.ItemAttributes?["waterTightContainerProps"]?["whenSpilled"];
            if (whenSpilled == null) yield break;

            string stackCode = whenSpilled["stack"]?["code"].AsString(null);
            if (!string.IsNullOrEmpty(stackCode))
            {
                yield return stackCode;
            }

            Dictionary<string, JsonItemStack> stackByFillLevel = whenSpilled["stackByFillLevel"]?.AsObject<Dictionary<string, JsonItemStack>>(null);
            if (stackByFillLevel == null) yield break;

            foreach (JsonItemStack jsonStack in stackByFillLevel.Values)
            {
                string fillLevelCode = jsonStack?.Code?.ToString();
                if (!string.IsNullOrEmpty(fillLevelCode))
                {
                    yield return fillLevelCode;
                }
            }
        }
    }
}