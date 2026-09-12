using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SignalsLink.src.signals.paperConditions
{
    /// <summary>
    /// Asks before overwriting or clearing a device's orders.
    ///
    /// It asks ONLY when something would be lost - the device already has orders and they differ
    /// from what is being written. A dialog that appears every time is clicked through blind
    /// within a day and then protects nothing.
    /// </summary>
    public class PaperConfirmMod : ModSystem
    {
        public const string ChannelName = "signalslinkpaper";

        /// <summary>How far a player may be from the device they are giving orders to.</summary>
        private const int ReachSquared = 100;

        private ICoreClientAPI capi;
        private IClientNetworkChannel clientChannel;

        public override bool ShouldLoad(EnumAppSide forSide) => true;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            if (api is ICoreClientAPI client)
            {
                clientChannel = client.Network.RegisterChannel(ChannelName)
                    .RegisterMessageType(typeof(PaperWritePacket));
            }
            else
            {
                ((ICoreServerAPI)api).Network.RegisterChannel(ChannelName)
                    .RegisterMessageType(typeof(PaperWritePacket))
                    .SetMessageHandler<PaperWritePacket>(Write);
            }
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            capi = api;
        }

        /// <summary>
        /// Is there anything to lose? False for a device with no orders, and for orders that say
        /// the same thing - rewriting a paper with its own text is not a mistake worth a dialog.
        /// </summary>
        public static bool WouldLoseSomething(string current, string wanted)
        {
            if (string.IsNullOrWhiteSpace(current)) return false;

            return !string.Equals(current.Trim(), wanted?.Trim(), System.StringComparison.Ordinal);
        }

        /// <summary>Opens the dialog. Client only; the server waits for what comes back.</summary>
        public void Ask(BlockPos pos, string current, string wanted)
        {
            if (capi == null) return;

            new GuiDialogConfirmPaper(capi, current, wanted, () => Send(pos, wanted)).TryOpen();
        }

        private void Send(BlockPos pos, string text)
        {
            clientChannel?.SendPacket(new PaperWritePacket { X = pos.X, Y = pos.Y, Z = pos.Z, Text = text });
        }

        private void Write(IServerPlayer player, PaperWritePacket packet)
        {
            BlockPos pos = new BlockPos(packet.X, packet.Y, packet.Z);

            // The client asked for this, so the client could have asked for anything: check that
            // the player is really standing at the device before it is believed.
            if (player?.Entity == null || player.Entity.Pos.SquareDistanceTo(pos.ToVec3d()) > ReachSquared) return;

            if (player.Entity.World.BlockAccessor.GetBlockEntity(pos) is not IPaperConditionsHost host) return;

            host.ConditionsText = string.IsNullOrWhiteSpace(packet.Text) ? null : packet.Text;
        }
    }
}
