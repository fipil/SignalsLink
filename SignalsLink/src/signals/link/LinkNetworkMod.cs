using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using signals.src.signalNetwork;

namespace SignalsLink.src.signals.link
{
    /// <summary>
    /// Persistence + synchronization + rendering of the link network — hoses (liquids) and
    /// sleeves (items) share one graph, told apart by <see cref="LinkConnection.kind"/>. Mirror of
    /// the Signals <c>HangingWiresMod</c>, but <b>independent of the signal network</b> (a link
    /// carries no signal, so <c>SignalNetworkMod</c> is never called). It additionally
    /// validates segment length and anchor occupancy.
    /// </summary>
    public class LinkNetworkMod : ModSystem
    {
        /// <summary>Maximum length of a single segment (anchor-to-anchor), straight-line, in blocks.</summary>
        public const double MaxLinkLength = 10.0;

        public const string ChannelName = "signalslinklinks";

        /// <summary>
        /// Save key of the whole network. It predates the hose/sleeve split and MUST NOT change -
        /// renaming it would orphan every hose in every existing world.
        /// </summary>
        public const string SaveKey = "signalslinkHoseData";

        public const string HoseItemCode = "signalslink:hose";
        public const string SleeveItemCode = "signalslink:sleeve";

        /// <summary>Maps a line item's code to its <see cref="LinkKind"/>, or -1 if it is not one.</summary>
        public static int KindForItemCode(string code)
        {
            if (code == HoseItemCode) return LinkKind.Hose;
            if (code == SleeveItemCode) return LinkKind.Sleeve;
            return -1;
        }

        public HangingLinksRenderer Renderer;

        IServerNetworkChannel serverChannel;
        IClientNetworkChannel clientChannel;

        public LinkNetworkData data = new LinkNetworkData();

        ICoreAPI api;
        ICoreServerAPI sapi;
        ICoreClientAPI capi;

        // The item handed back when a line of that kind is cut or its block is destroyed.
        readonly Item[] linkItems = new Item[LinkKind.Count];

        public override bool ShouldLoad(EnumAppSide forSide) => true;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            this.api = api;

            if (api.World is IClientWorldAccessor)
            {
                clientChannel = ((ICoreClientAPI)api).Network.RegisterChannel(ChannelName)
                    .RegisterMessageType(typeof(LinkNetworkData))
                    .SetMessageHandler<LinkNetworkData>(OnDataFromServer);
            }
            else
            {
                serverChannel = ((ICoreServerAPI)api).Network.RegisterChannel(ChannelName)
                    .RegisterMessageType(typeof(LinkNetworkData));
            }
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            capi = api;

            capi.Event.ChunkDirty += OnChunkDirty;
            capi.Event.RegisterGameTickListener(OnClientTick, 16);
            capi.Event.BlockTexturesLoaded += OnBlockTexturesLoaded;
            capi.Event.LeaveWorld += () => Renderer?.Dispose();
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            sapi = api;

            api.Event.GameWorldSave += Event_GameWorldSave;
            api.Event.SaveGameLoaded += Event_SaveGameLoaded;
            sapi.Event.PlayerNowPlaying += Event_OnPlayerJoin;

            linkItems[LinkKind.Hose] = api.World.GetItem(new AssetLocation(HoseItemCode));
            linkItems[LinkKind.Sleeve] = api.World.GetItem(new AssetLocation(SleeveItemCode));
        }

        private void OnBlockTexturesLoaded()
        {
            Renderer = new HangingLinksRenderer(capi, this);
            Renderer.RequestFullRebuild();
        }

        private void OnChunkDirty(Vec3i chunkCoord, IWorldChunk chunk, EnumChunkDirtyReason reason)
        {
            if (reason == EnumChunkDirtyReason.NewlyLoaded) Renderer?.RequestFullRebuild();
        }

        private void OnClientTick(float dt)
        {
            Renderer?.OnClientTick(dt);
        }

        private void OnDataFromServer(LinkNetworkData data)
        {
            this.data = data;
            InvalidateIndex();
            Renderer?.RequestIncrementalRebuild(data);
        }

        private void Event_GameWorldSave()
        {
            sapi.WorldManager.SaveGame.StoreData(SaveKey, SerializerUtil.Serialize(data));
        }

        private void Event_SaveGameLoaded()
        {
            byte[] blob = sapi.WorldManager.SaveGame.GetData(SaveKey);
            try
            {
                this.data = SerializerUtil.Deserialize<LinkNetworkData>(blob);
            }
            catch (Exception)
            {
                this.data = new LinkNetworkData();
            }

            InvalidateIndex();
        }

        private void Event_OnPlayerJoin(IServerPlayer player)
        {
            serverChannel.SendPacket(data, player);
        }

