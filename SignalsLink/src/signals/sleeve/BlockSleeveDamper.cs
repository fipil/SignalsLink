using SignalsLink.src.signals.link;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.sleeve
{
    /// <summary>
    /// Managed Damper — the ManagedSleeve endpoint, the counterpart of the Hose Valve. Same anchor
    /// layout (Signals Input 0, Signals Output 1, sleeve anchor 2) and the same mount variants;
    /// the cargo is items and blocks instead of liquid.
    /// </summary>
    public class BlockSleeveDamper : BlockLinkEndpointBase
    {
        protected override AssetLocation CanonicalCode => new AssetLocation("signalslink", "sleevedamper-north-down");

        protected override string AnchorLangKey => "signalslink:con-sleeve";

        public override byte AcceptedLinkKind => LinkKind.Sleeve;

        // Only the ceiling variant is a full collar and therefore a room boundary; the others are
        // small open frames that must not answer for a wall they do not form. Cached because
        // GetRetention is called from room detection, possibly off the main thread.
        private bool sealsRoom;
        private RetentionModeRegistry retentionModes;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            sealsRoom = Code?.FirstCodePart() == "sleevedamperroof";
            retentionModes = api.ModLoader.GetModSystem<RetentionModeRegistry>();
        }

        /// <summary>
        /// A sleeve passes through the ceiling and the room below should still be able to hold its
        /// climate. Which climate is up to the player: see <see cref="RetentionMode"/>.
        /// </summary>
        public override int GetRetention(BlockPos pos, BlockFacing facing, EnumRetentionType type)
        {
            if (!sealsRoom) return base.GetRetention(pos, facing, type);

            return RetentionMode.RetentionValue(retentionModes?.Get(pos) ?? RetentionMode.Cooling);
        }
    }
}
