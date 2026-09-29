using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace SignalsLink.YTT.src.train
{
    /// <summary>
    /// The kinds of vehicle a header may name: <c>load train fridge</c>.
    ///
    /// The other mod lets nobody name a wagon, and a number stops being true the day the train is
    /// coupled differently - so firewood ended up in the refrigerated wagon because it stood
    /// nearer. A kind is read off the entity's code, which needs nothing of the other mod's insides
    /// and survives whatever it does to them.
    /// </summary>
    public static class TrainVehicleKind
    {
        public const string Boxcar = "boxcar";
        public const string Fridge = "fridge";
        public const string Cart = "cart";

        /// <summary>The kind a header word names, or null when it names none.</summary>
        public static string FromToken(string token)
        {
            switch ((token ?? "").ToLowerInvariant())
            {
                case "boxcar": return Boxcar;
                case "fridge":
                case "reefer": return Fridge;
                case "cart":
                case "minecart": return Cart;
                default: return null;
            }
        }

        /// <summary>No kind asked for matches everything.</summary>
        public static bool Matches(string kind, AssetLocation code)
        {
            if (kind == null) return true;
            if (code == null || code.Domain != TrainCargoHolderFinder.Domain) return false;

            string path = code.Path ?? "";

            return kind switch
            {
                // Both the plain boxcar and the early one.
                Boxcar => path.Contains("boxcar", StringComparison.Ordinal),
                Fridge => path.Contains("fridge", StringComparison.Ordinal),
                Cart => path.StartsWith("minecart", StringComparison.Ordinal),
                _ => false,
            };
        }

        /// <summary>
        /// Where a vehicle rides in its convoy, the head being 1. The other mod numbers every
        /// member from zero, and that is used when it is there. Without it the place is guessed
        /// from the distance behind the head - which only ever fitted mine carts, 1.2 blocks
        /// apart, and put the second boxcar at eight.
        /// </summary>
        public static int PlaceInConvoy(int? convoyIndex, double distanceBehindHead)
        {
            if (convoyIndex != null && convoyIndex.Value >= 0) return convoyIndex.Value + 1;

            return 1 + (int)Math.Round(Math.Max(0, distanceBehindHead) / 1.2);
        }

        /// <summary>
        /// Which one of its kind a vehicle is, counted from the head: <c>fridge 2</c> is the second
        /// refrigerated wagon, wherever in the train it rides. Given its place in the convoy and
        /// the place of every vehicle of its kind in the same convoy (itself included or not).
        /// </summary>
        public static int RankAmong(double mine, IEnumerable<double> sameKind)
        {
            int ahead = 0;

            foreach (double other in sameKind ?? Array.Empty<double>())
            {
                // Half a block: two vehicles are never that close, and a float is never exact.
                if (other < mine - 0.5) ahead++;
            }

            return ahead + 1;
        }
    }
}