        #region Queries

        // Anchor -> the connections touching it. Without it every lookup was a linear scan over
        // all connections, and the valve does one scan per link per hop on every tick; with
        // multi-source valves that adds up. Rebuilt lazily: explicitly invalidated by every
        // mutation and whenever `data` is replaced, plus a connection-count backstop so a missed
        // invalidation cannot leave a permanently stale index.
        private Dictionary<NodePos, List<LinkConnection>> connectionIndex;
        private int indexedCount = -1;

        private Dictionary<NodePos, List<LinkConnection>> ConnectionIndex
        {
            get
            {
                if (connectionIndex == null || indexedCount != data.connections.Count) RebuildIndex();
                return connectionIndex;
            }
        }

        private void RebuildIndex()
        {
            Dictionary<NodePos, List<LinkConnection>> index = new Dictionary<NodePos, List<LinkConnection>>();
            foreach (LinkConnection con in data.connections)
            {
                IndexAnchor(index, con.pos1, con);
                IndexAnchor(index, con.pos2, con);
            }
            connectionIndex = index;
            indexedCount = data.connections.Count;
        }

        private static void IndexAnchor(Dictionary<NodePos, List<LinkConnection>> index, NodePos anchor, LinkConnection con)
        {
            if (anchor == null) return;
            if (!index.TryGetValue(anchor, out List<LinkConnection> list))
            {
                list = new List<LinkConnection>();
                index[anchor] = list;
            }
            list.Add(con);
        }

        /// <summary>Drops the anchor index; the next query rebuilds it.</summary>
        private void InvalidateIndex()
        {
            connectionIndex = null;
            indexedCount = -1;
        }

        /// <summary>Returns connections from the given anchor, oriented so that pos1 == the given position.</summary>
        public List<LinkConnection> GetConnectionsFrom(NodePos pos)
        {
            List<LinkConnection> output = new List<LinkConnection>();
            if (pos == null || !ConnectionIndex.TryGetValue(pos, out List<LinkConnection> touching)) return output;

            foreach (LinkConnection con in touching)
            {
                output.Add(con.pos1 == pos
                    ? new LinkConnection(con.pos1, con.pos2, con.kind)
                    : new LinkConnection(con.pos2, con.pos1, con.kind));
            }
            return output;
        }

        /// <summary>Is any link already attached to this anchor? Only meaningful for anchors that
        /// do not waive the "max 1 link" rule (see <see cref="AllowsMultiple"/>).</summary>
        public bool IsAnchorOccupied(NodePos pos)
        {
            return pos != null && ConnectionIndex.TryGetValue(pos, out List<LinkConnection> touching) && touching.Count > 0;
        }

        /// <summary>Does the block at this anchor waive the "max 1 link per anchor" rule (e.g. Intake)?</summary>
        public bool AllowsMultiple(NodePos pos)
        {
            return api?.World.BlockAccessor.GetBlock(pos.blockPos) is ILinkAnchor a && a.AllowsMultipleLinks(pos);
        }

        /// <summary>
        /// All endpoints (valve/intake) reachable from the given endpoint anchor — one per link
        /// attached to it. A valve anchor may carry several links (multi-source pumping), so this
        /// fans out over them; each individual branch then stays linear, because a coupling is
        /// strictly pass-through (2 anchors, at most 1 link each).
        ///
        /// Endpoints are deduplicated (two parallel routes to the same far anchor collapse into
        /// one logical line) and returned in a stable order — <c>data.connections</c> is a HashSet
        /// whose iteration order is not reproducible across reloads, and the caller round-robins
        /// over this list.
        /// </summary>
        public List<LinkSource> GetOtherEndpoints(IWorldAccessor world, NodePos startAnchor)
        {
            List<LinkSource> sources = new List<LinkSource>();

            foreach (LinkConnection con in GetConnectionsFrom(startAnchor))
            {
                NodePos endpoint = WalkToEndpoint(world, startAnchor, con.pos2);
                if (endpoint == null) continue;
                if (endpoint == startAnchor) continue;                        // walked back to ourselves
                if (sources.Exists(s => s.Endpoint == endpoint)) continue;    // parallel route to the same anchor
                sources.Add(new LinkSource(endpoint, con.pos2));
            }

            sources.Sort((x, y) => CompareAnchors(x.Endpoint, y.Endpoint));
            return sources;
        }

