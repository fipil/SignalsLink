using System;
using System.Collections.Generic;
using SignalsLink.src.signals.vehicle;
using SignalsLink.YTT.src.stoker;
using SignalsLink.YTT.src.train;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace SignalsLink.YTT.src.taxi
{
    /// <summary>
    /// Runs the taxi rides, server side: answers a ticket, gives the conductor his orders, and
    /// once a second looks at every loaded locomotive with a ride on it to see what comes next.
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
        private readonly IServerNetworkChannel channel;
        private readonly StandingWatch standing = new StandingWatch();
        private readonly List<PendingReach> pending = new List<PendingReach>();
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

        public TaxiDispatcher(ICoreServerAPI api, YttFire fire, YttStations stations, IServerNetworkChannel channel)
        {
            this.api = api;
            this.fire = fire;
            this.stations = stations;
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
                Send(player, new TaxiStationsPacket { Error = error });
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
            if (job != null)
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
                job = new TaxiJob
                {
                    ConvoyHead = seat.Head,
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
                Message(job.HolderUid, "aborted-nolocust");
                return;
            }

            bool still = standing.IsStanding(locomotive.EntityId, locomotive.Pos.X, locomotive.Pos.Y, locomotive.Pos.Z, now);
            int action = locomotive.WatchedAttributes?.GetInt(TrainSignals.ActionAttribute, 0) ?? 0;

            switch (job.Phase)
            {
                case TaxiPhase.Going:
                    WatchStuck(job, action, now);

                    bool arrived = job.Stopping
                        ? action == ActionNoTimetable && still
                        : action == ActionWaitingAtStation && still && Timetable.CurrentDestinationHash(locomotive) == job.DestinationHash;

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

            string put = fire.Available ? FireKeeping.TryExtinguish(fire, locomotive) : "unavailable";
            Message(job.HolderUid, put == "quenched" ? "waiting-done" : "nowater");
        }

        /// <summary>The holder is off and the grace is over: back to work, or back to the start.</summary>
        private void Depart(Entity locomotive, TaxiJob job)
        {
            if (job.SavedTimetable != null && job.SavedTimetable.GetInt("count", 0) > 0)
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
            Timetable.WriteSingle(locomotive, back);
            job.Save(locomotive);
            Message(job.HolderUid, "returning", back.Name);
        }

        /// <summary>Back where it began: no timetable, and the boiler as it was.</summary>
        private void FinishReturn(Entity locomotive, TaxiJob job)
        {
            Timetable.Restore(locomotive, job.SavedTimetable);
            TaxiJob.Remove(locomotive);

            if (!job.SavedBurning && fire.Available && fire.IsBurning(locomotive) == true) FireKeeping.TryExtinguish(fire, locomotive);
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
            StationInfo best = null;
            double bestDistance = double.MaxValue;

            foreach (StationInfo station in stations.All(GaugeOf(locomotive)))
            {
                if (station.Dimension != locomotive.Pos.Dimension) continue;
                if (status == ReachStatus.Ready && !reachable.Contains((station.NameHash, station.Dimension))) continue;

                double dx = station.X + 0.5 - locomotive.Pos.X;
                double dz = station.Z + 0.5 - locomotive.Pos.Z;
                double distance = dx * dx + dz * dz;

                if (distance >= bestDistance) continue;

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
