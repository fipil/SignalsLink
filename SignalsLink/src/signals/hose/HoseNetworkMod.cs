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

namespace SignalsLink.src.signals.hose
{
    /// <summary>
    /// Persistence + synchronization + rendering of the ManagedHose network. Mirror of the
    /// Signals <c>HangingWiresMod</c>, but <b>independent of the signal network</b> (a hose
    /// carries no signal, so <c>SignalNetworkMod</c> is never called). It additionally
    /// validates segment length and anchor occupancy.
    /// </summary>
    public class HoseNetworkMod : ModSystem
    {
        /// <summary>Maximum length of a single hose segment (anchor-to-anchor), straight-line, in blocks.</summary>
        public const double MaxHoseLength = 10.0;

        public const string ChannelName = "signalslinkhoses";
        public const string SaveKey = "signalslinkHoseData";
        public const string HoseItemCode = "signalslink:hose";

        public HangingHosesRenderer Renderer;

        IServerNetworkChannel serverChannel;
        IClientNetworkChannel clientChannel;

        public HoseNetworkData data = new HoseNetworkData();

        ICoreAPI api;
        ICoreServerAPI sapi;
        ICoreClientAPI capi;

        Item hoseItem;

        public override bool ShouldLoad(EnumAppSide forSide) => true;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            this.api = api;

            if (api.World is IClientWorldAccessor)
            {
                clientChannel = ((ICoreClientAPI)api).Network.RegisterChannel(ChannelName)
                    .RegisterMessageType(typeof(HoseNetworkData))
                    .SetMessageHandler<HoseNetworkData>(OnDataFromServer);
            }
            else
            {
                serverChannel = ((ICoreServerAPI)api).Network.RegisterChannel(ChannelName)
                    .RegisterMessageType(typeof(HoseNetworkData));
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

            hoseItem = api.World.GetItem(new AssetLocation(HoseItemCode));
        }

        private void OnBlockTexturesLoaded()
        {
            Renderer = new HangingHosesRenderer(capi, this);
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

        private void OnDataFromServer(HoseNetworkData data)
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
                this.data = SerializerUtil.Deserialize<HoseNetworkData>(blob);
            }
            catch (Exception)
            {
                this.data = new HoseNetworkData();
            }

            InvalidateIndex();
        }

        private void Event_OnPlayerJoin(IServerPlayer player)
        {
            serverChannel.SendPacket(data, player);
        }

        #region Queries

        // Anchor -> the connections touching it. Without it every lookup was a linear scan over
        // all connections, and the valve does one scan per hose per hop on every tick; with
        // multi-source valves that adds up. Rebuilt lazily: explicitly invalidated by every
        // mutation and whenever `data` is replaced, plus a connection-count backstop so a missed
        // invalidation cannot leave a permanently stale index.
        private Dictionary<NodePos, List<HoseConnection>> connectionIndex;
        private int indexedCount = -1;

        private Dictionary<NodePos, List<HoseConnection>> ConnectionIndex
        {
            get
            {
                if (connectionIndex == null || indexedCount != data.connections.Count) RebuildIndex();
                return connectionIndex;
            }
        }

        private void RebuildIndex()
        {
            Dictionary<NodePos, List<HoseConnection>> index = new Dictionary<NodePos, List<HoseConnection>>();
            foreach (HoseConnection con in data.connections)
            {
                IndexAnchor(index, con.pos1, con);
                IndexAnchor(index, con.pos2, con);
            }
            connectionIndex = index;
            indexedCount = data.connections.Count;
        }

