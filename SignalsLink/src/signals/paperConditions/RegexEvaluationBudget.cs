using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SignalsLink.src.signals.paperConditions
{
    /// <summary>Nested device/section/transfer passes share one budget for regex matching.</summary>
    public sealed class RegexEvaluationBudget : IDisposable
    {
        [ThreadStatic] private static RegexEvaluationBudget current;
        [ThreadStatic] private static long failureVersion;
        // Callers distinguish a failed evaluation from a legitimate non-match (including zero counts).
        public static long FailureVersion => failureVersion;
        public static bool Reject() { unchecked { failureVersion++; } return false; }
        private readonly bool ownsBudget;
        private long remaining = Stopwatch.Frequency * 5; // 5 seconds in regexes per outer pass
        private RegexEvaluationBudget(bool ownsBudget) { this.ownsBudget = ownsBudget; }
        public static RegexEvaluationBudget Begin()
        {
            var scope = new RegexEvaluationBudget(current == null);
            if (scope.ownsBudget) current = scope;
            return scope;
        }
        public static bool IsMatch(Regex regex, string input, Action budgetExceeded = null)
        {
            if (current == null) return regex.IsMatch(input);
            if (current.remaining <= 0) { budgetExceeded?.Invoke(); return Reject(); }
            long start = Stopwatch.GetTimestamp();
            try { return regex.IsMatch(input); }
            finally { current.remaining -= Stopwatch.GetTimestamp() - start; }
        }
        public void Dispose() { if (ownsBudget) current = null; }
    }
}
