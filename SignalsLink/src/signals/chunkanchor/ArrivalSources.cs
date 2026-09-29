using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.chunkanchor
{
    /// <summary>
    /// Something that can say a vehicle is on its way somewhere - a train mod watching its own
    /// simulation. The anchor knows only that such a thing exists and what to call it.
    /// </summary>
    public interface IArrivalSource
    {
        string Keyword { get; }

        /// <summary>The label of the anchor's "wake when ... is coming" switch, as a lang key.</summary>
        string LangKey { get; }
    }

    /// <summary>
    /// Where arrival sources sign in and where they report. A registry rather than a reference,
    /// because the only source today lives in another mod - and with none signed in, the anchor
    /// dialog shows no switch at all.
    /// </summary>
    public class ArrivalSourceRegistry : ModSystem
    {
        private readonly List<IArrivalSource> sources = new List<IArrivalSource>();

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public IReadOnlyList<IArrivalSource> Sources => sources;

        /// <summary>A vehicle at the first position is heading for the second.</summary>
        public event Action<Vec3d, BlockPos> Approaching;

        public void Register(IArrivalSource source)
        {
            if (source != null && !sources.Contains(source)) sources.Add(source);
        }

        /// <summary>
        /// Said as often as the source likes - once a second is fine. Waking is idempotent, so a
        /// repeated announcement costs nothing, and a source need not remember what it said.
        /// </summary>
        public void Announce(Vec3d vehicle, BlockPos target)
        {
            if (vehicle == null || target == null) return;

            Approaching?.Invoke(vehicle, target);
        }
    }

    /// <summary>Whether an announcement concerns a sleeping anchor. Pure, so it can be tested dry.</summary>
    public static class ApproachRule
    {
        /// <summary>
        /// The target stands on the anchor's ground or right next to it. Next to it as well, because
        /// a station block sits at the end of a platform and the platform is what the anchor holds.
        /// </summary>
        public static bool Concerns(IReadOnlyCollection<long> columns, long targetColumn)
        {
            if (columns == null || columns.Count == 0) return false;

            (int x, int z) = AnchorArea.Of(targetColumn);

            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (columns.Contains(AnchorArea.Key(x + dx, z + dz))) return true;
            }

            return false;
        }

        /// <summary>Close enough to be worth loading the ground for, measured flat.</summary>
        public static bool Close(Vec3d vehicle, BlockPos target, double withinBlocks)
        {
            if (vehicle == null || target == null || withinBlocks <= 0) return false;

            double dx = vehicle.X - (target.X + 0.5);
            double dz = vehicle.Z - (target.Z + 0.5);

            return dx * dx + dz * dz <= withinBlocks * withinBlocks;
        }
    }
}
