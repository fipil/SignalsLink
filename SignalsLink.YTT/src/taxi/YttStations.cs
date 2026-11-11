using System;
using System.Collections;
using System.Collections.Generic;
using SignalsLink.YTT.src.probe;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SignalsLink.YTT.src.taxi
{
    public enum ReachStatus
    {
        /// <summary>The set is complete.</summary>
        Ready,

        /// <summary>The other mod is still building the index for this piece of track; ask again.</summary>
        Pending,

        /// <summary>Not on this version, or the train is not on a rail it knows.</summary>
        Unavailable,
    }

    /// <summary>
    /// The other mod's stations, and which of them a train can reach. Both through the probe,
    /// both wrapped, each standing down on its own after a failure.
    ///
    /// Reach is NOT what the other mod's own timetable map calls "reachable" (that is a global
    /// index and true for any station on any rail). It is the per-component path index the
    /// automation drives by: a station resolves on the index of the train's current rail only
    /// when its stop lies on the same connected piece of track.
    /// </summary>
    public class YttStations
    {
        private readonly YttSurface surface;
        private readonly ILogger logger;
        private bool listFailed;
        private bool reachFailed;

        public YttStations(YttSurface surface, ILogger logger)
        {
            this.surface = surface;
            this.logger = logger;
        }

        private TaxiMembers Taxi => surface?.Taxi;

        public bool Available => !listFailed && Taxi?.CanListStations == true;

        public bool CanJudgeReach => !reachFailed && Taxi?.CanJudgeReach == true;

        /// <summary>Every station of a gauge (or all), with a stop the conductor can use.</summary>
        public IReadOnlyList<StationInfo> All(byte? gauge)
        {
            List<StationInfo> stations = new List<StationInfo>();
            if (!Available) return stations;

            try
            {
                TaxiMembers t = Taxi;

                if (t.BorrowStations.Invoke(t.StationRegistry, new object[] { gauge }) is not Array entries) return stations;

                foreach (object entry in entries)
                {
                    if (entry == null || t.ListHasStop.GetValue(entry) is not true) continue;

                    stations.Add(new StationInfo
                    {
                        Name = t.ListName.GetValue(entry) as string ?? "",
                        NameHash = (ulong)t.ListHash.GetValue(entry),
                        X = (int)t.ListX.GetValue(entry),
                        Y = (int)t.ListY.GetValue(entry),
                        Z = (int)t.ListZ.GetValue(entry),
                        Dimension = (int)t.ListDimension.GetValue(entry),
                        Gauge = (byte)t.ListGauge.GetValue(entry),
                    });
                }
            }
            catch (Exception e)
            {
                listFailed = true;
                logger?.Error("[SignalsLink.YTT] could not read the station registry: " + e
                    + " The train ticket does nothing for the rest of this session.");
                stations.Clear();
            }

            return stations;
        }

        /// <summary>
        /// The stations on the same connected piece of track as this locomotive, as (name hash,
        /// dimension) pairs. Pending while the other mod is still building that index.
        /// </summary>
        public ReachStatus Reachable(Entity locomotive, out HashSet<(ulong hash, int dimension)> reachable)
        {
            reachable = null;
            if (!CanJudgeReach || locomotive == null) return ReachStatus.Unavailable;

            try
            {
                TaxiMembers t = Taxi;

                if (!TryCurrentEdge(t, locomotive, out ulong edge)) return ReachStatus.Unavailable;

                object[] indexArgs = { edge, null, 0 };
                if (t.PathIndexForEdge.Invoke(t.Pathing, indexArgs) is not true || indexArgs[1] == null) return ReachStatus.Pending;

                object index = indexArgs[1];
                object graph = t.Graph.GetValue(t.RailGraphSystem);
                if (graph == null) return ReachStatus.Unavailable;

                object list = Activator.CreateInstance(typeof(List<>).MakeGenericType(t.StationEntryType));
                t.CollectEntries.Invoke(t.StationRegistry, new[] { list });

                reachable = new HashSet<(ulong, int)>();

                foreach (object entry in (IEnumerable)list)
                {
                    if (entry == null || t.EntryHasStop.GetValue(entry) is not true) continue;

                    object[] resolveArgs = { graph, entry, null };
                    if (t.ResolveStationTarget.Invoke(index, resolveArgs) is not true) continue;

                    ulong hash = (ulong)t.EntryHash.GetValue(entry);
                    int dimension = (int)t.KeyDimension.GetValue(t.EntryKey.GetValue(entry));
                    reachable.Add((hash, dimension));
                }

                return ReachStatus.Ready;
            }
            catch (Exception e)
            {
                reachFailed = true;
                reachable = null;
                logger?.Error("[SignalsLink.YTT] could not tell which stations a train can reach: " + e
                    + " The train ticket offers every station of the gauge for the rest of this session.");
                return ReachStatus.Unavailable;
            }
        }

        /// <summary>The rail under the train, as the automation sees it. Zero when it has none.</summary>
        private static bool TryCurrentEdge(TaxiMembers t, Entity locomotive, out ulong edge)
        {
            edge = 0;

            Type type = locomotive.GetType();
            System.Reflection.MethodInfo routeStart =
                t.LocomotiveType != null && t.LocomotiveType.IsAssignableFrom(type) ? t.RouteStartOfLocomotive
                : t.MinecartType != null && t.MinecartType.IsAssignableFrom(type) ? t.RouteStartOfMinecart
                : null;

            if (routeStart == null) return false;

            object[] args = { true, null };
            if (routeStart.Invoke(locomotive, args) is not true || args[1] == null) return false;

            edge = (ulong)t.CursorSegmentHash.GetValue(args[1]);
            return edge != 0;
        }
    }
}
