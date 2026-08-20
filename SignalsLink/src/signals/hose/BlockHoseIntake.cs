using signals.src.signalNetwork;

namespace SignalsLink.src.signals.hose
{
    /// <summary>
    /// Intake — a passive liquid source (fresh/salt water) that stands in water. One hose
    /// anchor on top. The liquid is pulled out of it by an active Valve; it moves nothing itself.
    /// TODO(step 5): validate "must stand in water" on placement + world-water source via
    /// LiquidTransferService.
    /// </summary>
    public class BlockHoseIntake : BlockHoseAnchorBase
    {
        // The intake is a source that can feed many targets — its anchor accepts multiple hoses.
        public override bool AllowsMultipleHoses(NodePos pos) => true;
    }
}
