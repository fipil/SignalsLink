using ProtoBuf;
using signals.src.signalNetwork;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SignalsLink.src.signals.link
{
    /// <summary>
    /// Two-phase placement of a link — a hose or a sleeve, whichever line item the player holds.
    /// Mirror of the Signals <c>PlacingWiresMod</c>. The client only proposes a connection; the
    /// <b>authoritative validation (kind, length, anchor occupancy) runs server-side</b> in
    /// <c>LinkNetworkMod.TryToAddConnection</c>.
    ///
    /// The ingame error messages come in one set per kind (ingameerror-hose-* / -sleeve-*), so a
    /// sleeve never complains about a hose.
    /// </summary>
    public class PlacingLinksMod : ModSystem
    {
        public const string ChannelName = "placinglinks";

        ICoreClientAPI capi;
        ICoreAPI api;
        IServerNetworkChannel serverChannel;
        IClientNetworkChannel clientChannel;

        LinkNetworkMod linkMod;

        NodePos pendingNode = null;
        PendingLinkRenderer pendingRenderer;

        public override bool ShouldLoad(EnumAppSide forSide) => true;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            this.api = api;
            this.linkMod = api.ModLoader.GetModSystem<LinkNetworkMod>();

            if (api.World is IClientWorldAccessor)
            {
                clientChannel = ((ICoreClientAPI)api).Network.RegisterChannel(ChannelName)
                    .RegisterMessageType(typeof(AddLinkConnectionPacket));
            }
            else
            {
                serverChannel = ((ICoreServerAPI)api).Network.RegisterChannel(ChannelName)
                    .RegisterMessageType(typeof(AddLinkConnectionPacket))
                    .SetMessageHandler<AddLinkConnectionPacket>(OnAddConnectionFromClient);
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
        public bool ConnectLink(NodePos pos, IPlayer byPlayer, ILinkAnchor anchor)
        {
            if (api.Side == EnumAppSide.Server) return false;

            // The held line decides the kind; an anchor of the other kind is not ours to click, so
            // fall through and let the block run its own interaction.
            int kind = GetHeldLinkKind(byPlayer);
            if (kind < 0 || anchor.AcceptedLinkKind != kind) return false;

            // Anchor already has a link -> refuse immediately (max 1 link per anchor). This
            // mirrors the authoritative server check but gives instant feedback, and prevents
            // even starting a second link on an occupied anchor. Client data is synced.
            if (linkMod != null && !anchor.AllowsMultipleLinks(pos) && linkMod.IsAnchorOccupied(pos))
            {
                capi?.TriggerIngameError(this, "linkoccupied", Lang.Get(ErrorKey(kind, "anchor-occupied")));
                return false;
            }

            if (pendingNode == null)
            {
                pendingNode = pos;
                Vec3f offset = anchor.GetLinkAnchorPosInBlock(pos);
                pendingRenderer?.Dispose();
                pendingRenderer = new PendingLinkRenderer(capi, this, pos.blockPos, offset, (byte)kind);
                capi?.Logger.Debug("Link pending {0}:{1}", pos.blockPos, pos.index);
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
                    capi?.TriggerIngameError(this, "linksameblock", Lang.Get(ErrorKey(kind, "same-block")));
                    return false;
                }

                LinkConnection connection = new LinkConnection(pendingNode, pos, (byte)kind);
                clientChannel.SendPacket(new AddLinkConnectionPacket { connection = connection, byPlayer = byPlayer.PlayerUID });
                ClearPending();
            }
            return true;
        }

        private void OnAddConnectionFromClient(IServerPlayer fromPlayer, AddLinkConnectionPacket msg)
        {
            LinkConnection connection = msg.connection;
            if (connection == null) return;

            // Never trust the client: verify the player holds that very kind of line and that both
            // ends are link anchors. The kind itself is re-validated in TryToAddConnection.
            if (!UseLink(fromPlayer, connection.kind, false)) return;
            if (!AnchorExists(connection.pos1) || !AnchorExists(connection.pos2)) return;

            LinkNetworkMod.AddResult result = linkMod.TryToAddConnection(connection);
            if (result == LinkNetworkMod.AddResult.Added)
            {
                UseLink(fromPlayer, connection.kind, true);
            }
            else if (result == LinkNetworkMod.AddResult.Blocked)
            {
                ((ICoreServerAPI)api).SendIngameError(fromPlayer, "linkblocked",
                    Lang.Get("signalslink:ingameerror-sleeve-blocked"));
            }
            else if (result == LinkNetworkMod.AddResult.TooLong)
            {
                ((ICoreServerAPI)api).SendIngameError(fromPlayer, "linktoolong",
                    Lang.Get(ErrorKey(connection.kind, "too-long"), LinkNetworkMod.MaxLinkLength));
            }
            // Other outcomes do not consume the line item.
        }

        private bool AnchorExists(NodePos pos)
        {
            if (pos?.blockPos == null) return false;
            ILinkAnchor anchor = api.World.BlockAccessor.GetBlock(pos.blockPos) as ILinkAnchor;
            if (anchor == null) return false;
            return anchor.CanAttachLink(api.World, pos);
        }

        /// <summary>Ingame error key for the given kind, so a sleeve never complains about a hose.</summary>
        private static string ErrorKey(int kind, string suffix)
        {
            return "signalslink:ingameerror-" + (kind == LinkKind.Sleeve ? "sleeve" : "hose") + "-" + suffix;
        }

        /// <summary>Kind of line the player holds in the right hand, or -1 if it is not a line.</summary>
        public int GetHeldLinkKind(IPlayer player)
        {
            Item item = player?.Entity?.RightHandItemSlot?.Itemstack?.Item;
            return LinkNetworkMod.KindForItemCode(item?.Code?.ToString());
        }

        public bool UseLink(IPlayer player, byte kind, bool doUse = false)
        {
            ItemStack itemStack = player?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
            if (LinkNetworkMod.KindForItemCode(itemStack?.Item?.Code?.ToString()) != kind) return false;
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
    public class AddLinkConnectionPacket
    {
        public LinkConnection connection;
        public string byPlayer;

        public AddLinkConnectionPacket() { }
    }
}
