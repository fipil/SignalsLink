namespace SignalsLink.src.signals.managedchute.transporting
{
    /// <summary>
    /// A slot that holds loose liquid measured in litres, without being one of the game's own
    /// liquid slots.
    ///
    /// The liquid service used to decide this by naming the two vanilla slot classes it knew. That
    /// is a list nobody can join: a device of ours whose slots take goods AND liquid was refused
    /// silently, because being refused looks exactly like there being nowhere to pour. A slot says
    /// for itself what it can do, and the service stays ignorant of which device it belongs to.
    /// </summary>
    public interface ILiquidHoldingSlot
    {
        /// <summary>How much liquid fits, in litres. A bucket is 10, a barrel 50.</summary>
        float CapacityLitres { get; }
    }
}
