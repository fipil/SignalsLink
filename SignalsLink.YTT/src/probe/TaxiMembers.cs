using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;

namespace SignalsLink.YTT.src.probe
{
    /// <summary>
    /// What the train ticket needs of the other mod: the list of stations, and the way to tell
    /// which of them lie on the same connected piece of track as the train. Looked up once at
    /// startup, nothing called; two groups that degrade apart, both out of the fingerprint.
    ///
    /// The timetable itself is NOT here: it is a plain attribute tree on the locomotive, and the
    /// ticket reads and writes it without reflection. Only its key is read off the other mod's
    /// constant, so a renamed key is noticed rather than written past.
    /// </summary>
    public sealed class TaxiMembers
    {
        private const BindingFlags Anywhere =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public const string DefaultTimetableKey = "yangtransport.timetable";

        // -------------------------------------------------------------- stations

        public object StationRegistry { get; private set; }

        /// <summary>RailStationRegistrySystem.BorrowStationListEntries(byte?): a PUBLIC entry type.</summary>
        public MethodInfo BorrowStations { get; private set; }

        public FieldInfo ListName { get; private set; }
        public FieldInfo ListHash { get; private set; }
        public FieldInfo ListX { get; private set; }
        public FieldInfo ListY { get; private set; }
        public FieldInfo ListZ { get; private set; }
        public FieldInfo ListDimension { get; private set; }
        public FieldInfo ListGauge { get; private set; }
        public FieldInfo ListHasStop { get; private set; }

        public bool CanListStations => BorrowStations != null && ListHasStop != null;

        // -------------------------------------------------------------- reach

        public object RailGraphSystem { get; private set; }
        public PropertyInfo Graph { get; private set; }
        public object Pathing { get; private set; }

        /// <summary>RailAutomationPathingSystem.TryGetPathIndexForEdge(ulong, out RailPathIndex, out int).</summary>
        public MethodInfo PathIndexForEdge { get; private set; }

        /// <summary>RailPathIndex.TryResolveStationTarget(RailGraphLive, RailStationEntry, out StationPathTarget).</summary>
        public MethodInfo ResolveStationTarget { get; private set; }

        /// <summary>RailStationRegistrySystem.CollectEntries(List&lt;RailStationEntry&gt;).</summary>
        public MethodInfo CollectEntries { get; private set; }

        public Type StationEntryType { get; private set; }
        public FieldInfo EntryHash { get; private set; }
        public FieldInfo EntryKey { get; private set; }
        public FieldInfo EntryHasStop { get; private set; }
        public PropertyInfo KeyDimension { get; private set; }

        /// <summary>The vehicles' ServerTryGetAutomationRouteStart(bool, out RailCursor), by vehicle type.</summary>
        public MethodInfo RouteStartOfLocomotive { get; private set; }
        public MethodInfo RouteStartOfMinecart { get; private set; }
        public Type LocomotiveType { get; private set; }
        public Type MinecartType { get; private set; }
        public FieldInfo CursorSegmentHash { get; private set; }

        public bool CanJudgeReach { get; private set; }

        // -------------------------------------------------------------- timetable

        public string TimetableKey { get; private set; } = DefaultTimetableKey;

        /// <summary>True when the other mod still names the key this expects.</summary>
        public bool TimetableKeyConfirmed { get; private set; }

        /// <summary>Never throws; a missing piece leaves its group off.</summary>
        public static TaxiMembers Probe(ICoreAPI api, Assembly ytt)
        {
            TaxiMembers members = new TaxiMembers();

            try { members.LookAtStations(api); } catch (Exception) { members.BorrowStations = null; }
            try { members.LookAtReach(api, ytt); } catch (Exception) { members.CanJudgeReach = false; }
            try { members.LookAtTimetableKey(ytt); } catch (Exception) { members.TimetableKeyConfirmed = false; }

            return members;
        }

        private static object SystemNamed(ICoreAPI api, string typeName)
        {
            foreach (ModSystem system in api.ModLoader.Systems)
            {
                if (system?.GetType().Name == typeName) return system;
            }

            return null;
        }

        private void LookAtStations(ICoreAPI api)
        {
            StationRegistry = SystemNamed(api, "RailStationRegistrySystem");
            if (StationRegistry == null) return;

            MethodInfo borrow = StationRegistry.GetType().GetMethod("BorrowStationListEntries", Anywhere);
            if (borrow == null || !borrow.ReturnType.IsArray) return;

            ParameterInfo[] parameters = borrow.GetParameters();
            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(byte?)) return;

