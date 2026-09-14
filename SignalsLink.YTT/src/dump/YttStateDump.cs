using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SignalsLink.YTT.src.dump
{
    /// <summary>
    /// Writes down what Yang's Transport Tycoon believes about its trains, for `/slytt dump`.
    ///
    /// YTT keeps a train in three places that do not have to agree: the entities in loaded chunks,
    /// the convoy authority (which copy of a vehicle is the real one), and the offscreen simulation
    /// (trains that exist only as snapshots). When a train gets stuck, the question is always which
    /// of the three is out of step, and none of it is visible in game. This reads all three.
    ///
    /// Reflection only, read only, and every section stands on its own: a member that YTT has
    /// renamed makes that one section say so, the rest still prints. Nothing here is on any hot
    /// path, so there is no probe and no fingerprint - a dump that comes out half empty is still a
    /// dump.
    /// </summary>
    public sealed class YttStateDump
    {
        private const BindingFlags Anywhere =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private const string GenerationAttribute = "yangtransport:authorityGeneration";

        private readonly ICoreServerAPI api;
        private readonly StringBuilder text = new StringBuilder();

        private readonly ModSystem authority;
        private readonly ModSystem offscreen;
        private readonly ModSystem convoys;
        private readonly ModSystem railGraph;

        private YttStateDump(ICoreServerAPI api)
        {
            this.api = api;
            authority = api.ModLoader.GetModSystem("YangTransport.RailConvoyAuthoritySystem");
            offscreen = api.ModLoader.GetModSystem("YangTransport.OffscreenConvoySimSystem");
            convoys = api.ModLoader.GetModSystem("YangTransport.RailConvoySystem");
            railGraph = api.ModLoader.GetModSystem("YangTransport.RailGraphServerSystem");
        }

        /// <summary>
        /// The whole map from YTT's own books, and with <paramref name="radius"/> above zero also
        /// every vehicle entity that far around <paramref name="around"/>.
        /// </summary>
        public static string Write(ICoreServerAPI api, Vec3d around, int radius)
        {
            YttStateDump dump = new YttStateDump(api);

            dump.Header();
            dump.Section("Authority (whole map)", dump.Authority);
            dump.Section("Virtual convoys (whole map)", dump.VirtualConvoys);
            dump.Section("Loaded convoys", dump.LoadedConvoys);
            dump.Section("Track occupancy (whole map)", dump.Occupancy);

            if (radius > 0 && around != null)
            {
                dump.Section("Vehicles within " + radius + " blocks", () => dump.Nearby(around, radius));
            }

            return dump.text.ToString();
        }

        // ------------------------------------------------------------------------------ sections

        private void Header()
        {
            Line("YTT state dump at " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                + ", world time " + api.World.Calendar.TotalHours.ToString("F2", CultureInfo.InvariantCulture) + " h");

            object graph = Get(railGraph, "LiveGraph");
            Line("rail graph: runtimeReady=" + Get(railGraph, "RuntimeReady")
                + " loaded=" + Get(railGraph, "Loaded")
                + " buildVersion=" + Get(graph, "BuildVersion")
                + " nodes=" + Get(graph, "NodeCount") + " edges=" + Get(graph, "ConnectionCount"));
        }

        /// <summary>
        /// Who YTT thinks each vehicle is: Material (an entity in a chunk is the truth), Virtual (a
        /// snapshot in a virtual convoy is), Deleted. Grouped by head, so a whole train reads as
        /// one block. A loaded entity whose generation differs from the record is a stale shell and
        /// will be discarded the next time YTT looks at it.
        /// </summary>
        private void Authority()
        {
            IDictionary byEntity = Get(authority, "AuthorityByEntityID") as IDictionary;
            IDictionary topologies = Get(authority, "MaterialTopologiesByHead") as IDictionary;
            IEnumerable deleted = Get(authority, "DeletedAuthorityIDs") as IEnumerable;

            if (byEntity == null) throw new MissingMemberException("AuthorityByEntityID");

            // head -> members, in convoy order
            SortedDictionary<long, List<(long id, object record)>> byHead = new SortedDictionary<long, List<(long, object)>>();

            foreach (DictionaryEntry entry in byEntity)
            {
                long head = (long)Get(entry.Value, "HeadID");
                if (!byHead.TryGetValue(head, out List<(long, object)> members))
                {
                    members = new List<(long, object)>();
                    byHead[head] = members;
                }
                members.Add(((long)entry.Key, entry.Value));
            }

            Line(byEntity.Count + " vehicles under " + byHead.Count + " heads");

            foreach (KeyValuePair<long, List<(long id, object record)>> pair in byHead)
            {
                pair.Value.Sort((a, b) => ((int)Get(a.record, "ConvoyIndex")).CompareTo((int)Get(b.record, "ConvoyIndex")));

                string topology = "";
                if (topologies != null && topologies.Contains(pair.Key))
                {
                    long[] members = Get(topologies[pair.Key], "Members") as long[];
                    topology = " topology=[" + string.Join(",", members ?? Array.Empty<long>()) + "]";
                }

                Line("head " + pair.Key + topology);

                foreach ((long id, object record) in pair.Value)
                {
                    Line("  " + id + " " + Get(record, "Mode") + " gen=" + Get(record, "Generation")
                        + " idx=" + Get(record, "ConvoyIndex") + " " + EntityState(id));
                }
            }

            if (deleted != null)
            {
                List<string> ids = new List<string>();
                foreach (object id in deleted) ids.Add(id.ToString());
                if (ids.Count > 0) Line("deleted: " + string.Join(",", ids));
            }
        }

        /// <summary>
        /// Trains that exist only as snapshots. An orphaned one has stopped for good and comes back
        /// only when every column of its frozen poses is loaded at once and no entity carries any of
        /// its ids - derailed and uncoupled.
        /// </summary>
        private void VirtualConvoys()
        {
            IDictionary virtuals = Get(offscreen, "VirtualConvoys") as IDictionary;
            IDictionary pending = Get(offscreen, "PendingConvoys") as IDictionary;

            if (virtuals == null) throw new MissingMemberException("VirtualConvoys");

            Line(virtuals.Count + " virtual, " + (pending?.Count.ToString() ?? "?") + " pending");

            foreach (DictionaryEntry entry in virtuals)
            {
                object convoy = entry.Value;
                object automation = Get(convoy, "Automation");

                Line("convoy " + Get(convoy, "HeadKey")
                    + " orphaned=" + Get(convoy, "Orphaned")
                    + " stopped=" + Get(convoy, "TerminalStopped")
                    + " pathTape=" + (Get(convoy, "PathTape") != null)
                    + " poseValid=" + Get(convoy, "MaterializationPoseValid")
                    + " automation=" + (automation == null ? "none" : Get(automation, "Status")?.ToString())
                    + " simulated=" + Num(Get(convoy, "DistanceSimulated")) + " blocks");

                IList vehicles = Get(convoy, "Vehicles") as IList;
                IList poses = Get(convoy, "FrozenPoses") as IList;

                for (int i = 0; vehicles != null && i < vehicles.Count; i++)
                {
                    object snapshot = vehicles[i];
                    long id = (long)Get(snapshot, "EntityID");
                    string pose = poses != null && i < poses.Count ? " frozen@" + Pose(poses[i]) : "";

                    Line("  " + id + " " + Get(snapshot, "Code")
                        + " idx=" + Get(snapshot, "ConvoyIndex")
                        + " gen=" + Get(snapshot, "AuthorityGeneration")
                        + (Truthy(Get(snapshot, "HasTractionEngine")) ? " engine" : "")
                        + pose + " " + EntityState(id));
                }

                IEnumerable missing = Get(convoy, "DesiredMaterializationColumns") as IEnumerable;
                List<string> columns = new List<string>();

                if (missing != null)
                {
                    foreach (object packed in missing)
                    {
                        long key = (long)packed;
                        columns.Add(((int)(key >> 32)) + "/" + ((int)(uint)(key & 0xFFFFFFFF)));
                    }
                }

                Line("  waiting for columns: " + (columns.Count == 0 ? "none" : string.Join(" ", columns)));
            }
        }

        /// <summary>
        /// Convoys assembled from loaded entities. `fullyLoaded=False` with a head that is loaded is
        /// the stuck train: it will not move, untether or be picked up until every member loads.
        /// </summary>
        private void LoadedConvoys()
        {
            IDictionary loaded = Get(convoys, "Convoys") as IDictionary;
            IDictionary vehicles = Get(convoys, "LoadedVehicles") as IDictionary;
            IEnumerable rejected = Get(convoys, "RejectedConvoyHeads") as IEnumerable;

            if (loaded == null) throw new MissingMemberException("Convoys");

            Line(loaded.Count + " convoys, " + (vehicles?.Count.ToString() ?? "?") + " loaded vehicles");

            foreach (DictionaryEntry entry in loaded)
            {
                object convoy = entry.Value;
                IList members = Get(convoy, "Members") as IList;
                IList loadedMembers = Get(convoy, "LoadedMembers") as IList;
                List<string> ids = new List<string>();

                if (members != null) foreach (object id in members) ids.Add(id.ToString());

                Line("convoy " + Get(convoy, "HeadID")
                    + " fullyLoaded=" + Get(convoy, "FullyLoaded")
                    + " loaded=" + (loadedMembers?.Count.ToString() ?? "?") + "/" + ids.Count
                    + " expected=" + Get(convoy, "ExpectedMemberCount")
                    + " authoritative=" + Get(convoy, "AuthoritativeTopology")
                    + " haloTicks=" + Get(convoy, "IncompleteLoadedHaloReadyTicks")
                    + " members=[" + string.Join(",", ids) + "]");
            }

            if (vehicles != null)
            {
                foreach (DictionaryEntry entry in vehicles)
                {
                    Entity entity = Get(entry.Value, "Entity") as Entity;
                    Line("  " + entry.Key + " " + Vehicle(entity));
                }
            }

            if (rejected != null)
            {
                List<string> ids = new List<string>();
                foreach (object id in rejected) ids.Add(id.ToString());
                if (ids.Count > 0) Line("rejected heads (this session only): " + string.Join(",", ids));
            }
        }

        /// <summary>
        /// Which owner holds which stretch of track. The graph is in the savegame whole, so this is
        /// the one place a train in an unloaded chunk still leaves a mark - roughly where it is.
        /// </summary>
        private void Occupancy()
        {
            IDictionary byOwner = Get(railGraph, "OccupancyByOwner") as IDictionary;
            object graph = Get(railGraph, "LiveGraph");

            if (byOwner == null || graph == null) throw new MissingMemberException("OccupancyByOwner");

            MethodInfo endpoints = graph.GetType().GetMethod("TryGetEdgeEndpoints", Anywhere);

            Line(byOwner.Count + " owners");

            foreach (DictionaryEntry entry in byOwner)
            {
                IEnumerable edges = Get(entry.Value, "Edges") as IEnumerable;
                int count = 0, placed = 0;
                double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
                int dimension = 0;

                if (edges != null)
                {
                    foreach (object edge in edges)
                    {
                        count++;
                        if (endpoints == null) continue;

                        object[] args = { (ulong)edge, null, null, null };
                        if (!(bool)endpoints.Invoke(graph, args)) continue;

                        foreach (object key in new[] { args[1], args[2] })
                        {
                            Vec3d at = key.GetType().GetMethod("ToWorld", Anywhere)?.Invoke(key, null) as Vec3d;
                            if (at == null) continue;

                            placed++;
                            dimension = (int)Get(key, "Dimension");
                            minX = Math.Min(minX, at.X); maxX = Math.Max(maxX, at.X);
                            minY = Math.Min(minY, at.Y); maxY = Math.Max(maxY, at.Y);
                            minZ = Math.Min(minZ, at.Z); maxZ = Math.Max(maxZ, at.Z);
                        }
                    }
                }

                string where = placed == 0
                    ? "(edges not in graph)"
                    : "from " + Num(minX) + "," + Num(minY) + "," + Num(minZ)
                        + " to " + Num(maxX) + "," + Num(maxY) + "," + Num(maxZ)
                        + (dimension != 0 ? " dim=" + dimension : "");

                Line("owner " + entry.Key + " edges=" + count + " " + where + " " + EntityState((long)entry.Key));
            }
        }

        /// <summary>Every YTT vehicle entity around the caller, with what YTT would make of it.</summary>
        private void Nearby(Vec3d around, int radius)
        {
            Entity[] found = api.World.GetEntitiesAround(around, radius, radius,
                e => e?.Code?.Domain == "yangtransport" && Convoyable(e) != null);

            Array.Sort(found, (a, b) => a.EntityId.CompareTo(b.EntityId));
            Line(found.Length + " vehicles");

            MethodInfo current = authority?.GetType().GetMethod("IsLoadedMaterialAuthorityCurrent", Anywhere);
            IDictionary byEntity = Get(authority, "AuthorityByEntityID") as IDictionary;

            foreach (Entity entity in found)
            {
                string record = byEntity != null && byEntity.Contains(entity.EntityId)
                    ? Get(byEntity[entity.EntityId], "Mode") + " gen=" + Get(byEntity[entity.EntityId], "Generation")
                        + " head=" + Get(byEntity[entity.EntityId], "HeadID")
                    : "no record";

                string trusted = current == null ? "?" : current.Invoke(authority, new object[] { entity })?.ToString();

                Line(entity.EntityId + " " + Vehicle(entity)
                    + " chunkLoaded=" + (api.World.BlockAccessor.GetChunkAtBlockPos(entity.Pos.AsBlockPos) != null)
                    + " | authority: " + record + " trusted=" + trusted
                    + " | attrs: convoyHeadId=" + entity.WatchedAttributes.GetLong("convoyHeadId", 0)
                    + " convoyCount=" + entity.WatchedAttributes.GetInt("convoyCount", 1)
                    + " derailed=" + entity.WatchedAttributes.GetBool("derailed"));
            }
        }

        // ------------------------------------------------------------------------------- helpers

        private void Section(string title, Action body)
        {
            Line("");
            Line("== " + title + " ==");

            try
            {
                body();
            }
            catch (Exception e)
            {
                // The section, not the dump: YTT moved something, and the rest is still worth having.
                Line("(not readable on this YTT version: " + e.GetType().Name + " " + e.Message + ")");
            }
        }

        private void Line(string line) => text.Append(line).Append('\n');

        /// <summary>Loaded or not, and if loaded, where and as what.</summary>
        private string EntityState(long id)
        {
            Entity entity = api.World.GetEntityById(id);
            return entity == null ? "not loaded" : "loaded " + Vehicle(entity);
        }

        /// <summary>One line about a loaded vehicle, read through YTT's public convoy interface.</summary>
        private string Vehicle(Entity entity)
        {
            if (entity == null) return "(null)";

            object vehicle = Convoyable(entity);
            string convoy = vehicle == null
                ? ""
                : " head=" + Get(vehicle, "ConvoyHeadEntityID") + " idx=" + Get(vehicle, "ConvoyOrderIndex")
                    + " expects=" + Get(vehicle, "ExpectedConvoyMemberCount")
                    + " derailed=" + Get(vehicle, "Derailed")
                    + (Truthy(Get(vehicle, "HasTractionEngine")) ? " engine" : "");

            return entity.Code + " @" + Num(entity.Pos.X) + "," + Num(entity.Pos.Y) + "," + Num(entity.Pos.Z)
                + (entity.Pos.Dimension != 0 ? " dim=" + entity.Pos.Dimension : "")
                + " entityGen=" + entity.Attributes.GetLong(GenerationAttribute, 0)
                + " state=" + entity.State + convoy;
        }

        /// <summary>The entity as YTT's convoy interface, or null when it is not a rail vehicle.</summary>
        private static object Convoyable(Entity entity)
        {
            return entity != null && entity.GetType().GetInterface("IRailwayConvoyVehicle") != null ? entity : null;
        }

        private static string Pose(object pose)
        {
            return Num(Get(pose, "X")) + "," + Num(Get(pose, "Y")) + "," + Num(Get(pose, "Z"))
                + " col " + Column(Get(pose, "X")) + "/" + Column(Get(pose, "Z"))
                + ((int)Get(pose, "Dimension") != 0 ? " dim=" + Get(pose, "Dimension") : "");
        }

        private static string Column(object coordinate) => ((int)Math.Floor(Convert.ToDouble(coordinate) / 32.0)).ToString();

        private static string Num(object value)
        {
            return value == null ? "?" : Convert.ToDouble(value).ToString("F1", CultureInfo.InvariantCulture);
        }

        private static bool Truthy(object value) => value is bool b && b;

        /// <summary>
        /// A field or property by name, wherever in the hierarchy it is declared and whatever its
        /// visibility. Interface properties are tried last, since a vehicle implements some of them
        /// explicitly and those do not show up on the class.
        /// </summary>
        private static object Get(object target, string name)
        {
            if (target == null) return null;

            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, Anywhere | BindingFlags.DeclaredOnly);
                if (field != null) return field.GetValue(target);

                PropertyInfo property = type.GetProperty(name, Anywhere | BindingFlags.DeclaredOnly);
                if (property != null && property.GetIndexParameters().Length == 0) return property.GetValue(target);
            }

            foreach (Type contract in target.GetType().GetInterfaces())
            {
                PropertyInfo property = contract.GetProperty(name);
                if (property != null) return property.GetValue(target);
            }

            return null;
        }
    }
}
