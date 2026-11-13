using System;
using System.Collections.Generic;
using SignalsLink.src.signals.vehicle;
using SignalsLink.YTT.src.stoker;
using SignalsLink.YTT.src.train;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.Server;

namespace SignalsLink.YTT.src.taxi
{
    /// <summary>
    /// Runs the taxi rides, server side: answers a ticket, gives the conductor his orders, and
    /// once a second looks at every loaded locomotive with a ride on it to see what comes next.
    /// Off a train the ticket summons one: the nearest fit train, loaded or off-screen, is
    /// brought to the nearest station and waits there for the caller.
    ///
    /// The conductor does the driving. This only writes his timetable - one stop: there, and
    /// stay - watches his action code, and puts the timetable back when the ride is over.
    /// </summary>
    public sealed class TaxiDispatcher
    {
        /// <summary>After the ticket holder got off, before the train leaves. Real time.</summary>
        public const long AlightGraceMs = 15000;

        /// <summary>How long to wait for the reach index before offering every station.</summary>
        public const long ReachPatienceMs = 3000;

        /// <summary>Stuck for this long: tell the holder.</summary>
        public const long StuckReportMs = 60000;

        public const int ActionGoing = 1;
        public const int ActionWaitingAtStation = 2;
        public const int ActionStuck = 3;
        public const int ActionNoTimetable = 5;

        private readonly ICoreServerAPI api;
        private readonly YttFire fire;
        private readonly YttStations stations;
        private readonly VirtualTrains virtuals;
        private readonly IServerNetworkChannel channel;
        private readonly StandingWatch standing = new StandingWatch();
        private readonly List<PendingReach> pending = new List<PendingReach>();
        private readonly Dictionary<string, Summon> summons = new Dictionary<string, Summon>();
        private long listener;

        private sealed class PendingReach
        {
            public string PlayerUid;
            public long LocomotiveId;
            public long Deadline;
        }

        private sealed class Seat
        {
            public Entity Vehicle;
            public long Head;
            public Entity Locomotive;
        }

        /// <summary>One player's call for a train, from the first candidate to the train at the pickup.</summary>
        private sealed class Summon
        {
            public string PlayerUid;
            public StationInfo Station;
            public bool WaitForMe;
            public readonly Queue<TrainCandidate> Queue = new Queue<TrainCandidate>();
            public int VirtualAttempts;

            /// <summary>The off-screen train being materialised right now, or null.</summary>
            public TrainCandidate Materialising;
            public long MaterialiseDeadline;

            /// <summary>Columns this summon keeps loaded, and since when.</summary>
            public readonly HashSet<(int x, int z)> Held = new HashSet<(int, int)>();
            public long HeldSince;

            /// <summary>Once a train took the order.</summary>
            public long LocomotiveId;
            public double OrderedX, OrderedZ;
        }

        public TaxiDispatcher(ICoreServerAPI api, YttFire fire, YttStations stations, VirtualTrains virtuals, IServerNetworkChannel channel)
        {
            this.api = api;
            this.fire = fire;
            this.stations = stations;
            this.virtuals = virtuals;
            this.channel = channel;
        }

        public void Start()
        {
            if (listener == 0) listener = api.Event.RegisterGameTickListener(Tick, 1000);
        }

        public void Stop()
        {
            if (listener != 0) api.Event.UnregisterGameTickListener(listener);
            listener = 0;

            foreach (Summon summon in summons.Values) ReleaseHeld(summon);
            summons.Clear();
        }

        // ------------------------------------------------------------------ the ticket is used

        public void Open(IServerPlayer player, TaxiOpenRequest _)
        {
            if (player == null) return;

            if (!stations.Available)
            {
                Send(player, new TaxiStationsPacket { Error = "unavailable" });
                return;
            }

            if (!TrySeat(player, out Seat seat, out string error))
            {
                // Off a train the ticket calls one; "no engine" and "no conductor" are a train's faults.
                if (error == "notintrain") OfferSummon(player);
                else Send(player, new TaxiStationsPacket { Error = error });
                return;
            }

            Send(player, Describe(seat, allowPending: true, player));
        }

