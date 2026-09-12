using Vintagestory.API.Common;

namespace SignalsLink.src
{
    /// <summary>
    /// Server settings an admin may want to change without rebuilding the mod.
    ///
    /// Only things whose right value depends on the SERVER belong here - how heavily built the
    /// world is, how generous the admin wants to be. Everything a player can see and reason about
    /// stays in the block's own attributes, where it travels with the block.
    /// </summary>
    public class SignalsLinkConfig
    {
        /// <summary>
        /// What a chunk anchor may keep alive before it costs the base rate: one gear per hundred
        /// in-game days.
        ///
        /// Measured, not guessed. A single chunk column of a built-up castle came to about 1500
        /// active blocks, which is why the first attempt at 50 priced that castle at five gears a
        /// day. A remote workshop of a few hundred is what this is meant to sit near.
        /// </summary>
        public float AnchorReferenceLoad = 250f;

        /// <summary>
        /// How sharply the price climbs above that. Must stay above 1, or a home base costs merely
        /// proportionally more than a workshop instead of unreasonably more - which is the whole
        /// reason the anchor is priced at all.
        ///
        /// With the reference load above, 1.174 gives roughly: 300 units 80 days, 1500 units 12
        /// days, 7000 units 2 days.
        /// </summary>
        public float AnchorPriceExponent = 1.174f;

        /// <summary>
        /// What one creature counts for against one active block. Creatures run AI, pathfinding and
        /// physics every tick where a block entity wakes a few times a second, so ten is the low
        /// end of what they actually cost.
        /// </summary>
        public int AnchorCreatureWeight = 10;
    }

    /// <summary>Loads the settings, and writes the file out the first time so it can be found.</summary>
    public class SignalsLinkConfigLoader : ModSystem
    {
        public const string FileName = "signalslink.json";

        /// <summary>Never null after Start: a missing or broken file falls back to the defaults.</summary>
        public static SignalsLinkConfig Current { get; private set; } = new SignalsLinkConfig();

        public override double ExecuteOrder() => 0.001;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            try
            {
                Current = api.LoadModConfig<SignalsLinkConfig>(FileName) ?? new SignalsLinkConfig();
            }
            catch (System.Exception e)
            {
                // A typo in the file must not stop the mod loading; it falls back and says so.
                api.Logger.Error("[SignalsLink] " + FileName + " could not be read (" + e.Message
                    + "). Using the built-in settings.");

                Current = new SignalsLinkConfig();
            }

            api.StoreModConfig(Current, FileName);
        }
    }
}
