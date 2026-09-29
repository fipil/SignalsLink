using System;
using System.Reflection;
using Newtonsoft.Json;
using Vintagestory.API.Common;

namespace SignalsLink.src
{
    /// <summary>
    /// Server settings an admin may want to change without rebuilding the mod.
    ///
    /// Only things whose right value depends on the SERVER belong here - how heavily built the
    /// world is, how generous the admin wants to be. Everything a player can see and reason about
    /// stays in the block's own attributes, where it travels with the block.
    ///
    /// Every setting is preceded by a <c>-description</c> key carrying its explanation. JSON has no
    /// comments, and the one that Newtonsoft tolerates on the way IN would be stripped the moment
    /// the file was written back out - a description that is itself a setting survives the round
    /// trip. They are in English because a config file is read by whoever runs the server.
    /// </summary>
    public class SignalsLinkConfig
    {
        [JsonProperty("AnchorReferenceLoad-description")]
        public string AnchorReferenceLoadDescription =
            "CHUNK ANCHOR. How much an anchor may keep alive before it costs the base rate of one "
            + "temporal gear per 100 in-game days. An anchor is billed for what it HOLDS - active "
            + "blocks and creatures in its columns - not for how busy that ground is. Raise this to "
            + "make anchors cheaper everywhere, lower it to make them dearer. Measured, not guessed: "
            + "one chunk column of a built-up castle came to about 1500 active blocks, so 250 sits "
            + "near a modest remote workshop.";

        public float AnchorReferenceLoad = 250f;

        [JsonProperty("AnchorPriceExponent-description")]
        public string AnchorPriceExponentDescription =
            "How sharply the price climbs above the reference load. Must stay above 1: at 1 a home "
            + "base costs merely proportionally more than a workshop, above 1 it costs unreasonably "
            + "more, which is the whole reason anchors are priced at all. With the reference load "
            + "above, 1.174 gives roughly 80 days per gear at 300 units, 12 days at 1500, 2 days at "
            + "7000.";

        public float AnchorPriceExponent = 1.174f;

        [JsonProperty("AnchorCreatureWeight-description")]
        public string AnchorCreatureWeightDescription =
            "What one domestic animal counts for against one active block. Only animals born in "
            + "captivity and tamed elk count. The game runs an animal's AI and physics only within "
            + "128 blocks of a player, so an animal held by an anchor alone mostly sleeps and costs "
            + "little. Turn AnchorReferenceLoad, not this, when bases come out too cheap or dear.";

        public int AnchorCreatureWeight = 2;

        [JsonProperty("AnchorColumnWeight-description")]
        public string AnchorColumnWeightDescription =
            "What one held chunk column costs before anything stands in it. Without this, empty "
            + "ground is free and a player can take the whole map window for nothing - and a loaded "
            + "chunk still costs memory and is written on every save. At 5, fifty columns of bare "
            + "ground come to the reference load. Set to 0 to charge only for contents.";

        public int AnchorColumnWeight = 5;

        [JsonProperty("AnchorMaxColumns-description")]
        public string AnchorMaxColumnsDescription =
            "The most chunk columns one anchor may hold. The price is the real brake; this is the "
            + "number a server owner can point at. The map offers a 15x15 window, so 225 is as high "
            + "as this can usefully go. 64 is a square of eight - a generous factory with room for "
            + "the track that serves it. Set to 0 to remove the ceiling and leave only the price.";

        public int AnchorMaxColumns = 64;

        [JsonProperty("AnchorApproachBlocks-description")]
        public string AnchorApproachBlocksDescription =
            "How near a vehicle heading for an anchor's ground has to be before a sleeping anchor "
            + "set to wake for it does so - in blocks, measured flat. Only used when another mod "
            + "reports approaching vehicles (the Signals Link YTT bridge does). Big enough for the "
            + "chunks to load before the vehicle arrives, small enough not to pay for the whole "
            + "journey: 160 is about a chunk column ahead at a fast train's speed.";

