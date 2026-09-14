using System;
using System.Collections.Generic;
using System.IO;
using SignalsLink.src;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Common.Entities;
using Vintagestory.Server;
using signals.src;
using signals.src.signalNetwork;
using signals.src.hangingwires;
using System.Linq;
using HarmonyLib;

namespace SignalsLink.src.signals.chunkanchor
{
    /// <summary>
    /// Keeps chunk columns loaded on behalf of the anchors standing in the world.
    ///
    /// A ModSystem rather than the block entity's own business, for two reasons. Columns are
    /// counted, so two anchors whose selections overlap do not release each other's ground. And the
    /// claims are saved: after a restart nothing would ever load the chunk an anchor stands in, so
    /// the anchor could never claim anything - it cannot pull itself up by its own bootstraps.
    ///
    /// The wake schedule lives here too: a sleeping anchor's own
    /// column is unloaded, so its block entity is not ticking and cannot wake itself either.
    /// </summary>
    public class ChunkAnchors : ModSystem
    {
        /// <summary>Never change: saved games hold anchors under this key.</summary>
        public const string SaveKey = "signalslinkChunkAnchors";

        /// <summary>
        /// Marks the saved form that carries a list of columns. The first generation wrote a plain
        /// count of anchors followed by a radius each, so any non-negative first number is one of
        /// those and is read the old way.
        /// </summary>
        private const int ColumnsFormat = -1;

        /// <summary>As above, and each anchor also carries the hour it should next wake at.</summary>
        private const int SleepersFormat = -2;
        private const int NamedFormat = -3;

        /// <summary>As above, and a sleeper also says whether an approaching vehicle wakes it.</summary>
        private const int ArrivalFormat = -4;
        private readonly Dictionary<BlockPos, string> names = new();

        public void SetName(BlockPos pos, string name)
        {
            if (pos == null) return;
            name = AnchorDisplay.CleanName(name);
            if (name.Length == 0) names.Remove(pos);
            else names[pos.Copy()] = name;
        }

        public string NameAt(BlockPos pos) => names.TryGetValue(pos, out string name) ? name : "";
        private string LogLocation(BlockPos pos) => AnchorDisplay.Location(NameAt(pos), pos);

        private ICoreServerAPI sapi;
        private AnchorColumnJobs jobs;
        private SignalNetworkMod signals;
        private HangingWiresMod wires;
        private readonly Queue<(BlockPos Pos, string Why, Sleeper Token)> wakeQueue = new();
        private readonly Dictionary<BlockPos, Sleeper> wakeQueued = new();
        private Harmony wireHooks;
        private readonly List<long> listeners = new();
        private readonly Dictionary<NodePos, List<NodePos>> wirePeers = new();
        private int wireCount = -1;
        private readonly Queue<BlockPos> validateClaims = new();
        private readonly Dictionary<BlockPos, List<NodePos>> wakeInputs = new();

        // What an anchor holds beyond its selection so that no train on its ground is split, and
        // which anchors have such a guest right now. See KeepGroupsWhole.
        private KeepTogetherRegistry keepTogether;
        private readonly Dictionary<BlockPos, HashSet<long>> halos = new();
        private readonly HashSet<BlockPos> guests = new();

        // Who can say a vehicle is coming. See OnApproaching.
        private ArrivalSourceRegistry arrivals;

        public bool ColumnsReady(IEnumerable<long> columns) => columns.All(jobs.IsReady);
        public bool TryCensus(IEnumerable<long> columns, double requestStarted, out AnchorCount result)
        {
            result = default;
            bool complete = true;
            double now = sapi.World.ElapsedMilliseconds / 1000.0;
            foreach (long key in columns)
            {
                if (!jobs.TryGet(key, now, out var count, requestStarted)) complete = false;
                result = new AnchorCount(result.Blocks + count.Blocks, result.Creatures + count.Creatures);
            }
            return complete;
        }