        /// <summary>
        /// Follows one branch from <paramref name="startAnchor"/> — whose first hop leads to
        /// <paramref name="firstHop"/> — through any couplings to the endpoint anchor on its far
        /// side. Returns null if the branch dangles or loops.
        /// </summary>
        private NodePos WalkToEndpoint(IWorldAccessor world, NodePos startAnchor, NodePos firstHop)
        {
            HashSet<NodePos> visited = new HashSet<NodePos> { startAnchor };
            NodePos otherAnchor = firstHop;

            while (true)
            {
                if (!visited.Add(otherAnchor)) return null; // loop guard

                ILinkAnchor block = world.BlockAccessor.GetBlock(otherAnchor.blockPos) as ILinkAnchor;
                if (block == null) return null;

                NodePos[] anchors = block.GetLinkAnchors(world, otherAnchor.blockPos);
                if (anchors.Length <= 1) return otherAnchor; // reached an endpoint (valve/intake)

                // Coupling: continue out its other anchor.
                NodePos through = null;
                foreach (NodePos a in anchors)
                {
                    if (a != otherAnchor) { through = a; break; }
                }
                if (through == null) return null;
                if (!visited.Add(through)) return null;

                // A coupling anchor never carries more than one link, so this hop is unambiguous.
                List<LinkConnection> cons = GetConnectionsFrom(through);
                if (cons.Count == 0) return null; // dangling
                otherAnchor = cons[0].pos2;
            }
        }

        /// <summary>Stable ordering of anchors (block position, then anchor index).</summary>
        public static int CompareAnchors(NodePos a, NodePos b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            int c = a.blockPos.X.CompareTo(b.blockPos.X); if (c != 0) return c;
            c = a.blockPos.Y.CompareTo(b.blockPos.Y); if (c != 0) return c;
            c = a.blockPos.Z.CompareTo(b.blockPos.Z); if (c != 0) return c;
            return a.index.CompareTo(b.index);
        }

        #endregion

        #region Mutations (server-authoritative)

        public enum AddResult { Added, Duplicate, SameAnchor, SameBlock, AnchorOccupied, TooLong, KindMismatch }

        /// <summary>Kind accepted by the anchor at this position, or -1 if there is no anchor there.</summary>
        private int GetAcceptedKind(NodePos pos)
        {
            return api?.World.BlockAccessor.GetBlock(pos.blockPos) is ILinkAnchor a ? a.AcceptedLinkKind : -1;
        }

        /// <summary>The item to hand back for a line of the given kind, or null if it is unknown.</summary>
        private Item ItemForKind(byte kind)
        {
            return kind < linkItems.Length ? linkItems[kind] : null;
        }

        /// <summary>
        /// Adds a connection with server-side validation: same anchor, same block, anchor
        /// kind, occupancy, segment length. Never calls SignalNetworkMod — a link carries no signal.
        /// </summary>
        public AddResult TryToAddConnection(LinkConnection connection)
        {
            if (connection?.pos1 == null || connection.pos2 == null) return AddResult.SameAnchor;
            if (connection.pos1 == connection.pos2) return AddResult.SameAnchor;

            // Both anchors on the same block (e.g. a coupling's two anchors) must not be joined.
            if (connection.pos1.blockPos.Equals(connection.pos2.blockPos)) return AddResult.SameBlock;

            // Both ends must be anchors of the same kind, and that kind must be the one the client
            // proposed (PlacingLinksMod derives it from the line item the player is holding).
            int kind1 = GetAcceptedKind(connection.pos1);
            int kind2 = GetAcceptedKind(connection.pos2);
            if (kind1 < 0 || kind1 != kind2 || kind1 != connection.kind) return AddResult.KindMismatch;

            if (data.connections.Contains(connection)) return AddResult.Duplicate;

            if ((!AllowsMultiple(connection.pos1) && IsAnchorOccupied(connection.pos1))
                || (!AllowsMultiple(connection.pos2) && IsAnchorOccupied(connection.pos2)))
                return AddResult.AnchorOccupied;

            if (GetSegmentLength(connection) > MaxLinkLength)
                return AddResult.TooLong;

            bool added = data.connections.Add(connection);
            if (!added) return AddResult.Duplicate;

            InvalidateIndex();
            serverChannel?.BroadcastPacket(data);
            return AddResult.Added;
        }

        public bool TryToRemoveConnection(NodePos pos1, NodePos pos2)
        {
            List<LinkConnection> toRemove = data.connections
                .Where(c => (c.pos1 == pos1 && c.pos2 == pos2) || (c.pos1 == pos2 && c.pos2 == pos1))
                .ToList();

            if (toRemove.Count == 0) return false;

            foreach (LinkConnection con in toRemove) data.connections.Remove(con);
            InvalidateIndex();
            serverChannel?.BroadcastPacket(data);
            return true;
        }