        private TaxiStationsPacket Describe(Seat seat, bool allowPending, IServerPlayer player)
        {
            Entity locomotive = seat.Locomotive;
            List<StationInfo> offered = StationsFor(locomotive, allowPending, player, out int reach);

            TaxiStationsPacket packet = new TaxiStationsPacket
            {
                LocomotiveId = locomotive.EntityId,
                Names = new string[offered.Count],
                Hashes = new long[offered.Count],
                Dimensions = new int[offered.Count],
                Reach = reach,
            };

            for (int i = 0; i < offered.Count; i++)
            {
                packet.Names[i] = offered[i].Name;
                packet.Hashes[i] = unchecked((long)offered[i].NameHash);
                packet.Dimensions[i] = offered[i].Dimension;
            }

            TaxiJob job = TaxiJob.Load(locomotive);
            if (job != null && job.Phase != TaxiPhase.AwaitingDestination && job.Phase != TaxiPhase.AtPickup)
            {
                packet.HasRide = true;
                packet.ActiveHash = unchecked((long)job.DestinationHash);
                packet.ActiveWait = job.Wait;
                packet.ActiveName = job.DestinationName;
            }

            return packet;
        }

        /// <summary>
        /// The stations this train can go to, by name. Reachability when the other mod has its
        /// index ready; while it is still building it, every station of the gauge and a promise
        /// to send the real list once it is known.
        /// </summary>
        private List<StationInfo> StationsFor(Entity locomotive, bool allowPending, IServerPlayer player, out int reach)
        {
            int dimension = locomotive.Pos.Dimension;
            List<StationInfo> all = new List<StationInfo>();

            foreach (StationInfo station in stations.All(GaugeOf(locomotive)))
            {
                if (station.Dimension == dimension) all.Add(station);
            }

            ReachStatus status = stations.Reachable(locomotive, out HashSet<(ulong, int)> reachable);
            List<StationInfo> offered = all;

            if (status == ReachStatus.Ready)
            {
                offered = all.FindAll(s => reachable.Contains((s.NameHash, s.Dimension)));
                reach = 0;
            }
            else if (status == ReachStatus.Pending && allowPending && player != null)
            {
                pending.Add(new PendingReach { PlayerUid = player.PlayerUID, LocomotiveId = locomotive.EntityId, Deadline = api.World.ElapsedMilliseconds + ReachPatienceMs });
                reach = 1;
            }
            else
            {
                reach = 2;
            }

            offered.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            return offered;
        }

        public void Order(IServerPlayer player, TaxiOrderPacket order)
        {
            if (player == null || order == null) return;
            if (!TrySeat(player, out Seat seat, out string error)) { Message(player, "error-" + error); return; }
            if (seat.Locomotive.EntityId != order.LocomotiveId) { Message(player, "error-notintrain"); return; }

            Entity locomotive = seat.Locomotive;
            StationInfo destination = Find(locomotive, unchecked((ulong)order.Hash), order.Dimension);
            if (destination == null) { Message(player, "error-nostation"); return; }

            TaxiJob job = TaxiJob.Load(locomotive);

            if (job == null)
            {
                job = NewJob(locomotive, seat.Head);

                // A cold boiler is lit at the passenger's expense, and only if it can be.
                if (!job.SavedBurning)
                {
                    if (!fire.Available) { Message(player, "error-unavailable"); return; }

                    string lit = FireKeeping.TryIgnite(fire, locomotive, () => LighterOf(player), api.World, locomotive.Pos.XYZ);
                    if (lit != "lit") { Message(player, "ignite-" + lit); return; }
                }
            }

            job.DestinationName = destination.Name;
            job.DestinationHash = destination.NameHash;
            job.DestinationDimension = destination.Dimension;
            job.Wait = order.Wait;
            job.HolderUid = player.PlayerUID;
            job.Phase = TaxiPhase.Going;
            job.Stopping = false;
            job.HolderLeftAtMs = -1;
            job.StuckSinceMs = 0;
            job.StuckReported = false;

            Timetable.WriteSingle(locomotive, destination);
            job.Save(locomotive);

            Message(player, "going", destination.Name);
        }