        private IEnumerable<AnchorCount> Scan(long key)
        {
            var (x, z) = AnchorArea.Of(key);
            int blocks = 0, creatures = 0;
            for (int y = 0; y < (sapi.WorldManager.MapSizeY + 31) / 32; y++)
            {
                var chunk = sapi.WorldManager.GetChunk(x, y, z);
                if (chunk == null) yield break;
                blocks += chunk.BlockEntities?.Count ?? 0;
                for (int i = 0, count = chunk.EntitiesCount; i < count; i++)
                {
                    if (i < chunk.EntitiesCount && chunk.Entities?[i] is EntityAgent entity && entity is not EntityPlayer && entity.Alive) creatures++;
                    yield return new AnchorCount(blocks, creatures);
                }
                yield return new AnchorCount(blocks, creatures);
            }
        }

        private bool ColumnReady(long key)
        {
            var (x, z) = AnchorArea.Of(key);
            for (int y = 0; y < (sapi.WorldManager.MapSizeY + 31) / 32; y++)
                if (sapi.WorldManager.GetChunk(x, y, z) == null) return false;
            return true;
        }

        private void ProcessWork(float dt)
        {
            // Old versions could persist claims after a sleeper was broken. Validate only
            // after the anchor's own column has finished loading, with bounded work per tick.
            int checks = Math.Min(8, validateClaims.Count);
            for (int i = 0; i < checks; i++)
            {
                BlockPos pos = validateClaims.Dequeue();
                if (!anchors.ContainsKey(pos)) continue;
                var (x, z) = ColumnAt(pos);
                if (!jobs.IsReady(AnchorArea.Key(x, z))) { validateClaims.Enqueue(pos); continue; }
                if (sapi.World.BlockAccessor.GetBlockEntity(pos) is not BEChunkAnchor) Release(pos);
            }
            // Wires survive unload. Read only live ends, never load a sleeping column to poll it.
            if ((wires?.data?.connections?.Count ?? 0) != wireCount) RebuildWakeInputs();
            foreach (var pair in wakeInputs)
            {
                foreach (NodePos input in pair.Value)
                {
                    var node = signals?.GetDeviceAt(input.blockPos)?.GetNodeAt(input);
                    if (node != null && (node.isSource ? Math.Max(node.output, node.value) : node.value) > 0)
                    { QueueWake(pair.Key, "a signal"); break; }
                }
            }
            for (int n = 0; n < 2 && wakeQueue.Count > 0; n++)
            {
                var next = wakeQueue.Dequeue();
                if (wakeQueued.TryGetValue(next.Pos, out var queued) && ReferenceEquals(queued, next.Token)) wakeQueued.Remove(next.Pos);
                if (sleeping.TryGetValue(next.Pos, out var current) && ReferenceEquals(current, next.Token)) Wake(next.Pos, next.Why);
            }
            jobs.Tick(sapi.World.ElapsedMilliseconds / 1000.0);
        }

        private void RebuildWakeInputs()
        {
            wirePeers.Clear();
            wakeInputs.Clear();
            wireCount = wires?.data?.connections?.Count ?? 0;
            if (wires?.data?.connections == null) return;
            foreach (var wire in wires.data.connections)
            {
                AddPeer(wire.pos1, wire.pos2);
                AddPeer(wire.pos2, wire.pos1);
            }
            foreach (var pos in sleeping.Keys) WatchInput(pos);
        }
        private void AddPeer(NodePos from, NodePos to)
        {
            if (!wirePeers.TryGetValue(from, out var peers)) wirePeers[from] = peers = new();
            if (!peers.Contains(to)) peers.Add(to);
        }
        private void WatchInput(BlockPos pos)
        {
            wakeInputs.Remove(pos);
            if (sleeping.ContainsKey(pos) && wirePeers.TryGetValue(new NodePos(pos, BEChunkAnchor.InputPin), out var peers)) wakeInputs[pos.Copy()] = peers;
        }
        private void QueueWake(BlockPos pos, string why)
        {
            if (pos != null && sleeping.TryGetValue(pos, out var token) && !wakeQueued.ContainsKey(pos))
            { wakeQueued[pos.Copy()] = token; wakeQueue.Enqueue((pos.Copy(), why, token)); }
        }
        private static void WireAdded(SignalNetworkMod __instance, WireConnection __0)
            => __instance.Api?.ModLoader?.GetModSystem<ChunkAnchors>()?.ChangeWire(__0, true);
        private static void WireRemoved(SignalNetworkMod __instance, WireConnection __0)
            => __instance.Api?.ModLoader?.GetModSystem<ChunkAnchors>()?.ChangeWire(__0, false);