        /// <summary>Cutting a line with shears: removes the connection and gives its item back.</summary>
        public void CutLink(EntityAgent byEntity, NodePos pos1, NodePos pos2)
        {
            // Read the kind before removing - afterwards the connection is gone.
            LinkConnection con = data.connections.FirstOrDefault(c =>
                (c.pos1 == pos1 && c.pos2 == pos2) || (c.pos1 == pos2 && c.pos2 == pos1));
            if (con == null) return;

            byte kind = con.kind;
            if (!TryToRemoveConnection(pos1, pos2)) return;

            Item item = ItemForKind(kind);
            if (item != null) byEntity.TryGiveItemStack(new ItemStack(item));
        }

        /// <summary>Removes every connection touching the given block (e.g. when it is destroyed).</summary>
        public void RemoveAllAt(BlockPos pos)
        {
            if (api.Side == EnumAppSide.Client) return;

            List<LinkConnection> toRemove = data.connections
                .Where(c => c.pos1.blockPos == pos || c.pos2.blockPos == pos)
                .ToList();

            if (toRemove.Count == 0) return;

            foreach (LinkConnection con in toRemove) data.connections.Remove(con);
            InvalidateIndex();
            serverChannel?.BroadcastPacket(data);

            // A block carries lines of a single kind today, but group anyway so a future block
            // with mixed anchors cannot silently hand back the wrong item.
            for (byte kind = 0; kind < LinkKind.Count; kind++)
            {
                int count = toRemove.Count(c => c.kind == kind);
                if (count == 0) continue;

                Item item = ItemForKind(kind);
                if (item != null) api.World.SpawnItemEntity(new ItemStack(item, count), pos);
            }
        }

        #endregion

        #region Valve alternation (arbitration)

        // Which valve currently holds the transfer "turn" on a given line. Server-side only, not
        // persisted (reset on load). This makes two facing active valves take turns instead of
        // fighting each other. A valve with several links takes part in one such line per source.
        private readonly Dictionary<LinkLine, NodePos> lineTokenHolder = new Dictionary<LinkLine, NodePos>();

        /// <summary>
        /// True if <paramref name="me"/> currently holds the transfer turn for the line
        /// me &lt;-&gt; other. If no token exists yet, <paramref name="me"/> claims it.
        /// </summary>
        public bool IsOnTurn(NodePos me, NodePos other)
        {
            LinkLine line = new LinkLine(me, other);
            if (!lineTokenHolder.TryGetValue(line, out NodePos holder))
            {
                lineTokenHolder[line] = me;
                return true;
            }
            return holder == me;
        }

        /// <summary>Hand the transfer turn to the other endpoint of the line.</summary>
        public void PassToken(NodePos me, NodePos other)
        {
            lineTokenHolder[new LinkLine(me, other)] = other;
        }

        #endregion

        /// <summary>Straight-line (Euclidean) distance between the blocks of the segment's two anchors.</summary>
        public static double GetSegmentLength(LinkConnection con)
        {
            BlockPos a = con.pos1.blockPos;
            BlockPos b = con.pos2.blockPos;
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            double dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class LinkNetworkData
    {
        public HashSet<LinkConnection> connections = new HashSet<LinkConnection>();
    }

    /// <summary>
    /// One source reachable from a valve's anchor. <see cref="Endpoint"/> is the far valve/intake
    /// anchor (used for arbitration and for the round-robin cursor); <see cref="FirstHop"/> is the
    /// anchor at the other end of OUR own segment, which is what the renderer needs in order
    /// to wobble just that one line rather than every line on the anchor.
    /// </summary>
    public readonly struct LinkSource
    {
        public readonly NodePos Endpoint;
        public readonly NodePos FirstHop;

        public LinkSource(NodePos endpoint, NodePos firstHop)
        {
            Endpoint = endpoint;
            FirstHop = firstHop;
        }
    }

    /// <summary>
    /// Identity of one line: the unordered pair of its two endpoint anchors, stored in a
    /// canonical order so that (a,b) and (b,a) are the same key. Replaces the former string key
    /// of the arbitration table — same semantics, but no string building on every tick.
    /// </summary>
    public readonly struct LinkLine : IEquatable<LinkLine>
    {
        public readonly NodePos A;
        public readonly NodePos B;

        public LinkLine(NodePos x, NodePos y)
        {
            if (LinkNetworkMod.CompareAnchors(x, y) <= 0) { A = x; B = y; }
            else { A = y; B = x; }
        }

        public bool Equals(LinkLine other) => A == other.A && B == other.B;

        public override bool Equals(object obj) => obj is LinkLine other && Equals(other);

        public override int GetHashCode()
        {
            return ((A?.GetHashCode() ?? 0) * 397) ^ (B?.GetHashCode() ?? 0);
        }
    }
}
