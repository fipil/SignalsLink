namespace SignalsLink.src.signals
{
    /// <summary>
    /// A block entity that keeps an Input signal buffer (pending items/litres to transfer).
    /// Implemented by ManagedChute and the hose Valve so a wrench can clear that buffer.
    /// </summary>
    public interface ISignalBuffer
    {
        /// <summary>Clear the pending transfer buffer (and stop continuous/unlimited mode).</summary>
        void ClearBuffer();
    }
}