        private void ChangeWire(WireConnection wire, bool added)
        {
            if (wireCount < 0) return; // The persisted topology is indexed once after startup.
            if (added) { AddPeer(wire.pos1, wire.pos2); AddPeer(wire.pos2, wire.pos1); }
            else
            {
                if (wirePeers.TryGetValue(wire.pos1, out var first)) first.Remove(wire.pos2);
                if (wirePeers.TryGetValue(wire.pos2, out var second)) second.Remove(wire.pos1);
            }
            wireCount = wires?.data?.connections?.Count ?? 0;
            WatchInput(wire.pos1.blockPos);
            WatchInput(wire.pos2.blockPos);
        }

        public override void Dispose()
        {
            if (sapi != null)
            {
                foreach (long listener in listeners) sapi.Event.UnregisterGameTickListener(listener);
                sapi.Event.SaveGameLoaded -= Restore;
                sapi.Event.GameWorldSave -= Store;
                sapi.Event.PlayerNowPlaying -= OnPlayerArrived;
                if (arrivals != null) arrivals.Approaching -= OnApproaching;
            }
            wireHooks?.UnpatchAll("signalslink.chunkanchor.wires");
            jobs?.Dispose();
            base.Dispose();
        }

        /// <summary>Which columns each anchor holds.</summary>
        private readonly Dictionary<BlockPos, HashSet<long>> anchors = new Dictionary<BlockPos, HashSet<long>>();

