using System;

namespace SignalsLink.src.signals.chunkanchor
{
    /// <summary>
    /// What an anchor is keeping alive, and what that costs.
    ///
    /// The anchor is not charged for ACTIVITY. Measuring activity was considered and dropped: the
    /// only flag the game offers is "needs resending to the client", which is not the same as "did
    /// work"; reading it at all would mean reflecting into the server; and worst of all it would
    /// price the wrong thing, because a player who pays per action builds a factory that idles.
    ///
    /// So the anchor pays for what it HOLDS. Its one job is keeping things loaded, so the bill is
    /// what is loaded. That can be dodged too - by moving the animals out and building sparsely -
    /// but dodging it means not having a base there, which is the outcome we wanted anyway.
    /// </summary>
    public static class AnchorCensus
    {
        /// <summary>An active block - anything with state of its own. The unit everything else is measured against.</summary>
        public const int ActiveBlockWeight = 1;

        /// <summary>
        /// Fallbacks, used where no server settings are to hand - in tests, and if the config
        /// system has not come up. The live values are in <see cref="SignalsLinkConfig"/>, because
        /// the right ones depend on how heavily built the server's world is.
        ///
        /// These were calibrated against a measurement, not guessed: one chunk column of a
        /// built-up castle came to about 1500 active blocks.
        /// </summary>
        public const int CreatureWeight = 10;

        public const float ReferenceLoad = 250f;

        /// <summary>
        /// Must stay above 1, or a home base costs merely proportionally more than a workshop
        /// rather than unreasonably more - which is the whole reason any of this exists.
        /// </summary>
        public const float Exponent = 1.174f;

        /// <summary>Weighted total of what is held.</summary>
        public static float Units(int activeBlocks, int creatures, int creatureWeight = CreatureWeight)
        {
            if (activeBlocks < 0) activeBlocks = 0;
            if (creatures < 0) creatures = 0;

            return activeBlocks * ActiveBlockWeight + creatures * creatureWeight;
        }

        /// <summary>
        /// The census expressed as the "volume" the shared charge behaviour already understands, so
        /// the anchor needs no consumption arithmetic of its own: that behaviour bills
        /// volume / referenceVolume, which is exactly the ratio wanted once the curve is applied.
        /// </summary>
        public static float EffectiveVolume(float units, float referenceVolume,
            float referenceLoad = ReferenceLoad, float exponent = Exponent)
        {
            if (referenceVolume <= 0 || referenceLoad <= 0) return 0f;
            if (units <= 0) return 0f;

            return referenceVolume * (float)Math.Pow(units / referenceLoad, exponent);
        }

        /// <summary>
        /// How long one gear lasts at this load, in in-game days. What the dialog quotes, and the
        /// only form of the price a player can act on.
        /// </summary>
        public static double DaysPerGear(float units, float gearTotalCharge, float referenceVolume,
            float baseConsumptionFactor, float referenceLoad = ReferenceLoad, float exponent = Exponent)
        {
            float volume = EffectiveVolume(units, referenceVolume, referenceLoad, exponent);
            if (volume <= 0 || gearTotalCharge <= 0 || baseConsumptionFactor <= 0) return double.PositiveInfinity;

            // Mirrors the shared behaviour: at volume == referenceVolume one gear lasts 100 days.
            double perHour = gearTotalCharge / (100.0 * 24.0) * (volume / referenceVolume) * baseConsumptionFactor;
            if (perHour <= 0) return double.PositiveInfinity;

            return gearTotalCharge / perHour / 24.0;
        }
    }
}
