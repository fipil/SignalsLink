using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace SignalsLink.src.signals.paperConditions
{
    public class CodeGlobCondition : ICondition
    {
        private readonly string glob;
        public CodeGlobCondition(string glob) { this.glob = glob; }

        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx)
        {
            foreach (string code in ConditionCodeHelper.GetCodes(stack))
                if (Matches(code)) return true;
            return false;
        }

        // One remembered star, never a recursive/exponential regex search. At worst each
        // position in the short item code retries the remaining literal part of the glob.
        private bool Matches(string code)
        {
            int pattern = 0, input = 0, star = -1, retry = 0;
            while (input < code.Length)
            {
                if (pattern < glob.Length && (glob[pattern] == '?' || glob[pattern] == code[input]))
                { pattern++; input++; }
                else if (pattern < glob.Length && glob[pattern] == '*')
                { star = pattern++; retry = input; }
                else if (star >= 0)
                { pattern = star + 1; input = ++retry; }
                else return false;
            }
            while (pattern < glob.Length && glob[pattern] == '*') pattern++;
            return pattern == glob.Length;
        }
    }

    public class CodeRegexCondition : ICondition
    {
        private readonly Regex regex;
        private long? lastWarning;

        public CodeRegexCondition(Regex regex)
        {
            this.regex = new Regex(regex.ToString(), regex.Options & ~RegexOptions.Compiled, TimeSpan.FromSeconds(1));
        }

        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx)
        {
            foreach (string code in ConditionCodeHelper.GetCodes(stack))
            {
                long failureVersion = RegexEvaluationBudget.FailureVersion;
                try
                {
                    if (RegexEvaluationBudget.IsMatch(regex, code,
                        () => Warn(ctx, code, "shared regex budget of 5000 ms exhausted"))) return true;
                    if (RegexEvaluationBudget.FailureVersion != failureVersion) return false;
                }
                catch (RegexMatchTimeoutException)
                {
                    Warn(ctx, code, "regex timeout of 1000 ms exceeded");
                    return RegexEvaluationBudget.Reject();
                }
            }

            return false;
        }
        private void Warn(IDictionary<string, object> ctx, string code, string reason)
        {
            var world = ctx != null && ctx.TryGetValue("world", out var w) ? w as IWorldAccessor : null;
            if (world != null && world.Side != EnumAppSide.Server) return;
            var logger = RegexDiagnostics.Logger ?? world?.Logger;
            if (logger == null) return;
            long now = Environment.TickCount64;
            if (lastWarning.HasValue && now - lastWarning.Value < 60000) return;
            lastWarning = now;
            string location = RegexDiagnostics.Location;
            if (location == null)
                location = ctx != null && ctx.TryGetValue("targetBlockPos", out var p) ? "target " + p : "unknown device";
            logger.Warning("[SignalsLink] Paper Conditions: " + reason + " at " + location
                + "; condition @" + regex.ToString().Replace("\r", "\\r").Replace("\n", "\\n")
                + "; input " + code.Replace("\r", "\\r").Replace("\n", "\\n")
                + ". This evaluation was rejected; the condition will be retried on a later pass."
                + " Repeated warnings for this condition are limited to once per 60 seconds.");
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