        /// <summary>What a train was doing before a ride: set aside, to be put back after it.</summary>
        private TaxiJob NewJob(Entity locomotive, long head)
        {
            TaxiJob job = new TaxiJob
            {
                ConvoyHead = head,
                SavedTimetable = Timetable.Snapshot(locomotive),
                SavedBurning = fire.IsBurning(locomotive) == true,
                OriginX = locomotive.Pos.X,
                OriginY = locomotive.Pos.Y,
                OriginZ = locomotive.Pos.Z,
            };

            StationInfo back = Nearest(locomotive);
            if (back != null)
            {
                job.ReturnName = back.Name;
                job.ReturnHash = back.NameHash;
                job.ReturnDimension = back.Dimension;
                job.ReturnX = back.X;
                job.ReturnY = back.Y;
                job.ReturnZ = back.Z;
                job.ReturnGauge = back.Gauge;
            }

            return job;
        }

        public void StopHere(IServerPlayer player, TaxiStopPacket stop)
        {
            if (player == null || stop == null) return;
            if (!TrySeat(player, out Seat seat, out string error)) { Message(player, "error-" + error); return; }

            Entity locomotive = seat.Locomotive;
            TaxiJob job = TaxiJob.Load(locomotive);
            if (job == null || locomotive.EntityId != stop.LocomotiveId) { Message(player, "error-noride"); return; }

            job.Wait = stop.Wait;
            job.Stopping = true;
            job.HolderUid = player.PlayerUID;
            job.Phase = TaxiPhase.Going;
            job.HolderLeftAtMs = -1;

            // A conductor without a timetable brakes and holds. That is the whole trick.
            Timetable.Clear(locomotive);
            job.Save(locomotive);

            Message(player, "stopping");
        }

        // ------------------------------------------------------------------ summoning

        /// <summary>Off a train: the nearest station within reach, offered as the place to call a train to.</summary>
        private void OfferSummon(IServerPlayer player)
        {
            TaxiSummonOfferPacket offer = new TaxiSummonOfferPacket { HasSummon = summons.ContainsKey(player.PlayerUID) };
            StationInfo station = NearestStation(player.Entity.Pos.X, player.Entity.Pos.Z, player.Entity.Pos.Dimension, SummonRules.StationReach, null);

            if (station == null) offer.Error = "nostationnear";
            else
            {
                offer.StationName = station.Name;
                offer.Hash = unchecked((long)station.NameHash);
                offer.Dimension = station.Dimension;
                offer.Gauge = station.Gauge;
            }

            channel?.SendPacket(offer, player);
        }

        public void SummonTrain(IServerPlayer player, TaxiSummonPacket packet)
        {
            if (player == null || packet == null) return;

            if (summons.TryGetValue(player.PlayerUID, out Summon running))
            {
                CancelSummon(running);
                Message(player, "summon-cancelled");
            }

            if (packet.Cancel) return;

            StationInfo station = null;
            foreach (StationInfo candidate in stations.All((byte)packet.Gauge))
            {
                if (candidate.NameHash == unchecked((ulong)packet.Hash) && candidate.Dimension == packet.Dimension) { station = candidate; break; }
            }

            if (station == null) { Message(player, "error-nostation"); return; }

            List<TrainCandidate> candidates = LoadedCandidates(station.Dimension);
            candidates.AddRange(virtuals.Candidates(station.Dimension));

            int limit = SignalsLink.src.SignalsLinkConfigLoader.Current?.TaxiSummonMaxBlocks ?? 2000;
            List<TrainCandidate> picked = SummonRules.Pick(candidates, station.X + 0.5, station.Z + 0.5, station.Dimension, station.Gauge, limit);

            if (picked.Count == 0) { Message(player, "summon-none"); return; }

            Summon summon = new Summon { PlayerUid = player.PlayerUID, Station = station, WaitForMe = packet.WaitForMe };
            foreach (TrainCandidate train in picked) summon.Queue.Enqueue(train);
            summons[player.PlayerUID] = summon;

            TryNext(summon);
        }

