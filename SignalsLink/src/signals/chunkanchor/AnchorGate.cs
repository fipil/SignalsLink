namespace SignalsLink.src.signals.chunkanchor
{
    /// <summary>
    /// Whether chunk anchors may work on this server at all, and if not, why.
    ///
    /// Two reasons. The owner may simply not want them. And a train mod without its bridge: an
    /// anchor keeps a permanent edge between loaded and unloaded ground, a train standing across
    /// it is left half loaded, and that train mod cannot recover from it while the server runs.
    /// The bridge is what keeps a train whole; without it the anchor is part of the problem.
    /// </summary>
    public static class AnchorGate
    {
        public const string Open = "";
        public const string ByAdmin = "admin";
        public const string MissingTrainBridge = "trainbridge";

        /// <summary>Mod ids only - this mod knows nothing else about either.</summary>
        public const string TrainMod = "yangtransport";
        public const string TrainBridge = "signalslinkytt";

        public static string Reason(bool enabledInConfig, bool trainMod, bool trainBridge, bool allowWithoutBridge)
        {
            // The owner's word first: it is the one a player can do nothing about but ask.
            if (!enabledInConfig) return ByAdmin;
            if (trainMod && !trainBridge && !allowWithoutBridge) return MissingTrainBridge;

            return Open;
        }

        /// <summary>What to tell the player, or null when there is nothing to tell.</summary>
        public static string LangKey(string reason)
        {
            return reason switch
            {
                ByAdmin => "signalslink:chunkanchor-disabled-admin",
                MissingTrainBridge => "signalslink:chunkanchor-disabled-trainbridge",
                _ => null
            };
        }
    }
}