        public int AnchorApproachBlocks = 160;

        [JsonProperty("AnchorsEnabled-description")]
        public string AnchorsEnabledDescription =
            "Set to false to switch chunk anchors off on this server altogether. Placed anchors "
            + "then hold nothing and cost nothing, and tell the player the server owner has "
            + "switched them off. Their settings are kept, so setting this back to true brings "
            + "every anchor back as it was.";

        public bool AnchorsEnabled = true;

        [JsonProperty("AnchorAllowWithoutTrainBridge-description")]
        public string AnchorAllowWithoutTrainBridgeDescription =
            "Chunk anchors switch themselves off when Yang's Transport Tycoon is installed without "
            + "the Signals Link YTT bridge: an anchor can leave a train half loaded, and that train "
            + "then gets stuck until the server restarts. The bridge keeps trains whole. Set to true "
            + "only if no anchor on this server stands anywhere near a track - at your own risk.";

        public bool AnchorAllowWithoutTrainBridge = false;

        public bool Validate()
        {
            bool valid = true;
            if (!float.IsFinite(AnchorReferenceLoad) || AnchorReferenceLoad <= 0) { AnchorReferenceLoad = 250; valid = false; }
            if (!float.IsFinite(AnchorPriceExponent) || AnchorPriceExponent <= 1) { AnchorPriceExponent = 1.174f; valid = false; }
            if (AnchorCreatureWeight < 0) { AnchorCreatureWeight = 2; valid = false; }
            if (AnchorColumnWeight < 0) { AnchorColumnWeight = 5; valid = false; }
            if (AnchorMaxColumns < 0) { AnchorMaxColumns = 64; valid = false; }
            if (AnchorApproachBlocks < 0) { AnchorApproachBlocks = 160; valid = false; }
            return valid;
        }
    }

    /// <summary>Loads the settings, and writes the file back out so it can be found and read.</summary>
    public class SignalsLinkConfigLoader : ModSystem
    {
        public const string FileName = "signalslink.json";

        /// <summary>Never null after Start: a missing or broken file falls back to the defaults.</summary>
        public static SignalsLinkConfig Current { get; private set; } = new SignalsLinkConfig();

        public override double ExecuteOrder() => 0.001;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            // Clients receive the authoritative values in each anchor's attributes.
            if (api.Side != EnumAppSide.Server) return;

            try
            {
                Current = api.LoadModConfig<SignalsLinkConfig>(FileName) ?? new SignalsLinkConfig();
            }
            catch (Exception e)
            {
                // A typo in the file must not stop the mod loading; it falls back and says so.
                api.Logger.Error("[SignalsLink] " + FileName + " could not be read (" + e.Message
                    + "). Using the built-in settings.");

                Current = new SignalsLinkConfig();
            }

            if (!Current.Validate()) api.Logger.Warning("[SignalsLink] Invalid anchor settings replaced by defaults.");
            RefreshDescriptions(Current);

            // Written back every start, so a setting added by a newer build turns up in the file
            // instead of only existing in the code.
            try { api.StoreModConfig(Current, FileName); }
            catch (Exception e) { api.Logger.Warning("[SignalsLink] Could not write config: " + e.Message); }
        }

        /// <summary>
        /// Puts the current wording back over whatever the file had.
        ///
        /// Otherwise a file written by an older build keeps its old explanations for ever - and an
        /// explanation that no longer matches what the setting does is worse than none. Every string
        /// field here IS a description; nothing else is one.
        /// </summary>
        private static void RefreshDescriptions(SignalsLinkConfig config)
        {
            SignalsLinkConfig fresh = new SignalsLinkConfig();

            foreach (FieldInfo field in typeof(SignalsLinkConfig)
                .GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.FieldType == typeof(string)) field.SetValue(config, field.GetValue(fresh));
            }
        }
    }
}