        /// <summary>Every loaded convoy as a candidate, in the given dimension.</summary>
        private List<TrainCandidate> LoadedCandidates(int dimension)
        {
            Dictionary<long, List<Entity>> convoys = new Dictionary<long, List<Entity>>();

            foreach (Entity entity in api.World.LoadedEntities.Values)
            {
                if (entity?.Code?.Domain != TrainCargoHolderFinder.Domain || entity.Pos.Dimension != dimension) continue;

                long head = LocomotiveFinder.ConvoyOf(entity);
                if (!convoys.TryGetValue(head, out List<Entity> members)) convoys[head] = members = new List<Entity>();
                members.Add(entity);
            }

            HashSet<long> withPlayers = new HashSet<long>();
            foreach (IPlayer player in api.World.AllOnlinePlayers)
            {
                IMountableSeat mount = player?.Entity?.MountedOn;
                Entity vehicle = mount?.Entity ?? mount?.MountSupplier?.OnEntity;
                if (vehicle?.Code?.Domain == TrainCargoHolderFinder.Domain) withPlayers.Add(LocomotiveFinder.ConvoyOf(vehicle));
            }

            List<TrainCandidate> candidates = new List<TrainCandidate>();

            foreach (KeyValuePair<long, List<Entity>> convoy in convoys)
            {
                Entity engine = null;
                bool passenger = false, conductor = false;

                foreach (Entity member in convoy.Value)
                {
                    if (LocomotiveFinder.HasEngine(member)) engine ??= member;
                    if (SummonRules.IsPassengerCar(member.Code.Path)) passenger = true;
                    if (TrainSignals.HasConductorLocust(member)) conductor = true;
                }

                if (engine == null) continue;

                bool stocked = !fire.Available
                    || (fire.FuelSlotOf(engine)?.Empty == false && fire.WaterSlotOf(engine)?.Empty == false);

                candidates.Add(new TrainCandidate
                {
                    Head = convoy.Key,
                    Virtual = false,
                    X = engine.Pos.X,
                    Z = engine.Pos.Z,
                    Dimension = dimension,
                    Gauge = GaugeOf(engine),
                    HasPassengerCar = passenger,
                    HasConductor = conductor,
                    HasEngine = true,
                    HasPlayers = withPlayers.Contains(convoy.Key),
                    HasFuelAndWater = stocked,
                    OnTaxiRide = TaxiJob.Exists(engine) && TaxiJob.LoadFree(engine) == null,
                });
            }

            return candidates;
        }

        /// <summary>The next candidate: a loaded one is ordered at once, an off-screen one is first materialised.</summary>
        private void TryNext(Summon summon)
        {
            long now = api.World.ElapsedMilliseconds;

            while (summon.Queue.Count > 0)
            {
                TrainCandidate train = summon.Queue.Dequeue();

                if (!train.Virtual)
                {
                    Entity locomotive = LocomotiveFinder.LocomotiveOfConvoy(api.World, train.Head);
                    bool free = locomotive != null && (!TaxiJob.Exists(locomotive) || TaxiJob.LoadFree(locomotive) != null);
                    if (free && StartRide(summon, locomotive, train)) return;
                    continue;
                }

                if (summon.VirtualAttempts >= SummonRules.MaxAttempts) continue;

                // Hold its columns; the other mod materialises the train on its own once they are loaded.
                summon.VirtualAttempts++;
                summon.Materialising = train;
                summon.MaterialiseDeadline = now + SummonRules.MaterialisePatienceMs;
                Hold(summon, train.Columns, now);

                if (summon.VirtualAttempts == 1) Message(summon.PlayerUid, "summon-searching");
                return;
            }

            ReleaseHeld(summon);
            summons.Remove(summon.PlayerUid);
            Message(summon.PlayerUid, "summon-none");
        }

        /// <summary>The train is loaded: give the conductor the pickup station and tell the caller.</summary>
        private bool StartRide(Summon summon, Entity locomotive, TrainCandidate train)
        {
            IServerPlayer player = api.World.PlayerByUid(summon.PlayerUid) as IServerPlayer;
            if (player == null) return false;

            // A train on its way back keeps what it had set aside; its boiler is burning already.
            TaxiJob job = TaxiJob.LoadFree(locomotive);
            if (job != null) job.Reuse();
            else job = NewJob(locomotive, LocomotiveFinder.ConvoyOf(locomotive));

            if (fire.IsBurning(locomotive) != true)
            {
                if (!fire.Available) { Message(player, "error-unavailable"); return false; }

                string lit = FireKeeping.TryIgnite(fire, locomotive, () => LighterOf(player), api.World, locomotive.Pos.XYZ);
                if (lit != "lit") { Message(player, "ignite-" + lit); return false; }
            }

            job.DestinationName = summon.Station.Name;
            job.DestinationHash = summon.Station.NameHash;
            job.DestinationDimension = summon.Station.Dimension;
            job.HolderUid = summon.PlayerUid;
            job.WaitForMe = summon.WaitForMe;
            job.Phase = TaxiPhase.Summoned;

            Timetable.WriteSingle(locomotive, summon.Station);
            job.Save(locomotive);

            summon.Materialising = null;
            summon.LocomotiveId = locomotive.EntityId;
            summon.OrderedX = locomotive.Pos.X;
            summon.OrderedZ = locomotive.Pos.Z;

            double distance = SummonRules.Distance(locomotive.Pos.X, locomotive.Pos.Z, summon.Station.X + 0.5, summon.Station.Z + 0.5);
            Message(player, "summon-going", summon.Station.Name, SummonRules.EtaSeconds(distance, train?.SpeedBps ?? 0));
            return true;
        }

