using System.Text;
using System.Text.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace YttRepro
{
    /// <summary>
    /// One straight track across a chunk column boundary, a locomotive on one side, two boxcars
    /// on the other, coupled. Then the boxcars are taken away the way a chunk unload takes them,
    /// and the locomotive is watched: does YTT ever notice its convoy is incomplete?
    ///
    /// Everything the rig placed is remembered in the savegame so that `check` and `clean` work
    /// after a restart too - the restart is half of the bug.
    /// </summary>
    public sealed class ReproRig
    {
        private const string SaveKey = "yttrepro";
        private const string Domain = "yangtransport";

        /// <summary>Primitive tier is 5 blocks long; a boxcar is 7. Couplers must be within 4.</summary>
        private const string LocomotiveCode = "sglocomotive-primitive";
        private const string WagonCode = "sgwagon_boxcar";
        private const string RailCode = "widerails_straight-ns-metal";

        /// <summary>Block offsets along Z from the locomotive: rear couplers 2 and 1 blocks apart.</summary>
        private static readonly int[] WagonOffsets = { -8, -16 };
        private const int RailBehind = 40;
        private const int RailAhead = 24;

        private readonly ICoreServerAPI api;

        public ReproRig(ICoreServerAPI api)
        {
            this.api = api;
        }

        // ---------------------------------------------------------------------------- what was built

        private sealed class Placed
        {
            public long Locomotive { get; set; }
            public List<long> Wagons { get; set; } = new List<long>();
            public int X { get; set; }
            public int Y { get; set; }
            public int Z { get; set; }
        }

        private Placed Load()
        {
            byte[] data = api.WorldManager.SaveGame.GetData(SaveKey);
            if (data == null || data.Length == 0) return null;

            try
            {
                return JsonSerializer.Deserialize<Placed>(data);
            }
            catch (Exception e)
            {
                api.Logger.Warning("[yttrepro] saved rig state unreadable, ignoring: " + e.Message);
                return null;
            }
        }

        private void Store(Placed placed)
        {
            if (placed == null) api.WorldManager.SaveGame.StoreData(SaveKey, Array.Empty<byte>());
            else api.WorldManager.SaveGame.StoreData(SaveKey, JsonSerializer.SerializeToUtf8Bytes(placed));
        }

        // ------------------------------------------------------------------------------------ build

        /// <summary>
        /// The locomotive stands 4 blocks south of a chunk column boundary, facing south (+Z), the
        /// boxcars behind it north of the boundary. So the train straddles two chunk columns, which
        /// is the whole point.
        /// </summary>
        public string Build(IServerPlayer player)
        {
            if (Load() != null) return "There is already a rig; run /yttrepro clean first.";

            EntityPos at = player.Entity.Pos;
            int x = (int)Math.Floor(at.X) + 6;
            int y = (int)Math.Floor(at.Y);
            int column = (int)Math.Floor(at.Z / 32.0) + 1;
            int z = column * 32 + 4;

            Block rail = api.World.GetBlock(new AssetLocation(Domain, RailCode));
            Block ground = api.World.GetBlock(new AssetLocation("game", "cobblestone"));
            if (rail == null) return "Block " + Domain + ":" + RailCode + " is not registered - is YTT loaded?";

            IBlockAccessor blocks = api.World.BlockAccessor;

            for (int dz = -RailBehind; dz <= RailAhead; dz++)
            {
                BlockPos pos = new BlockPos(x, y, z + dz, 0);

                blocks.SetBlock(ground.Id, pos.DownCopy());
                for (int dy = 1; dy <= 4; dy++) blocks.SetBlock(0, pos.UpCopy(dy));
                blocks.SetBlock(rail.Id, pos);
            }

            Placed placed = new Placed { X = x, Y = y, Z = z };

            Entity locomotive = Spawn(LocomotiveCode, x, y, z);
            if (locomotive == null) return "Could not spawn " + Domain + ":" + LocomotiveCode + ".";
            placed.Locomotive = locomotive.EntityId;

            foreach (int offset in WagonOffsets)
            {
                Entity wagon = Spawn(WagonCode, x, y, z + offset);
                if (wagon == null) return "Could not spawn " + Domain + ":" + WagonCode + ".";
                placed.Wagons.Add(wagon.EntityId);
            }

            Store(placed);

            // Vehicles bind to the rail on their own tick; coupling has to wait for that.
            api.Event.RegisterCallback(_ => Couple(player, placed), 2000);

            return "Track " + (RailBehind + RailAhead + 1) + " blocks at x=" + x + " y=" + y + " z=" + (z - RailBehind) + ".." + (z + RailAhead)
                + "; locomotive " + placed.Locomotive + " in chunk column " + column
                + ", wagons " + string.Join(",", placed.Wagons) + " in column " + (column - 1)
                + ". Coupling in 2 s - watch the chat.";
        }

        /// <summary>Placed exactly as YTT's own item does it, minus the placement checks.</summary>
        private Entity Spawn(string code, int x, int y, int z)
        {
            EntityProperties type = api.World.GetEntityType(new AssetLocation(Domain, code));
            if (type == null) return null;

            Entity entity = api.World.ClassRegistry.CreateEntity(type);
            entity.Pos.X = x + 0.5;
            entity.Pos.Y = y + 0.125;
            entity.Pos.Z = z + 0.5;
            entity.Pos.Yaw = 0f;                             // facing +Z
            entity.Attributes.SetFloat("seedYaw", 0f);
            api.World.SpawnEntity(entity);

            return entity;
        }

        /// <summary>
        /// YTT's public RailConvoySystem.TryMerge(anchorEntityId, clickedVehicle, player) - what the
        /// linkage pole calls. Locomotive first, then each wagon onto the growing convoy.
        /// </summary>
        private void Couple(IServerPlayer player, Placed placed)
        {
            ModSystem convoys = YttPeek.System(api, "RailConvoySystem");
            StringBuilder report = new StringBuilder();

            try
            {
                foreach (long wagonId in placed.Wagons)
                {
                    Entity wagon = api.World.GetEntityById(wagonId);
                    if (wagon == null) { report.Append("wagon " + wagonId + " is gone; "); continue; }

                    bool merged = (bool)YttPeek.Call(convoys, "TryMerge", placed.Locomotive, wagon, player);
                    report.Append("wagon " + wagonId + (merged ? " coupled; " : " NOT coupled (see the in-game error); "));
                }
            }
            catch (Exception e)
            {
                report.Append("coupling failed: " + e.GetBaseException().Message);
                api.Logger.Error("[yttrepro] " + e);
            }

            Tell(player, "[yttrepro] " + report + " Now /yttrepro check should say COMPLETE.");
        }

        // ------------------------------------------------------------------------------------ split

        /// <summary>
        /// Takes the wagons away exactly as a chunk unload does: DespawnEntity with reason Unload.
        /// YTT cannot tell the difference - its OnEntityDespawn sees the same call - and the
        /// locomotive's chunk stays loaded, which is the situation a chunk keeper produces.
        /// </summary>
        public string Split()
        {
            Placed placed = Load();
            if (placed == null) return "No rig; run /yttrepro build first.";

            int gone = 0;
            foreach (long wagonId in placed.Wagons)
            {
                Entity wagon = api.World.GetEntityById(wagonId);
                if (wagon == null) continue;

                api.World.DespawnEntity(wagon, new EntityDespawnData { Reason = EnumDespawnReason.Unload });
                gone++;
            }

            return gone + " wagons unloaded like a chunk unload would. Wait 10 s, then /yttrepro check.";
        }

        // ------------------------------------------------------------------------------------ check

        /// <summary>
        /// STUCK is the bug: the locomotive keeps its convoy, the convoy is incomplete, and nothing
        /// in YTT is trying to do anything about it. RECOVERED is what a fixed YTT should reach on
        /// its own within a few seconds of the split.
        /// </summary>
        public string Check()
        {
            Placed placed = Load();
            if (placed == null) return "No rig; run /yttrepro build first.";

            ModSystem convoys = YttPeek.System(api, "RailConvoySystem");
            ModSystem graph = YttPeek.System(api, "RailGraphServerSystem");
            StringBuilder text = new StringBuilder();

            Entity locomotive = api.World.GetEntityById(placed.Locomotive);
            text.Append("locomotive " + placed.Locomotive + ": " + (locomotive == null ? "NOT LOADED" : Describe(locomotive)) + "\n");

            foreach (long wagonId in placed.Wagons)
            {
                Entity wagon = api.World.GetEntityById(wagonId);
                text.Append("wagon " + wagonId + ": " + (wagon == null ? "not loaded" : Describe(wagon)) + "\n");
            }

            text.Append("rail graph: runtimeReady=" + YttPeek.Get(graph, "RuntimeReady")
                + " requestedSupportColumns=" + YttPeek.Count(YttPeek.Get(graph, "RequestedRailSupportColumns")) + "\n");

            long head = locomotive != null ? YttPeek.GetLong(locomotive, "ConvoyHeadEntityID") : 0;
            object convoy = head != 0 ? YttPeek.Entry(YttPeek.Get(convoys, "Convoys"), head) : null;

            if (convoy == null)
            {
                text.Append("convoy: none registered for head " + head + "\n");
            }
            else
            {
                text.Append("convoy " + head + ": fullyLoaded=" + YttPeek.Get(convoy, "FullyLoaded")
                    + " loaded=" + YttPeek.Count(YttPeek.Get(convoy, "LoadedMembers")) + "/" + YttPeek.Count(YttPeek.Get(convoy, "Members"))
                    + " expected=" + YttPeek.Get(convoy, "ExpectedMemberCount")
                    + " haloTicks=" + YttPeek.Get(convoy, "IncompleteLoadedHaloReadyTicks") + "\n");
            }

            text.Append("verdict: " + Verdict(locomotive, head, convoy));
            return text.ToString();
        }

        private static string Verdict(Entity locomotive, long head, object convoy)
        {
            if (locomotive == null) return "locomotive not loaded - stand next to the rig";

            bool derailed = YttPeek.GetBool(locomotive, "Derailed");

            if (head == 0)
            {
                return derailed
                    ? "RECOVERED - YTT dissolved the incomplete convoy and derailed the locomotive; it can be picked up"
                    : "FREE - the locomotive is not in a convoy";
            }

            if (convoy == null) return "INCONSISTENT - the locomotive names head " + head + " but RailConvoySystem has no such convoy";
            if (YttPeek.GetBool(convoy, "FullyLoaded")) return "COMPLETE - every member is loaded";

            return "STUCK - convoy incomplete, locomotive still coupled and not derailed: it will not drive, untether or be picked up,"
                + " and haloTicks/requestedSupportColumns show whether any recovery is even trying";
        }

        // --------------------------------------------------------------------------------- untether

        /// <summary>What the player gets when they try the linkage pole on the stuck locomotive.</summary>
        public string Untether(IServerPlayer player)
        {
            Placed placed = Load();
            if (placed == null) return "No rig; run /yttrepro build first.";

            Entity locomotive = api.World.GetEntityById(placed.Locomotive);
            if (locomotive == null) return "Locomotive not loaded.";

            ModSystem convoys = YttPeek.System(api, "RailConvoySystem");
            bool result = (bool)YttPeek.Call(convoys, "TryUntether", locomotive, player);

            return "TryUntether returned " + result + ". Did you get any in-game message? On the stuck train you should not - that is the silent failure.";
        }

        // ------------------------------------------------------------------------------------ clean

        public string Clean()
        {
            Placed placed = Load();
            if (placed == null) return "Nothing to clean.";

            int removed = 0;
            foreach (long id in placed.Wagons.Append(placed.Locomotive))
            {
                Entity entity = api.World.GetEntityById(id);
                if (entity == null) continue;

                api.World.DespawnEntity(entity, new EntityDespawnData { Reason = EnumDespawnReason.Removed });
                removed++;
            }

            IBlockAccessor blocks = api.World.BlockAccessor;
            for (int dz = -RailBehind; dz <= RailAhead; dz++)
            {
                blocks.SetBlock(0, new BlockPos(placed.X, placed.Y, placed.Z + dz, 0));
            }

            Store(null);
            return removed + " vehicles removed, track removed. Wagons that were split off and are not loaded stay in their chunk as shells.";
        }

        // ---------------------------------------------------------------------------------- helpers

        private static string Describe(Entity entity)
        {
            string convoy = YttPeek.IsVehicle(entity)
                ? " head=" + YttPeek.Get(entity, "ConvoyHeadEntityID") + " idx=" + YttPeek.Get(entity, "ConvoyOrderIndex")
                    + " expects=" + YttPeek.Get(entity, "ExpectedConvoyMemberCount") + " derailed=" + YttPeek.Get(entity, "Derailed")
                : "";

            return entity.Code + " @" + entity.Pos.X.ToString("F1") + "," + entity.Pos.Y.ToString("F1") + "," + entity.Pos.Z.ToString("F1") + convoy;
        }

        private static void Tell(IServerPlayer player, string message)
        {
            player.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);
        }
    }
}
