using signals.src.signalNetwork;

namespace SignalsLink.src.signals.hose
{
    /// <summary>
    /// Intake — a passive liquid source. Can be placed anywhere; if there is no water/liquid at
    /// its position or a neighbour, it simply pulls nothing (the transfer resolves no source).
    /// This lets a player place it first and flood it later (e.g. via a hatch). One hose anchor on
    /// top; the liquid is pulled out of it by an active Valve — it moves nothing itself.
    /// </summary>
    public class BlockHoseIntake : BlockHoseAnchorBase
    {
        // The intake is a source that can feed many targets — its anchor accepts multiple hoses.
        public override bool AllowsMultipleHoses(NodePos pos) => true;
    }
}