        private void CancelSummon(Summon summon)
        {
            ReleaseHeld(summon);
            summons.Remove(summon.PlayerUid);

            Entity locomotive = summon.LocomotiveId != 0 ? api.World.GetEntityById(summon.LocomotiveId) : null;
            TaxiJob job = locomotive == null ? null : TaxiJob.Load(locomotive);
            if (job == null || job.HolderUid != summon.PlayerUid) return;
            if (job.Phase != TaxiPhase.Summoned && job.Phase != TaxiPhase.AtPickup && job.Phase != TaxiPhase.AwaitingDestination) return;

            Timetable.Restore(locomotive, job.SavedTimetable);
            TaxiJob.Remove(locomotive);
            RestoreBoiler(locomotive, job);
        }

        private void Hold(Summon summon, IReadOnlyList<(int x, int z)> columns, long now)
        {
            foreach ((int x, int z) in columns)
            {
                if (summon.Held.Add((x, z))) api.WorldManager.LoadChunkColumn(x, z, true);
            }

            summon.HeldSince = now;
        }

        /// <summary>
        /// Unpin only, as the anchor does: UnloadChunkColumn would discard dirty chunks. The
        /// ordinary unload system saves them and respects players and chunk lifetime.
        /// </summary>
        private void ReleaseHeld(Summon summon)
        {
            if (summon.Held.Count == 0) return;

            if (api.World is ServerMain server)
            {
                foreach ((int x, int z) in summon.Held) server.RemoveChunkColumnFromForceLoadedList(api.WorldManager.MapChunkIndex2D(x, z));
            }

            summon.Held.Clear();
        }

        private void TickSummons(long now)
        {
            List<Summon> all = new List<Summon>(summons.Values);

            foreach (Summon summon in all)
            {
                if (summon.Materialising != null)
                {
                    Entity locomotive = LocomotiveFinder.LocomotiveOfConvoy(api.World, summon.Materialising.Head);

                    if (locomotive != null && !TaxiJob.Exists(locomotive))
                    {
                        TrainCandidate train = summon.Materialising;
                        if (!StartRide(summon, locomotive, train)) { ReleaseHeld(summon); summon.Materialising = null; TryNext(summon); }
                    }
                    else if (now >= summon.MaterialiseDeadline)
                    {
                        ReleaseHeld(summon);
                        summon.Materialising = null;
                        TryNext(summon);
                    }

                    continue;
                }

                if (summon.LocomotiveId == 0) continue;

                // Let go of the columns once the train is under way, or after a minute regardless.
                if (summon.Held.Count > 0)
                {
                    Entity locomotive = api.World.GetEntityById(summon.LocomotiveId);
                    bool moved = locomotive != null && SummonRules.Distance(locomotive.Pos.X, locomotive.Pos.Z, summon.OrderedX, summon.OrderedZ) > 2;

                    if (moved || now - summon.HeldSince > SummonRules.HoldColumnsMs) ReleaseHeld(summon);
                }
            }
        }

        /// <summary>A summon is over once its ride is: forget it, and let its columns go.</summary>
        private void ForgetSummonOf(string playerUid)
        {
            if (string.IsNullOrEmpty(playerUid) || !summons.TryGetValue(playerUid, out Summon summon)) return;

            ReleaseHeld(summon);
            summons.Remove(playerUid);
        }

        // ------------------------------------------------------------------ every second

