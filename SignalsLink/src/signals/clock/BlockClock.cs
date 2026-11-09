using signals.src.signalNetwork;

namespace SignalsLink.src.signals.clock;

/// <summary>
/// The clock: a glass bulb on a cast-iron base with three output pins (0 = Phase, 1 = Turn, 2 = Beat) that carry the
/// game time, see <see cref="ClockTime"/>. Wires attach through the Signals <c>BlockConnection</c>; the block
/// itself has no state, no orientation and nothing to click.
/// </summary>
public class BlockClock : BlockConnection
{
}
