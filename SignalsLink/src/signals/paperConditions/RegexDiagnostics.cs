using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.paperConditions
{
    /// <summary>Identifies the paper owner in timeout warnings without requiring # debug.</summary>
    public sealed class RegexDiagnostics : IDisposable
    {
        [ThreadStatic] private static RegexDiagnostics current;
        private readonly RegexDiagnostics previous;
        private readonly ICoreAPI api;
        private readonly BlockPos pos;
        private readonly string device;
        public static ILogger Logger => current?.api?.Side == EnumAppSide.Server ? current.api.Logger : null;
        public static string Location => current == null ? null
            : current.device + " at " + current.pos + " (dimension " + current.pos?.dimension + ")";
        private RegexDiagnostics(ICoreAPI api, BlockPos pos, string device)
        {
            previous = current; this.api = api; this.pos = pos; this.device = device; current = this;
        }
        public static RegexDiagnostics Begin(ICoreAPI api, BlockPos pos, string device) => new(api, pos, device);
        public void Dispose() { current = previous; }
    }
}