        private void Tick(float dt)
        {
            long now = api.World.ElapsedMilliseconds;

            try
            {
                standing.Forget(now);

                foreach (Entity entity in api.World.LoadedEntities.Values)
                {
                    if (entity?.Code?.Domain != TrainCargoHolderFinder.Domain || !LocomotiveFinder.HasEngine(entity)) continue;
                    if (!TaxiJob.Exists(entity)) continue;

                    Advance(entity, now);
                }

                TickSummons(now);
                AnswerPending(now);
            }
            catch (Exception e)
            {
                // Whatever broke, the server tick goes on; the next second tries again.
                api.Logger.Error("[SignalsLink.YTT] the train taxi tick failed: " + e);
            }
        }

        private void Advance(Entity locomotive, long now)
        {
            TaxiJob job = TaxiJob.Load(locomotive);
            if (job == null) return;

            if (!ConvoyHasConductor(job.ConvoyHead))
            {
                Timetable.Restore(locomotive, job.SavedTimetable);
                TaxiJob.Remove(locomotive);
                ForgetSummonOf(job.HolderUid);
                Message(job.HolderUid, "aborted-nolocust");
                return;
            }

            bool still = standing.IsStanding(locomotive.EntityId, locomotive.Pos.X, locomotive.Pos.Y, locomotive.Pos.Z, now);
            int action = locomotive.WatchedAttributes?.GetInt(TrainSignals.ActionAttribute, 0) ?? 0;
            bool atDestination = action == ActionWaitingAtStation && still && Timetable.CurrentDestinationHash(locomotive) == job.DestinationHash;

            switch (job.Phase)
            {
                case TaxiPhase.Going:
                    WatchStuck(job, action, now);

                    bool arrived = job.Stopping ? action == ActionNoTimetable && still : atDestination;

                    if (!arrived) { job.Save(locomotive); return; }

                    if (job.Wait) { FinishWaiting(locomotive, job); return; }

                    job.Phase = TaxiPhase.Alighting;
                    job.HolderLeftAtMs = -1;
                    job.Save(locomotive);
                    Message(job.HolderUid, "arrived");
                    return;

                case TaxiPhase.Alighting:
                    if (HolderAboard(job)) job.HolderLeftAtMs = -1;
                    else if (job.HolderLeftAtMs < 0 || job.HolderLeftAtMs > now) job.HolderLeftAtMs = now;

                    if (job.HolderLeftAtMs >= 0 && now - job.HolderLeftAtMs >= AlightGraceMs) { Depart(locomotive, job); return; }

                    job.Save(locomotive);
                    return;

                case TaxiPhase.Returning:
                    WatchStuck(job, action, now);

                    if (action == ActionWaitingAtStation && still && Timetable.CurrentDestinationHash(locomotive) == job.ReturnHash)
                    {
                        FinishReturn(locomotive, job);
                        return;
                    }

                    job.Save(locomotive);
                    return;

                case TaxiPhase.Summoned:
                    WatchStuck(job, action, now);

                    if (!atDestination) { job.Save(locomotive); return; }

                    job.Phase = TaxiPhase.AtPickup;
                    job.PickupAtMs = now;
                    job.Save(locomotive);
                    ForgetSummonOf(job.HolderUid);
                    Message(job.HolderUid, job.WaitForMe ? "summon-arrived-wait" : "summon-arrived", job.DestinationName);
                    return;

                case TaxiPhase.AtPickup:
                    if (HolderAboard(job))
                    {
                        job.Phase = TaxiPhase.AwaitingDestination;
                        job.HolderLeftAtMs = -1;
                        job.Save(locomotive);
                        Message(job.HolderUid, "summon-boarded");
                        return;
                    }

                    if (job.PickupAtMs > now) job.PickupAtMs = now;
                    if (!job.WaitForMe && now - job.PickupAtMs >= SummonRules.PickupGraceMs) { Depart(locomotive, job); return; }

                    job.Save(locomotive);
                    return;

                case TaxiPhase.AwaitingDestination:
                    // Aboard: the ticket's own dialog takes it from here. Off again: as after a ride.
                    if (HolderAboard(job)) job.HolderLeftAtMs = -1;
                    else if (job.HolderLeftAtMs < 0 || job.HolderLeftAtMs > now) job.HolderLeftAtMs = now;

                    if (job.HolderLeftAtMs >= 0 && now - job.HolderLeftAtMs >= AlightGraceMs) { Depart(locomotive, job); return; }

                    job.Save(locomotive);
                    return;
            }
        }