        /// <summary>How many anchors want each column. A column at zero is let go.</summary>
        private readonly Dictionary<long, int> held = new Dictionary<long, int>();

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;
            // VS 1.22 UnloadChunkColumn discards dirty chunks. Unpin only, then let the
            // ordinary unload system save them and respect players and normal chunk lifetime.
            var server = api.World as ServerMain
                ?? throw new NotSupportedException("Chunk anchors require the VS server world.");
            jobs = new AnchorColumnJobs(ColumnReady,
                key => { var (x, z) = AnchorArea.Of(key); api.WorldManager.LoadChunkColumn(x, z, true); },
                key => { var (x, z) = AnchorArea.Of(key); server.RemoveChunkColumnFromForceLoadedList(api.WorldManager.MapChunkIndex2D(x, z)); },
                key => Scan(key).GetEnumerator());
            signals = api.ModLoader.GetModSystem<SignalNetworkMod>();
            wires = api.ModLoader.GetModSystem<HangingWiresMod>();
            keepTogether = api.ModLoader.GetModSystem<KeepTogetherRegistry>();
            arrivals = api.ModLoader.GetModSystem<ArrivalSourceRegistry>();
            if (arrivals != null) arrivals.Approaching += OnApproaching;
            wireHooks = new Harmony("signalslink.chunkanchor.wires");
            wireHooks.Patch(AccessTools.Method(typeof(SignalNetworkMod), nameof(SignalNetworkMod.OnWireAdded)),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(ChunkAnchors), nameof(WireAdded))));
            wireHooks.Patch(AccessTools.Method(typeof(SignalNetworkMod), nameof(SignalNetworkMod.OnWireRemoved)),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(ChunkAnchors), nameof(WireRemoved))));

            api.Event.SaveGameLoaded += Restore;
            api.Event.GameWorldSave += Store;

            listeners.Add(api.Event.RegisterGameTickListener(ProcessWork, 100));
            listeners.Add(api.Event.RegisterGameTickListener(WakeSleepers, 5000));
            // Every 5 s: the game unloads a column 12 s after the last player leaves it.
            listeners.Add(api.Event.RegisterGameTickListener(KeepGroupsWhole, 5000));
            api.Event.PlayerNowPlaying += OnPlayerArrived;
        }

        // ---------------------------------------------------------------- keeping groups whole

        /// <summary>
        /// Holds, on top of what each anchor was set to hold, every column of a group that stands
        /// partly on its ground - so a train is never left half loaded when the player walks away
        /// and the anchor's columns are the only ones that stay. The train mod cannot recover from
        /// that; it can recover from a train unloaded whole.
        /// </summary>
        private void KeepGroupsWhole(float dt)
        {
            if (anchors.Count == 0 || keepTogether == null || keepTogether.Sources.Count == 0) return;

            List<IReadOnlyCollection<long>> groups = new();
            foreach (IKeepTogetherSource source in keepTogether.Sources)
            {
                foreach (IReadOnlyCollection<long> group in source.Groups(sapi.World, ColumnKeyOf)) groups.Add(group);
            }

            foreach (KeyValuePair<BlockPos, HashSet<long>> anchor in anchors)
            {
                halos.TryGetValue(anchor.Key, out HashSet<long> before);
                (int cx, int cz) = ColumnAt(anchor.Key);

                HashSet<long> after = AnchorHalo.Compute(anchor.Value, before, groups, cx, cz, AnchorArea.WindowRadius, out int standing);

                if (standing > 0) guests.Add(anchor.Key); else guests.Remove(anchor.Key);

                ApplyHalo(anchor.Key, before, after);
            }
        }

        private void ApplyHalo(BlockPos pos, HashSet<long> before, HashSet<long> after)
        {
            int claimed = 0, released = 0;

            foreach (long key in after)
            {
                if (before != null && before.Contains(key)) continue;
                claimed++;
                Claim(key, census: false);
            }

            if (before != null)
            {
                foreach (long key in before)
                {
                    if (after.Contains(key)) continue;
                    released++;
                    LetGo(key);
                }
            }

            if (after.Count == 0) halos.Remove(pos);
            else halos[pos] = after;

            if (claimed == 0 && released == 0) return;

            sapi.Logger.Notification("[SignalsLink] anchor at " + LogLocation(pos)
                + (after.Count == 0 ? " let its extra columns go; " : " keeps " + after.Count + " extra column(s) so a convoy stays whole; ")
                + held.Count + " held in all.");
        }

        private long ColumnKeyOf(double x, double z)
        {
            int size = sapi?.WorldManager?.ChunkSize ?? 32;

            return AnchorArea.Key((int)Math.Floor(x / size), (int)Math.Floor(z / size));
        }

        /// <summary>The columns held for a guest on top of the anchor's own selection.</summary>
        public IReadOnlyCollection<long> HaloOf(BlockPos pos)
        {
            if (pos != null && halos.TryGetValue(pos, out HashSet<long> halo)) return halo;

            return System.Array.Empty<long>();
        }

        /// <summary>Is a group standing on this anchor's ground? Then it must not let go.</summary>
        public bool HasGuests(BlockPos pos) => pos != null && guests.Contains(pos);

        // ---------------------------------------------------------------- sleeping and waking

        /// <summary>
        /// A sleeping anchor: what it wants to hold, and the in-game hour it should wake at.
        ///
        /// This has to live HERE and not in the block entity, and it is the same reason the claims
        /// do: a sleeping anchor's own column is unloaded, so its block entity is not ticking and
        /// cannot possibly wake itself. Nothing in the world would ever load that chunk again.
        /// </summary>
        private sealed class Sleeper
        {
            public double WakeAtHours;
            public HashSet<long> Columns;

            /// <summary>Also come up when somebody logs in to a server that was standing idle.</summary>
            public bool OnPlayerJoin;

            /// <summary>And when a vehicle is reported heading for its ground.</summary>
            public bool OnArrival;
        }

        /// <summary>The label for the "wake when ... is coming" switch, or empty when nothing can say so.</summary>
        public string ArrivalLangKey => arrivals != null && arrivals.Sources.Count > 0 ? arrivals.Sources[0].LangKey ?? "" : "";

        /// <summary>
        /// A vehicle is on its way. Wake every sleeper that asked for it, whose ground the target
        /// stands on, once the vehicle is near enough - see the AnchorApproachBlocks setting.
        /// </summary>
        private void OnApproaching(Vec3d vehicle, BlockPos target)
        {
            if (sleeping.Count == 0 || vehicle == null || target == null) return;
            if (!ApproachRule.Close(vehicle, target, SignalsLinkConfigLoader.Current.AnchorApproachBlocks)) return;

            long column = ColumnKeyOf(target.X, target.Z);

            due.Clear();

            foreach (KeyValuePair<BlockPos, Sleeper> one in sleeping)
            {
                if (one.Value.OnArrival && ApproachRule.Concerns(one.Value.Columns, column)) due.Add(one.Key);
            }

            foreach (BlockPos pos in due) QueueWake(pos, "a vehicle is on its way to " + target);
        }

        private readonly Dictionary<BlockPos, Sleeper> sleeping = new Dictionary<BlockPos, Sleeper>();

        /// <summary>Lets everything go, but remembers it, and comes back for it at the given hour.</summary>
        public void Sleep(BlockPos pos, IEnumerable<long> columns, double wakeAtHours, bool onPlayerJoin, bool onArrival = false)
        {
            if (sapi == null || pos == null) return;

            HashSet<long> wanted = new HashSet<long>(columns ?? System.Array.Empty<long>());

            Release(pos);

            sleeping[pos.Copy()] = new Sleeper
            {
                WakeAtHours = wakeAtHours,
                Columns = wanted,
                OnPlayerJoin = onPlayerJoin,
                OnArrival = onArrival
            };
            if (wireCount < 0) RebuildWakeInputs(); else WatchInput(pos);

            sapi.Logger.Notification("[SignalsLink] anchor asleep at " + LogLocation(pos) + "; " + wanted.Count
                + " column(s) let go, " + DueText(wakeAtHours, onPlayerJoin, onArrival) + ".");
        }

        /// <summary>When and why it will be back. An infinite hour means only a prompt brings it back.</summary>
        private string DueText(double wakeAtHours, bool onPlayerJoin, bool onArrival)
        {
            if (double.IsPositiveInfinity(wakeAtHours))
            {
                return "back only on a signal" + (onPlayerJoin ? ", when the server wakes up" : "")
                    + (onArrival ? ", or when a vehicle is coming" : "");
            }

            return "back at " + AnchorDisplay.TimeOfDay(wakeAtHours, sapi.World.Calendar.HoursPerDay)
                + " (in " + AnchorDisplay.Duration(wakeAtHours - sapi.World.Calendar.TotalHours) + ")"
                + (onPlayerJoin ? ", or sooner if the server wakes up" : "")
                + (onArrival ? ", or when a vehicle is coming" : "");
        }

        /// <summary>True while this anchor is down for its interval rather than out of charge.</summary>
        public bool IsAsleep(BlockPos pos) => pos != null && sleeping.ContainsKey(pos);

        /// <summary>When it is next due up, or null when it is not asleep.</summary>
        public double? WakesAt(BlockPos pos)
        {
            return pos != null && sleeping.TryGetValue(pos, out Sleeper one) ? one.WakeAtHours : null;
        }

        /// <summary>Brings it back now - what a signal on the input pin amounts to.</summary>
        public void WakeNow(BlockPos pos) => QueueWake(pos, "a signal");
        public void UpdateSleep(BlockPos pos, IEnumerable<long> columns, double when, bool onJoin, bool onArrival = false)
        {
            if (sleeping.TryGetValue(pos, out var one))
            {
                sleeping[pos.Copy()] = new Sleeper { Columns = new HashSet<long>(columns), WakeAtHours = when, OnPlayerJoin = onJoin, OnArrival = onArrival };
                wakeQueued.Remove(pos);

                // Said in the log like the original sleep was, or the last line about this anchor
                // keeps promising a wake-up that is no longer coming.
                sapi.Logger.Notification("[SignalsLink] anchor asleep at " + LogLocation(pos) + " now "
                    + DueText(when, onJoin, onArrival) + ".");
            }
        }

        /// <summary>
        /// Wakes a sleeper and says WHY in the log.
        ///
        /// The reason is the point: on a running server the only way to tell a working wake cycle
        /// from an anchor that woke by accident - or never woke at all - is to read the pairs off
        /// the log. "Asleep at X / awake at X" with a reason each is a thing you can grep.
        /// </summary>
        private void Wake(BlockPos pos, string why)
        {
            if (pos == null || !sleeping.TryGetValue(pos, out Sleeper one)) return;

            sleeping.Remove(pos);
            wakeInputs.Remove(pos);
            wakeQueued.Remove(pos);

            sapi.Logger.Notification("[SignalsLink] anchor awake at " + LogLocation(pos) + "; " + why
                + " at " + AnchorDisplay.TimeOfDay(sapi.World.Calendar.TotalHours, sapi.World.Calendar.HoursPerDay)
                + ", taking back " + one.Columns.Count + " column(s).");

            if (sapi.World.BlockAccessor.GetBlockEntity(pos) is BEChunkAnchor anchor) anchor.WakeFromSchedule();
            else SetColumns(pos, one.Columns);
        }

        /// <summary>
        /// Somebody has arrived on a server that was standing empty.
        ///
        /// While no one is on, the server suspends its ticks - so no interval elapses, no factory
        /// runs, and an anchor that went to sleep last night is still asleep. Waking on arrival is
        /// what makes the world look like it kept going, and it is settable per anchor because a
        /// player who wants a factory to stay down until its train comes should get that too.
        /// </summary>
        private void OnPlayerArrived(IServerPlayer player)
        {
            if (sleeping.Count == 0) return;
            if (sapi.World.AllOnlinePlayers.Length > 1) return;

            due.Clear();

            foreach (KeyValuePair<BlockPos, Sleeper> one in sleeping)
            {
                if (one.Value.OnPlayerJoin) due.Add(one.Key);
            }

            foreach (BlockPos pos in due) QueueWake(pos, "the server came back to life");
        }

        private readonly List<BlockPos> due = new List<BlockPos>();

        private void WakeSleepers(float dt)
        {
            if (sleeping.Count == 0) return;

            double now = sapi.World.Calendar.TotalHours;

            due.Clear();

            foreach (KeyValuePair<BlockPos, Sleeper> one in sleeping)
            {
                if (one.Value.WakeAtHours <= now) due.Add(one.Key);
            }

            // Claiming the columns loads the chunks, which brings the block entity up, which is
            // what actually starts the anchor working again.
            foreach (BlockPos pos in due) QueueWake(pos, "its interval elapsed");
        }

        /// <summary>The columns this anchor holds, or an empty set when it holds none.</summary>
        public IReadOnlyCollection<long> ColumnsOf(BlockPos pos)
        {
            if (pos != null && anchors.TryGetValue(pos, out HashSet<long> columns)) return columns;

            return System.Array.Empty<long>();
        }

        /// <summary>
        /// Where an anchor stands, in column coordinates. Floor, not integer division: the latter
        /// rounds towards zero, so everything west or north of the origin would land one column
        /// off - and disagree with the map, which floors.
        /// </summary>
        public (int X, int Z) ColumnAt(BlockPos pos)
        {
            int size = sapi?.WorldManager?.ChunkSize ?? 32;

            return ((int)Math.Floor((double)pos.X / size), (int)Math.Floor((double)pos.Z / size));
        }

        /// <summary>
        /// Sets what an anchor holds, loading what is newly wanted and letting go of what is not.
        ///
        /// One method for claiming, changing and re-claiming after a restart, because they are the
        /// same thing: only the difference against what is held now is acted on. Claiming twice
        /// therefore costs nothing, which is what makes it safe to call from Initialize.
        /// </summary>
        public void SetColumns(BlockPos pos, IEnumerable<long> wanted)
        {
            if (sapi == null || pos == null) return;

            (int cx, int cz) = ColumnAt(pos);

            HashSet<long> after = AnchorArea.Sanitise(wanted, cx, cz,
                AnchorArea.WindowRadius, SignalsLinkConfigLoader.Current.AnchorMaxColumns);

            if (!anchors.TryGetValue(pos, out HashSet<long> before))
            {
                before = new HashSet<long>();
                anchors[pos.Copy()] = before;
                validateClaims.Enqueue(pos.Copy());
            }

            int claimed = 0, released = 0;

            foreach (long key in after)
            {
                if (before.Contains(key)) continue;

                claimed++;
                Claim(key);
            }

            foreach (long key in before)
            {
                if (after.Contains(key)) continue;

                released++;
                LetGo(key);
            }

            before.Clear();
            foreach (long key in after) before.Add(key);

            // An anchor is claimed twice on every start - once from the saved list, so that its own
            // chunk loads at all, and again when its block entity comes up in that chunk. The
            // second one is a no-op by design, and saying so in the log only makes it look like
            // something happened twice.
            if (claimed == 0 && released == 0) return;

            sapi.Logger.Notification("[SignalsLink] anchor at " + LogLocation(pos) + " holds "
                + after.Count + " chunk column(s); " + held.Count + " held in all.");
        }

        public void Release(BlockPos pos)
        {
            if (pos == null) return;
            sleeping.Remove(pos);
            wakeInputs.Remove(pos);
            wakeQueued.Remove(pos);
            if (sapi == null || pos == null || !anchors.TryGetValue(pos, out HashSet<long> columns)) return;

            anchors.Remove(pos);
            guests.Remove(pos);

            foreach (long key in columns) LetGo(key);

            // Whatever was held for a guest goes with it, in the same breath - a group let go in
            // two halves is exactly what all of this exists to prevent.
            if (halos.Remove(pos, out HashSet<long> halo))
            {
                foreach (long key in halo) LetGo(key);
            }

            sapi.Logger.Notification("[SignalsLink] anchor at " + LogLocation(pos) + " let go; "
                + held.Count + " columns still held.");
        }

        /// <summary>Everything held by every anchor.</summary>
        public int HeldColumns => held.Count;

        private void Claim(long key, bool census = true)
        {
            held.TryGetValue(key, out int count);
            held[key] = count + 1;

            if (count == 0) jobs.Retain(key, census);
        }

        private void LetGo(long key)
        {
            if (!held.TryGetValue(key, out int count)) return;

            if (count > 1)
            {
                held[key] = count - 1;
                return;
            }

            held.Remove(key);

            jobs.LetGo(key);
        }

        // ---------------------------------------------------------------- across a restart

        private void Store()
        {
            using MemoryStream buffer = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(buffer);

            // Sleepers are saved with everything else. Without that a restart would leave an
            // anchor asleep with nothing left in the world that knows to come back for it.
            writer.Write(ArrivalFormat);
            writer.Write(anchors.Count + sleeping.Count);

            foreach (KeyValuePair<BlockPos, HashSet<long>> anchor in anchors)
            {
                WriteOne(writer, anchor.Key, anchor.Value, double.NegativeInfinity, false, false, NameAt(anchor.Key));
            }

            foreach (KeyValuePair<BlockPos, Sleeper> one in sleeping)
            {
                WriteOne(writer, one.Key, one.Value.Columns, one.Value.WakeAtHours, one.Value.OnPlayerJoin, one.Value.OnArrival, NameAt(one.Key));
            }

            sapi.WorldManager.SaveGame.StoreData(SaveKey, buffer.ToArray());
        }

        /// <summary>An hour of negative infinity means "awake"; anything else is when to wake it.</summary>
        private static void WriteOne(BinaryWriter writer, BlockPos pos, HashSet<long> columns,
            double wakeAt, bool onPlayerJoin, bool onArrival, string name)
        {
            writer.Write(pos.X);
            writer.Write(pos.Y);
            writer.Write(pos.Z);
            writer.Write(wakeAt);
            writer.Write(onPlayerJoin);
            writer.Write(onArrival);
            writer.Write(name);

            writer.Write(columns.Count);
            foreach (long key in columns) writer.Write(key);
        }

        /// <summary>
        /// Claims again what was claimed before the server stopped. Done from the saved list and
        /// not from the blocks, because the blocks are in chunks nobody has asked for yet.
        /// </summary>
        private void Restore()
        {
            byte[] data = sapi.WorldManager.SaveGame.GetData(SaveKey);
            if (data == null || data.Length == 0) return;

            using MemoryStream buffer = new MemoryStream(data);
            using BinaryReader reader = new BinaryReader(buffer);

            List<(BlockPos Pos, HashSet<long> Columns)> saved;

            try
            {
                int first = reader.ReadInt32();

                saved = first switch
                {
                    ArrivalFormat => ReadSleepers(reader, true, true),
                    NamedFormat => ReadSleepers(reader, true),
                    SleepersFormat => ReadSleepers(reader),
                    ColumnsFormat => ReadColumns(reader),
                    _ => ReadSquares(reader, first)
                };
            }
            catch (EndOfStreamException)
            {
                // Half a record is worth nothing, and throwing here would stop the world loading.
                sapi.Logger.Warning("[SignalsLink] the saved chunk anchors are cut short; what could be read is kept.");
                return;
            }

            // Once the world is actually running: claiming during load is too early for the chunk
            // loader to answer.
            sapi.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () =>
            {
                foreach ((BlockPos pos, HashSet<long> columns) in saved) SetColumns(pos, columns);
                RebuildWakeInputs();
            });
        }

        /// <summary>
        /// Reads the form that knows about sleeping. A sleeper is not claimed now - it is put back
        /// on the schedule, so a world that stops overnight does not wake every sleeping factory
        /// the moment it starts again.
        /// </summary>
        private List<(BlockPos, HashSet<long>)> ReadSleepers(BinaryReader reader, bool named = false, bool arrival = false)
        {
            List<(BlockPos, HashSet<long>)> awake = new List<(BlockPos, HashSet<long>)>();

            int count = reader.ReadInt32();

            for (int i = 0; i < count; i++)
            {
                BlockPos pos = new BlockPos(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                double wakeAt = reader.ReadDouble();
                bool onPlayerJoin = reader.ReadBoolean();
                bool onArrival = arrival && reader.ReadBoolean();
                if (named) SetName(pos, reader.ReadString());

                HashSet<long> columns = new HashSet<long>();
                int columnCount = reader.ReadInt32();

                for (int c = 0; c < columnCount; c++) columns.Add(reader.ReadInt64());

                if (double.IsNegativeInfinity(wakeAt)) awake.Add((pos, columns));
                else sleeping[pos] = new Sleeper
                {
                    WakeAtHours = wakeAt,
                    Columns = columns,
                    OnPlayerJoin = onPlayerJoin,
                    OnArrival = onArrival
                };
            }

            return awake;
        }

        private static List<(BlockPos, HashSet<long>)> ReadColumns(BinaryReader reader)
        {
            List<(BlockPos, HashSet<long>)> saved = new List<(BlockPos, HashSet<long>)>();

            int count = reader.ReadInt32();

            for (int i = 0; i < count; i++)
            {
                BlockPos pos = new BlockPos(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());

                HashSet<long> columns = new HashSet<long>();
                int columnCount = reader.ReadInt32();

                for (int c = 0; c < columnCount; c++) columns.Add(reader.ReadInt64());

                saved.Add((pos, columns));
            }

            return saved;
        }

        /// <summary>
        /// The first generation's form: a radius per anchor, meaning a filled square. Read rather
        /// than discarded so an anchor already standing in a world goes on holding what it held.
        /// </summary>
        private List<(BlockPos, HashSet<long>)> ReadSquares(BinaryReader reader, int count)
        {
            List<(BlockPos, HashSet<long>)> saved = new List<(BlockPos, HashSet<long>)>();
            int size = sapi.WorldManager.ChunkSize;

            for (int i = 0; i < count; i++)
            {
                BlockPos pos = new BlockPos(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                int radius = reader.ReadInt32();

                saved.Add((pos, AnchorArea.Square(
                    (int)Math.Floor((double)pos.X / size), (int)Math.Floor((double)pos.Z / size), radius)));
            }

            return saved;
        }
    }
}