            Type entry = borrow.ReturnType.GetElementType();
            ListName = Field(entry, "Name", typeof(string));
            ListHash = Field(entry, "NameHash", typeof(ulong));
            ListX = Field(entry, "X", typeof(int));
            ListY = Field(entry, "Y", typeof(int));
            ListZ = Field(entry, "Z", typeof(int));
            ListDimension = Field(entry, "Dimension", typeof(int));
            ListGauge = Field(entry, "Gauge", typeof(byte));
            FieldInfo hasStop = Field(entry, "HasStopEndpoint", typeof(bool));

            if (ListName == null || ListHash == null || ListX == null || ListY == null || ListZ == null
                || ListDimension == null || ListGauge == null || hasStop == null) return;

            BorrowStations = borrow;
            ListHasStop = hasStop;
        }

        private void LookAtReach(ICoreAPI api, Assembly ytt)
        {
            if (StationRegistry == null || ytt == null) return;

            RailGraphSystem = SystemNamed(api, "RailGraphServerSystem");
            Pathing = SystemNamed(api, "RailAutomationPathingSystem");
            if (RailGraphSystem == null || Pathing == null) return;

            Graph = RailGraphSystem.GetType().GetProperty("Graph", Anywhere);
            if (Graph == null || !Graph.CanRead) return;

            PathIndexForEdge = Pathing.GetType().GetMethod("TryGetPathIndexForEdge", Anywhere);
            if (PathIndexForEdge == null || PathIndexForEdge.ReturnType != typeof(bool)) return;

            ParameterInfo[] indexParameters = PathIndexForEdge.GetParameters();
            if (indexParameters.Length != 3 || indexParameters[0].ParameterType != typeof(ulong)
                || !indexParameters[1].IsOut || !indexParameters[2].IsOut) return;

            Type pathIndex = indexParameters[1].ParameterType.GetElementType();

            foreach (MethodInfo method in StationRegistry.GetType().GetMethods(Anywhere))
            {
                if (method.Name != "CollectEntries" || method.GetParameters().Length != 1) continue;

                Type list = method.GetParameters()[0].ParameterType;
                if (!list.IsGenericType || list.GetGenericTypeDefinition() != typeof(List<>)) continue;

                CollectEntries = method;
                StationEntryType = list.GetGenericArguments()[0];
                break;
            }

            if (CollectEntries == null) return;

            EntryHash = Field(StationEntryType, "NameHash", typeof(ulong));
            EntryKey = StationEntryType.GetField("Key", Anywhere);
            EntryHasStop = Field(StationEntryType, "HasStopEndpoint", typeof(bool));
            if (EntryHash == null || EntryKey == null || EntryHasStop == null) return;

            KeyDimension = EntryKey.FieldType.GetProperty("Dimension", Anywhere);
            if (KeyDimension?.PropertyType != typeof(int)) return;

            ResolveStationTarget = pathIndex?.GetMethod("TryResolveStationTarget", Anywhere);
            if (ResolveStationTarget == null || ResolveStationTarget.ReturnType != typeof(bool)) return;

            ParameterInfo[] resolveParameters = ResolveStationTarget.GetParameters();
            if (resolveParameters.Length != 3 || resolveParameters[1].ParameterType != StationEntryType
                || !resolveParameters[2].IsOut) return;

            LocomotiveType = ytt.GetType("YangTransport.EntityStandardGaugeLocomotive");
            MinecartType = ytt.GetType("YangTransport.EntityMinecart");
            RouteStartOfLocomotive = RouteStartOn(LocomotiveType);
            RouteStartOfMinecart = RouteStartOn(MinecartType);
            if (RouteStartOfLocomotive == null && RouteStartOfMinecart == null) return;

            MethodInfo any = RouteStartOfLocomotive ?? RouteStartOfMinecart;
            Type cursor = any.GetParameters()[1].ParameterType.GetElementType();
            CursorSegmentHash = Field(cursor, "SegmentHash", typeof(ulong));
            if (CursorSegmentHash == null) return;

            CanJudgeReach = true;
        }

        private static MethodInfo RouteStartOn(Type vehicle)
        {
            MethodInfo method = vehicle?.GetMethod("ServerTryGetAutomationRouteStart", Anywhere);
            if (method == null || method.ReturnType != typeof(bool)) return null;

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 2 || parameters[0].ParameterType != typeof(bool) || !parameters[1].IsOut) return null;

            return method;
        }

        private void LookAtTimetableKey(Assembly ytt)
        {
            FieldInfo key = ytt?.GetType("YangTransport.ConductorTimetableStorage")?.GetField("RootAttributeKey", Anywhere);
            if (key == null || !key.IsLiteral || key.FieldType != typeof(string)) return;

            if (key.GetRawConstantValue() is string found && found.Length > 0)
            {
                TimetableKey = found;
                TimetableKeyConfirmed = true;
            }
        }

        private static FieldInfo Field(Type type, string name, Type expected)
        {
            FieldInfo found = type?.GetField(name, Anywhere);

            return found != null && found.FieldType == expected ? found : null;
        }
    }
}
