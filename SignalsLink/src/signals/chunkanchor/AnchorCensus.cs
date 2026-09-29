using System;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

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
        /// <summary>An active block - one the server works on. The unit everything else is measured against.</summary>
        public const int ActiveBlockWeight = 1;

        /// <summary>
        /// Is this block one the server works on? A block entity with a tick listener of its own.
        ///
        /// Having a block entity is not the test. Measured on a small station: 950 of them, of
        /// which 730 were chiselled blocks and 112 piles on the ground - shapes and stacks that
        /// never tick - and 28 were the machines the anchor was there for. Asked of the instance,
        /// so it holds for every mod's blocks and for listeners registered in a base class.
        /// </summary>
        public static bool IsActive(BlockEntity entity)
        {
            if (entity == null) return false;

            // Never, whatever they do: a burning pile ticks, and a wall of them must not start to cost.
            if (entity is BlockEntityMicroBlock || entity is BlockEntityGroundStorage) return false;

            return HasTickListener(entity);
        }

        private static readonly FieldInfo TickHandlers =
            typeof(BlockEntity).GetField("TickHandlers", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private static bool HasTickListener(BlockEntity entity)
        {
            // Should the game ever rename it, everything counts again rather than nothing.
            if (TickHandlers == null) return true;

            return TickHandlers.GetValue(entity) is System.Collections.ICollection listeners && listeners.Count > 0;
        }

        /// <summary>
        /// Is this creature somebody's animal? Born in captivity, or a tamed elk.
        ///
        /// The game itself tells a domestic animal by its generation: offspring get one more than
        /// their parents, and what spawned in the wild has none. Monsters in the caves under a
        /// station and deer crossing it were being charged at ten blocks each.
        /// </summary>
        public static bool IsLivestock(AssetLocation code, int generation)
        {
            if (generation >= 1) return true;

            return code != null && code.Domain == "game" && code.Path.StartsWith("tameddeer", StringComparison.Ordinal);
        }

        public static bool IsLivestock(Entity entity)
        {
            return entity is EntityAgent agent && entity is not EntityPlayer && agent.Alive
                && IsLivestock(entity.Code, entity.WatchedAttributes?.GetInt("generation", 0) ?? 0);
        }

        /// <summary>
        /// What a held column costs before anything is standing in it.
        ///
        /// Without this an empty column is free, and two hundred of them are free as well - which
        /// is not true. A loaded chunk costs memory, it gets written on every save, and it sits in
        /// the set the server walks. Ten units means twenty-five columns of bare ground come to
        /// the reference load: noticeable, and nowhere near what a built-up one costs.
        ///
        /// Five, not ten: the point is that bare ground is not FREE, not that it is expensive. Fifty
        /// columns of nothing then come to the reference load, which is about what a player who
        /// grabbed the whole window deserves to pay.
        ///
        /// It is NOT a charge for growing things. Crops are counted already - every tilled block is
        /// a block entity - and wild plants do not grow in an anchored chunk at all, because random
        /// block ticks only happen within BlockTickChunkRange of a player.
        /// </summary>
        public const int ColumnWeight = 5;

        /// <summary>
        /// Fallbacks, used where no server settings are to hand - in tests, and if the config
        /// system has not come up. The live values are in <see cref="SignalsLinkConfig"/>, because
        /// the right ones depend on how heavily built the server's world is.
        ///
        /// These were calibrated against a measurement, not guessed: one chunk column of a
        /// built-up castle came to about 1500 active blocks.
        /// </summary>
        public const int CreatureWeight = 2;

        public const float ReferenceLoad = 250f;

        /// <summary>
        /// Must stay above 1, or a home base costs merely proportionally more than a workshop
        /// rather than unreasonably more - which is the whole reason any of this exists.
        /// </summary>
        public const float Exponent = 1.174f;

        // K34: a solitary empty anchor is free; extra empty columns still have their normal cost.
        public static float AnchorUnits(int blocks, int creatures, int columns,
            int creatureWeight = CreatureWeight, int columnWeight = ColumnWeight)
            => columns == 1 && blocks == 0 && creatures == 0 ? 0 : Units(blocks, creatures, columns, creatureWeight, columnWeight);

        /// <summary>Weighted total of what is held.</summary>
        public static float Units(int activeBlocks, int creatures, int columns = 0,
            int creatureWeight = CreatureWeight, int columnWeight = ColumnWeight)
        {
            if (activeBlocks < 0) activeBlocks = 0;
            if (creatures < 0) creatures = 0;
            if (columns < 0) columns = 0;

            return activeBlocks * ActiveBlockWeight
                + creatures * creatureWeight
                + columns * columnWeight;
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