        private void WatchStuck(TaxiJob job, int action, long now)
        {
            if (action != ActionStuck) { job.StuckSinceMs = 0; job.StuckReported = false; return; }

            if (job.StuckSinceMs == 0 || job.StuckSinceMs > now) job.StuckSinceMs = now;
            if (job.StuckReported || now - job.StuckSinceMs < StuckReportMs) return;

            job.StuckReported = true;
            Message(job.HolderUid, "stuck");
        }

        /// <summary>Stay: the conductor gets his old timetable back, the boiler goes out.</summary>
        private void FinishWaiting(Entity locomotive, TaxiJob job)
        {
            Timetable.Restore(locomotive, job.SavedTimetable);
            TaxiJob.Remove(locomotive);
            ForgetSummonOf(job.HolderUid);

            string put = fire.Available ? FireKeeping.TryExtinguish(fire, locomotive) : "unavailable";
            Message(job.HolderUid, put == "quenched" ? "waiting-done" : "nowater");
        }

        /// <summary>
        /// The holder is off and the grace is over. The boiler at the time of the taking decides:
        /// a train that was burning was on its way, and goes back to its timetable at once; a
        /// train that stood cold stood there for a reason, and first goes back to where it stood,
        /// to get its timetable back and its boiler put out THERE.
        /// </summary>
        private void Depart(Entity locomotive, TaxiJob job)
        {
            ForgetSummonOf(job.HolderUid);

            if (job.SavedBurning && job.SavedTimetable != null && job.SavedTimetable.GetInt("count", 0) > 0)
            {
                Timetable.Restore(locomotive, job.SavedTimetable);
                TaxiJob.Remove(locomotive);
                Message(job.HolderUid, "resumed");
                return;
            }

            StationInfo back = job.ReturnStation();

            if (back == null)
            {
                FinishReturn(locomotive, job);
                return;
            }

            job.Phase = TaxiPhase.Returning;
            job.StuckSinceMs = 0;
            job.StuckReported = false;
            Timetable.WriteSingle(locomotive, back, Timetable.ReturnHoldSeconds);
            job.Save(locomotive);
            Message(job.HolderUid, "returning", back.Name);
        }

        /// <summary>Back where it began: its timetable (or none) and its boiler as they were.</summary>
        private void FinishReturn(Entity locomotive, TaxiJob job)
        {
            bool hadTimetable = job.SavedTimetable != null && job.SavedTimetable.GetInt("count", 0) > 0;

            Timetable.Restore(locomotive, job.SavedTimetable);
            TaxiJob.Remove(locomotive);

            if (RestoreBoiler(locomotive, job) && hadTimetable) Message(job.HolderUid, "resumed-cold");
        }

        /// <summary>The boiler as it was before the ride. True when it had to be put out.</summary>
        private bool RestoreBoiler(Entity locomotive, TaxiJob job)
        {
            if (job.SavedBurning || !fire.Available || fire.IsBurning(locomotive) != true) return false;

            FireKeeping.TryExtinguish(fire, locomotive);
            return true;
        }

        private void AnswerPending(long now)
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                PendingReach wait = pending[i];
                IServerPlayer player = api.World.PlayerByUid(wait.PlayerUid) as IServerPlayer;
                Entity locomotive = api.World.GetEntityById(wait.LocomotiveId);

                if (player == null || locomotive == null) { pending.RemoveAt(i); continue; }

                ReachStatus status = stations.Reachable(locomotive, out _);
                if (status == ReachStatus.Pending && now < wait.Deadline) continue;

                pending.RemoveAt(i);

                if (TrySeat(player, out Seat seat, out _) && seat.Locomotive.EntityId == wait.LocomotiveId)
                {
                    Send(player, Describe(seat, allowPending: false, player));
                }
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Where the player sits: a vehicle of the other mod, its convoy, and the convoy's engine.</summary>
        private bool TrySeat(IServerPlayer player, out Seat seat, out string error)
        {
            seat = null;
            error = "notintrain";

            IMountableSeat mount = player?.Entity?.MountedOn;
            Entity vehicle = mount?.Entity ?? mount?.MountSupplier?.OnEntity;
            if (vehicle?.Code?.Domain != TrainCargoHolderFinder.Domain) return false;

            long head = LocomotiveFinder.ConvoyOf(vehicle);
            Entity locomotive = LocomotiveFinder.HasEngine(vehicle) ? vehicle : LocomotiveFinder.LocomotiveOfConvoy(api.World, head);

            if (locomotive == null) { error = "noengine"; return false; }
            if (!ConvoyHasConductor(head)) { error = "nolocust"; return false; }

            seat = new Seat { Vehicle = vehicle, Head = head, Locomotive = locomotive };
            return true;
        }

