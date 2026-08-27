using ProtoBuf;
using signals.src.signalNetwork;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SignalsLink.src.signals.hose
{
    /// <summary>
    /// Two-phase hose placement (the <c>signalslink:hose</c> item is held). Mirror of the
    /// Signals <c>PlacingWiresMod</c>. The client only proposes a connection; the
    /// <b>authoritative validation (length, anchor occupancy) runs server-side</b> in
    /// <c>HoseNetworkMod.TryToAddConnection</c>.
    /// </summary>
    public class PlacingHosesMod : ModSystem
    {
        public const string ChannelName = "placinghoses";

        ICoreClientAPI capi;
        ICoreAPI api;
        IServerNetworkChannel serverChannel;
        IClientNetworkChannel clientChannel;

        HoseNetworkMod hoseMod;

        NodePos pendingNode = null;
        PendingHoseRenderer pendingRenderer;

        public override bool ShouldLoad(EnumAppSide forSide) => true;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            this.api = api;
            this.hoseMod = api.ModLoader.GetModSystem<HoseNetworkMod>();

            if (api.World is IClientWorldAccessor)
            {
                clientChannel = ((ICoreClientAPI)api).Network.RegisterChannel(ChannelName)
                    .RegisterMessageType(typeof(AddHoseConnectionPacket));
            }
            else
            {
                serverChannel = ((ICoreServerAPI)api).Network.RegisterChannel(ChannelName)
                    .RegisterMessageType(typeof(AddHoseConnectionPacket))
                    .SetMessageHandler<AddHoseConnectionPacket>(OnAddConnectionFromClient);
            }
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            capi = api;
            capi.Event.AfterActiveSlotChanged += OnActiveSlotChanged;
        }

        public NodePos GetPendingNode() => pendingNode;

        /// <summary>
        /// Client-side placement: first click = pending anchor, second click = send the proposed connection.
        /// </summary>
        public bool ConnectHose(NodePos pos, IPlayer byPlayer, IHoseAnchor anchor)
        {
            if (api.Side == EnumAppSide.Server) return false;
            if (!IsHoldingHose(byPlayer)) return false;

            // Anchor already has a hose -> refuse immediately (max 1 hose per anchor). This
            // mirrors the authoritative server check but gives instant feedback, and prevents
            // even starting a second hose on an occupied anchor. Client data is synced.
            if (hoseMod != null && !anchor.AllowsMultipleHoses(pos) && hoseMod.IsAnchorOccupied(pos))
            {
                capi?.TriggerIngameError(this, "hoseoccupied", Lang.Get("signalslink:ingameerror-hose-anchor-occupied"));
                return false;
            }

            if (pendingNode == null)
            {
                pendingNode = pos;
                Vec3f offset = anchor.GetHoseAnchorPosInBlock(pos);
                pendingRenderer?.Dispose();
                pendingRenderer = new PendingHoseRenderer(capi, this, pos.blockPos, offset);
                capi?.Logger.Debug("Hose pending {0}:{1}", pos.blockPos, pos.index);
            }
            else
            {
                if (pendingNode == pos)
                {
                    // Clicking the same anchor cancels the pending placement.
                    ClearPending();
                    return true;
                }

                // Can't join two anchors of the same block (e.g. a coupling to itself). Keep the
                // pending anchor so the player can still pick a different second anchor.
                if (pendingNode.blockPos.Equals(pos.blockPos))
                {
                    capi?.TriggerIngameError(this, "hosesameblock", Lang.Get("signalslink:ingameerror-hose-same-block"));
                    return false;
                }

                HoseConnection connection = new HoseConnection(pendingNode, pos);
                clientChannel.SendPacket(new AddHoseConnectionPacket { connection = connection, byPlayer = byPlayer.PlayerUID });
                ClearPending();
            }
            return true;
        }

        private void OnAddConnectionFromClient(IServerPlayer fromPlayer, AddHoseConnectionPacket msg)
        {
            HoseConnection connection = msg.connection;
            if (connection == null) return;

            // Never trust the client: verify the player is holding a hose and that both ends are hose anchors.
            if (!UseHose(fromPlayer, false)) return;
            if (!AnchorExists(connection.pos1) || !AnchorExists(connection.pos2)) return;

            HoseNetworkMod.AddResult result = hoseMod.TryToAddConnection(connection);
            if (result == HoseNetworkMod.AddResult.Added)
            {
                UseHose(fromPlayer, true);
            }
            else if (result == HoseNetworkMod.AddResult.TooLong)
            {
                ((ICoreServerAPI)api).SendIngameError(fromPlayer, "hosetoolong",
                    Lang.Get("signalslink:ingameerror-hose-too-long", HoseNetworkMod.MaxHoseLength));
            }
            // Other outcomes do not consume the hose.
        }

        private bool AnchorExists(NodePos pos)
        {
            if (pos?.blockPos == null) return false;
            IHoseAnchor anchor = api.World.BlockAccessor.GetBlock(pos.blockPos) as IHoseAnchor;
            if (anchor == null) return false;
            return anchor.CanAttachHose(api.World, pos);
        }

        public bool IsHoldingHose(IPlayer player)
        {
            Item item = player?.Entity?.RightHandItemSlot?.Itemstack?.Item;
            return item?.Code?.ToString() == HoseNetworkMod.HoseItemCode;
        }

        public bool UseHose(IPlayer player, bool doUse = false)
        {
            ItemStack itemStack = player?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
            if (itemStack?.Item?.Code?.ToString() != HoseNetworkMod.HoseItemCode) return false;
            if (player.WorldData.CurrentGameMode == EnumGameMode.Creative) return true;

            if (doUse)
            {
                player.InventoryManager.ActiveHotbarSlot.TakeOut(1);
                player.InventoryManager.ActiveHotbarSlot.MarkDirty();
            }
            return true;
        }

        private void OnActiveSlotChanged(ActiveSlotChangeEventArgs slotChange)
        {
            ClearPending();
        }

        private void ClearPending()
        {
            pendingNode = null;
            pendingRenderer?.Dispose();
            pendingRenderer = null;
        }
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class AddHoseConnectionPacket
    {
        public HoseConnection connection;
        public string byPlayer;

        public AddHoseConnectionPacket() { }
    }
}
