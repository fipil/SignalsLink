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
        }

        private void Event_OnPlayerJoin(IServerPlayer player)
        {
            serverChannel.SendPacket(data, player);
        }

        #region Queries

        /// <summary>Returns connections from the given anchor, oriented so that pos1 == the given position.</summary>
        public List<HoseConnection> GetConnectionsFrom(NodePos pos)
        {
            List<HoseConnection> output = new List<HoseConnection>();
            foreach (HoseConnection con in data.connections.Where(c => c.pos1 == pos || c.pos2 == pos))
            {
                output.Add(con.pos1 == pos
                    ? new HoseConnection(con.pos1, con.pos2)
                    : new HoseConnection(con.pos2, con.pos1));
            }
            return output;
        }

        /// <summary>Is a hose already attached to this anchor? (Max 1 hose per anchor.)</summary>
        public bool IsAnchorOccupied(NodePos pos)
        {
            return data.connections.Any(c => c.pos1 == pos || c.pos2 == pos);
        }

        /// <summary>Does the block at this anchor waive the "max 1 hose per anchor" rule (e.g. Intake)?</summary>
        public bool AllowsMultiple(NodePos pos)
        {
            return api?.World.BlockAccessor.GetBlock(pos.blockPos) is IHoseAnchor a && a.AllowsMultipleHoses(pos);
        }

        /// <summary>
        /// Walks the hose line from an endpoint anchor, through any couplings, and returns the
        /// anchor of the OTHER endpoint (valve/intake) of that line — or null if the line is
        /// dangling or loops. An endpoint block has exactly 1 hose anchor; a coupling has 2
        /// (pass-through). The line is linear (no branching), so each anchor has at most 1 hose.
        /// </summary>
        public NodePos GetOtherEndpoint(IWorldAccessor world, NodePos startAnchor)
        {
            NodePos currentAnchor = startAnchor;
            HashSet<NodePos> visited = new HashSet<NodePos>();

            while (true)
            {
                if (!visited.Add(currentAnchor)) return null; // loop guard

                List<HoseConnection> cons = GetConnectionsFrom(currentAnchor);
                if (cons.Count == 0) return null; // dangling

                NodePos otherAnchor = cons[0].pos2; // anchor on the connected block
                if (visited.Contains(otherAnchor)) return null;

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

                visited.Add(otherAnchor);
                currentAnchor = through;
            }
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
            serverChannel?.BroadcastPacket(data);

            if (hoseItem != null)
            {
                api.World.SpawnItemEntity(new ItemStack(hoseItem, toRemove.Count), pos);
            }
        }

        #endregion

        #region Valve alternation (arbitration)

        // Per hose line (identified by its two endpoint anchors), which valve currently holds
        // the transfer "turn". Server-side only, not persisted (reset on load). This makes two
        // facing active valves take turns instead of fighting each other.
        private readonly Dictionary<string, NodePos> lineTokenHolder = new Dictionary<string, NodePos>();

        private static string LineKey(NodePos a, NodePos b)
        {
            string sa = a.ToString();
            string sb = b.ToString();
            return string.CompareOrdinal(sa, sb) <= 0 ? sa + "|" + sb : sb + "|" + sa;
        }

        /// <summary>
        /// True if <paramref name="me"/> currently holds the transfer turn for the line
        /// me &lt;-&gt; other. If no token exists yet, <paramref name="me"/> claims it.
        /// </summary>
        public bool IsOnTurn(NodePos me, NodePos other)
        {
            string key = LineKey(me, other);
            if (!lineTokenHolder.TryGetValue(key, out NodePos holder))
            {
                lineTokenHolder[key] = me;
                return true;
            }
            return holder == me;
        }

        /// <summary>Hand the transfer turn to the other endpoint of the line.</summary>
        public void PassToken(NodePos me, NodePos other)
        {
            lineTokenHolder[LineKey(me, other)] = other;
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
}