        /// <summary>A conductor locust on any loaded vehicle of the convoy; it sits where the player put it.</summary>
        private bool ConvoyHasConductor(long head)
        {
            foreach (Entity entity in api.World.LoadedEntities.Values)
            {
                if (entity?.Code?.Domain != TrainCargoHolderFinder.Domain || LocomotiveFinder.ConvoyOf(entity) != head) continue;
                if (TrainSignals.HasConductorLocust(entity)) return true;
            }

            return false;
        }

        private bool HolderAboard(TaxiJob job)
        {
            IMountableSeat mount = (api.World.PlayerByUid(job.HolderUid) as IServerPlayer)?.Entity?.MountedOn;
            Entity vehicle = mount?.Entity ?? mount?.MountSupplier?.OnEntity;

            return vehicle != null && LocomotiveFinder.ConvoyOf(vehicle) == job.ConvoyHead;
        }

        /// <summary>The first thing in the player's inventory the other mod counts as a light.</summary>
        private ItemSlot LighterOf(IServerPlayer player)
        {
            ItemSlot found = null;

            player?.Entity?.WalkInventory(slot =>
            {
                if (slot?.Empty == false && fire.IsIgnitionSource(slot.Itemstack))
                {
                    found = slot;
                    return false;
                }

                return true;
            });

            return found;
        }

        private StationInfo Find(Entity locomotive, ulong hash, int dimension)
        {
            foreach (StationInfo station in stations.All(GaugeOf(locomotive)))
            {
                if (station.NameHash == hash && station.Dimension == dimension) return station;
            }

            return null;
        }

        /// <summary>The nearest station to the train, among those it can reach when that is known.</summary>
        private StationInfo Nearest(Entity locomotive)
        {
            ReachStatus status = stations.Reachable(locomotive, out HashSet<(ulong, int)> reachable);

            return NearestStation(locomotive.Pos.X, locomotive.Pos.Z, locomotive.Pos.Dimension, double.MaxValue, GaugeOf(locomotive),
                status == ReachStatus.Ready ? reachable : null);
        }

        /// <summary>The nearest station to a point, within a reach; any gauge when none is given.</summary>
        private StationInfo NearestStation(double x, double z, int dimension, double reach, byte? gauge, HashSet<(ulong, int)> among = null)
        {
            StationInfo best = null;
            double bestDistance = reach * reach;

            foreach (StationInfo station in stations.All(gauge))
            {
                if (station.Dimension != dimension) continue;
                if (among != null && !among.Contains((station.NameHash, station.Dimension))) continue;

                double dx = station.X + 0.5 - x;
                double dz = station.Z + 0.5 - z;
                double distance = dx * dx + dz * dz;

                if (distance > bestDistance) continue;

                bestDistance = distance;
                best = station;
            }

            return best;
        }

        /// <summary>Mine carts run on the narrow gauge (0), everything else on the standard one (1).</summary>
        public static byte GaugeOf(Entity locomotive)
        {
            string path = locomotive?.Code?.Path ?? "";
            return path.StartsWith("minecart", StringComparison.Ordinal) ? (byte)0 : (byte)1;
        }

        private void Send(IServerPlayer player, TaxiStationsPacket packet) => channel?.SendPacket(packet, player);

        private void Message(IServerPlayer player, string key, params object[] args)
        {
            player?.SendMessage(GlobalConstants.GeneralChatGroup, Lang.Get("signalslinkytt:ticket-" + key, args), EnumChatType.Notification);
        }

        private void Message(string playerUid, string key, params object[] args)
        {
            if (string.IsNullOrEmpty(playerUid)) return;
            Message(api.World.PlayerByUid(playerUid) as IServerPlayer, key, args);
        }
    }
}