        private static void IndexAnchor(Dictionary<NodePos, List<HoseConnection>> index, NodePos anchor, HoseConnection con)
        {
            if (anchor == null) return;
            if (!index.TryGetValue(anchor, out List<HoseConnection> list))
            {
                list = new List<HoseConnection>();
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
        public List<HoseConnection> GetConnectionsFrom(NodePos pos)
        {
            List<HoseConnection> output = new List<HoseConnection>();
            if (pos == null || !ConnectionIndex.TryGetValue(pos, out List<HoseConnection> touching)) return output;

            foreach (HoseConnection con in touching)
            {
                output.Add(con.pos1 == pos
                    ? new HoseConnection(con.pos1, con.pos2)
                    : new HoseConnection(con.pos2, con.pos1));
            }
            return output;
        }

        /// <summary>Is any hose already attached to this anchor? Only meaningful for anchors that
        /// do not waive the "max 1 hose" rule (see <see cref="AllowsMultiple"/>).</summary>
        public bool IsAnchorOccupied(NodePos pos)
        {
            return pos != null && ConnectionIndex.TryGetValue(pos, out List<HoseConnection> touching) && touching.Count > 0;
        }

        /// <summary>Does the block at this anchor waive the "max 1 hose per anchor" rule (e.g. Intake)?</summary>
        public bool AllowsMultiple(NodePos pos)
        {
            return api?.World.BlockAccessor.GetBlock(pos.blockPos) is IHoseAnchor a && a.AllowsMultipleHoses(pos);
        }

        /// <summary>
        /// All endpoints (valve/intake) reachable from the given endpoint anchor — one per hose
        /// attached to it. A valve anchor may carry several hoses (multi-source pumping), so this
        /// fans out over them; each individual branch then stays linear, because a coupling is
        /// strictly pass-through (2 anchors, at most 1 hose each).
        ///
        /// Endpoints are deduplicated (two parallel routes to the same far anchor collapse into
        /// one logical line) and returned in a stable order — <c>data.connections</c> is a HashSet
        /// whose iteration order is not reproducible across reloads, and the caller round-robins
        /// over this list.
        /// </summary>
        public List<HoseSource> GetOtherEndpoints(IWorldAccessor world, NodePos startAnchor)
        {
            List<HoseSource> sources = new List<HoseSource>();

            foreach (HoseConnection con in GetConnectionsFrom(startAnchor))
            {
                NodePos endpoint = WalkToEndpoint(world, startAnchor, con.pos2);
                if (endpoint == null) continue;
                if (endpoint == startAnchor) continue;                        // walked back to ourselves
                if (sources.Exists(s => s.Endpoint == endpoint)) continue;    // parallel route to the same anchor
                sources.Add(new HoseSource(endpoint, con.pos2));
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

                IHoseAnchor block = world.BlockAccessor.GetBlock(otherAnchor.blockPos) as IHoseAnchor;
                if (block == null) return null;

                NodePos[] anchors = block.GetHoseAnchors(world, otherAnchor.blockPos);
                if (anchors.Length <= 1) return otherAnchor; // reached an endpoint (valve/intake)

                // Coupling: continue out its other anchor.
                NodePos through = null;
                foreach (NodePos a in anchors)
                {
                    if (a != otherAnchor) { through = a; break; }
                }
                if (through == null) return null;
                if (!visited.Add(through)) return null;

                // A coupling anchor never carries more than one hose, so this hop is unambiguous.
                List<HoseConnection> cons = GetConnectionsFrom(through);
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

        public enum AddResult { Added, Duplicate, SameAnchor, SameBlock, AnchorOccupied, TooLong }

        /// <summary>
        /// Adds a connection with server-side validation: same anchor, same block, anchor
        /// occupancy, segment length. Never calls SignalNetworkMod — a hose carries no signal.
        /// </summary>
        public AddResult TryToAddConnection(HoseConnection connection)
        {
            if (connection?.pos1 == null || connection.pos2 == null) return AddResult.SameAnchor;
            if (connection.pos1 == connection.pos2) return AddResult.SameAnchor;

            // Both anchors on the same block (e.g. a coupling's two anchors) must not be joined.
            if (connection.pos1.blockPos.Equals(connection.pos2.blockPos)) return AddResult.SameBlock;

            if (data.connections.Contains(connection)) return AddResult.Duplicate;

            if ((!AllowsMultiple(connection.pos1) && IsAnchorOccupied(connection.pos1))
                || (!AllowsMultiple(connection.pos2) && IsAnchorOccupied(connection.pos2)))
                return AddResult.AnchorOccupied;

            if (GetSegmentLength(connection) > MaxHoseLength)
                return AddResult.TooLong;

            bool added = data.connections.Add(connection);
            if (!added) return AddResult.Duplicate;

            InvalidateIndex();
            serverChannel?.BroadcastPacket(data);
            return AddResult.Added;
        }

        public bool TryToRemoveConnection(NodePos pos1, NodePos pos2)
        {
            List<HoseConnection> toRemove = data.connections
                .Where(c => (c.pos1 == pos1 && c.pos2 == pos2) || (c.pos1 == pos2 && c.pos2 == pos1))
                .ToList();

            if (toRemove.Count == 0) return false;

            foreach (HoseConnection con in toRemove) data.connections.Remove(con);
            InvalidateIndex();
            serverChannel?.BroadcastPacket(data);
            return true;
        }

        /// <summary>Cutting a hose with shears: removes the connection and gives the hose item back.</summary>
        public void CutHose(EntityAgent byEntity, NodePos pos1, NodePos pos2)
        {
            if (TryToRemoveConnection(pos1, pos2) && hoseItem != null)
            {
                byEntity.TryGiveItemStack(new ItemStack(hoseItem));
            }
        }

        /// <summary>Removes every connection touching the given block (e.g. when it is destroyed).</summary>
        public void RemoveAllAt(BlockPos pos)
        {
            if (api.Side == EnumAppSide.Client) return;

            List<HoseConnection> toRemove = data.connections
                .Where(c => c.pos1.blockPos == pos || c.pos2.blockPos == pos)
                .ToList();

            if (toRemove.Count == 0) return;

            foreach (HoseConnection con in toRemove) data.connections.Remove(con);
            InvalidateIndex();
            serverChannel?.BroadcastPacket(data);

            if (hoseItem != null)
            {
                api.World.SpawnItemEntity(new ItemStack(hoseItem, toRemove.Count), pos);
            }
        }

        #endregion

        #region Valve alternation (arbitration)

        // Which valve currently holds the transfer "turn" on a given line. Server-side only, not
        // persisted (reset on load). This makes two facing active valves take turns instead of
        // fighting each other. A valve with several hoses takes part in one such line per source.
        private readonly Dictionary<HoseLine, NodePos> lineTokenHolder = new Dictionary<HoseLine, NodePos>();

        /// <summary>
        /// True if <paramref name="me"/> currently holds the transfer turn for the line
        /// me &lt;-&gt; other. If no token exists yet, <paramref name="me"/> claims it.
        /// </summary>
        public bool IsOnTurn(NodePos me, NodePos other)
        {
            HoseLine line = new HoseLine(me, other);
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
            lineTokenHolder[new HoseLine(me, other)] = other;
        }

        #endregion

        /// <summary>Straight-line (Euclidean) distance between the blocks of the segment's two anchors.</summary>
        public static double GetSegmentLength(HoseConnection con)
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
    public class HoseNetworkData
    {
        public HashSet<HoseConnection> connections = new HashSet<HoseConnection>();
    }

    /// <summary>
    /// One source reachable from a valve's anchor. <see cref="Endpoint"/> is the far valve/intake
    /// anchor (used for arbitration and for the round-robin cursor); <see cref="FirstHop"/> is the
    /// anchor at the other end of OUR own hose segment, which is what the renderer needs in order
    /// to wobble just that one hose rather than every hose on the anchor.
    /// </summary>
    public readonly struct HoseSource
    {
        public readonly NodePos Endpoint;
        public readonly NodePos FirstHop;

        public HoseSource(NodePos endpoint, NodePos firstHop)
        {
            Endpoint = endpoint;
            FirstHop = firstHop;
        }
    }

    /// <summary>
    /// Identity of one hose line: the unordered pair of its two endpoint anchors, stored in a
    /// canonical order so that (a,b) and (b,a) are the same key. Replaces the former string key
    /// of the arbitration table — same semantics, but no string building on every tick.
    /// </summary>
    public readonly struct HoseLine : IEquatable<HoseLine>
    {
        public readonly NodePos A;
        public readonly NodePos B;

        public HoseLine(NodePos x, NodePos y)
        {
            if (HoseNetworkMod.CompareAnchors(x, y) <= 0) { A = x; B = y; }
            else { A = y; B = x; }
        }

        public bool Equals(HoseLine other) => A == other.A && B == other.B;

        public override bool Equals(object obj) => obj is HoseLine other && Equals(other);

        public override int GetHashCode()
        {
            return ((A?.GetHashCode() ?? 0) * 397) ^ (B?.GetHashCode() ?? 0);
        }
    }
}
