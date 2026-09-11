namespace SignalsLink.src.signals.manageddock
{
    /// <summary>
    /// How often the dock runs next. What is paid for is the SEARCH, not the transfer.
    /// </summary>
    public static class DockTickRate
    {
        public const int IdleMs = 1000;     // nothing there, only looking
        public const int WaitingMs = 200;   // found something, waiting for it
        public const int WorkingMs = 50;    // moving goods

        /// <param name="searchedLive">
        /// The pass searched for something that cannot be remembered between ticks - a train.
        /// </param>
        public static int Next(bool sawHolder, bool actionPerformed, bool searchedLive)
        {
            // Never back to idle once a holder was seen: the train is still standing there.
            if (!sawHolder) return IdleMs;
            if (!actionPerformed) return WaitingMs;

            // An uncacheable holder would be searched 20x a second at the fast beat.
            return searchedLive ? WaitingMs : WorkingMs;
        }
    }
}
