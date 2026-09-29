using System;
using System.Collections.Generic;
using SignalsLink.src.signals.cargo;

namespace SignalsLink.src.signals.manageddock
{
    /// <summary>A bounded ordered search, resumed across ticks until a move or a complete sweep.</summary>
    public sealed class DockActionCursor
    {
        private int resumeAction;
        private long resumePair;
        private int actionIndex;
        private int budget;
        public int CheckedPairsThisTick { get; private set; }
        private (string Code, Vintagestory.API.MathTools.BlockPos Pos)[] sourceLayout, targetLayout;
        public bool Deferred { get; private set; }
        public void Reset() { resumeAction = 0; resumePair = 0; sourceLayout = targetLayout = null; }
        public void BeginTick(int maxPairs) { actionIndex = 0; budget = maxPairs; CheckedPairsThisTick = 0; Deferred = false; }
        public void EndTick() { if (!Deferred) Reset(); }

        public bool TryRun(IReadOnlyList<ICargoHold> sources, IReadOnlyList<ICargoHold> targets,
            bool needsSource, Func<ICargoHold, ICargoHold, bool> attempt, int? order = null)
        {
            int index = order ?? actionIndex++;
            if (Deferred || index < resumeAction) return false;
            if (sources == null || targets == null || targets.Count == 0) return false;
            if (index == resumeAction && resumePair > 0
                && (!SameLayout(sources, sourceLayout) || !SameLayout(targets, targetLayout))) resumePair = 0;
            long count = (long)sources.Count * targets.Count;
            for (long pair = index == resumeAction ? resumePair : 0; pair < count; pair++)
            {
                if (budget <= 0)
                {
                    resumeAction = index; resumePair = pair; Deferred = true;
                    if (!SameLayout(sources, sourceLayout)) sourceLayout = Layout(sources);
                    if (!SameLayout(targets, targetLayout)) targetLayout = Layout(targets);
                    return false;
                }
                budget--;
                CheckedPairsThisTick++;
                var source = sources[(int)(pair / targets.Count)];
                var target = targets[(int)(pair % targets.Count)];
                if (source == null || target == null || ReferenceEquals(source, target)) continue;
                if (needsSource && source.IsEmpty)
                {
                    pair = (pair / targets.Count + 1) * targets.Count - 1;
                    continue;
                }
                if (source.Pos != null && source.Pos.Equals(target.Pos)) continue;
                if (attempt(source, target)) { Reset(); return true; }
            }
            resumeAction = index + 1; resumePair = 0;
            return false;
        }
        private static (string Code, Vintagestory.API.MathTools.BlockPos Pos)[] Layout(IReadOnlyList<ICargoHold> holds)
        {
            var result = new (string Code, Vintagestory.API.MathTools.BlockPos Pos)[holds.Count];
            for (int i = 0; i < result.Length; i++) result[i] = (holds[i]?.Code, holds[i]?.Pos?.Copy());
            return result;
        }
        private static bool SameLayout(IReadOnlyList<ICargoHold> holds, (string Code, Vintagestory.API.MathTools.BlockPos Pos)[] layout)
        {
            if (layout == null || holds.Count != layout.Length) return false;
            for (int i = 0; i < layout.Length; i++)
                if (holds[i]?.Code != layout[i].Code || !Equals(holds[i]?.Pos, layout[i].Pos)) return false;
            return true;
        }
    }
}
