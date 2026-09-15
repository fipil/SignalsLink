using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Vintagestory.API.Common;

namespace SignalsLink.YTT.src.probe
{
    /// <summary>
    /// Layers one and two of the safety net, and the ONE place allowed to know the other mod's
    /// member names.
    ///
    /// Layer 1: a structural probe over TYPES from the class registry - nothing constructed,
    /// nothing called, run once at startup. Layer 2: those names and types hashed into a
    /// fingerprint and compared against the known-good ones.
    ///
    /// <b>Always bind with Public | NonPublic</b>, or the day the author makes something public
    /// the bridge breaks.
    /// </summary>
    public sealed class YttSurface
    {
        public const string StorageBehavior = "SGStorage";
        public const string SteamBehavior = "SteamPowered";

        private const BindingFlags Anywhere =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>Known-good surfaces. Anything else is reported, work or not.</summary>
        private static readonly HashSet<string> KnownGood = new HashSet<string>
        {
            // YTT 0.9.0, read off the log line this class prints on a first run.
            "8ded53acfa62",
            // YTT 0.9.2, with the offscreen simulation members included.
            "0f6917689b91",
        };

        /// <summary>True when goods can be moved in and out of a boxcar.</summary>
        public bool CanCarry { get; private set; }

        /// <summary>True when the steam engine of a locomotive can be reached as well.</summary>
        public bool CanReachEngine { get; private set; }

        /// <summary>What was found, hashed. Short enough to read out of a log line.</summary>
        public string Surface { get; private set; } = "";

        /// <summary>Why the bridge stood down, when it did.</summary>
        public string Complaint { get; private set; } = "";

        // -------------------------------------------------------------- what the bridge then uses

        public FieldInfo StoragePointsField { get; private set; }
        public FieldInfo StoragePointInventoryField { get; private set; }
        public Type SteamBehaviorType { get; private set; }
        public MemberInfo EngineControllerMember { get; private set; }

        /// <summary>How a locomotive is asked to write its engine down. See YttEngine.</summary>
        public MethodInfo RequestSyncMethod { get; private set; }

        // -------------------------------------------------------------- the offscreen simulation

        /// <summary>True when trains under way in the simulation can be read. See TrainApproachWatch.</summary>
        public bool CanWatchApproach { get; private set; }

        public object OffscreenSystem { get; private set; }
        public FieldInfo VirtualConvoysField { get; private set; }
        public FieldInfo ConvoyAutomationField { get; private set; }
        public FieldInfo ConvoyPosesField { get; private set; }
        public FieldInfo ConvoyLeadIndexField { get; private set; }
        public FieldInfo AutomationStatusField { get; private set; }
        public FieldInfo AutomationIndexField { get; private set; }
        public FieldInfo AutomationRouteField { get; private set; }
        public FieldInfo RouteXField { get; private set; }
        public FieldInfo RouteYField { get; private set; }
        public FieldInfo RouteZField { get; private set; }
        public FieldInfo PoseXField { get; private set; }
        public FieldInfo PoseZField { get; private set; }

        /// <summary>The status value that means "under way to the next station".</summary>
        public object GoingStatus { get; private set; }

        /// <summary>
        /// The station the simulation actually resolved for the current leg. Optional and kept
        /// out of the fingerprint: without it the timetable entry is used, which carries the
        /// coordinates the station had when the timetable was written - and a station that has
        /// since been moved is found by name, not by them.
        /// </summary>
        public FieldInfo AutomationTargetField { get; private set; }
        public PropertyInfo TargetStationKeyProperty { get; private set; }
        public PropertyInfo StationKeyXProperty { get; private set; }
        public PropertyInfo StationKeyYProperty { get; private set; }
        public PropertyInfo StationKeyZProperty { get; private set; }

        public bool CanResolveTarget => StationKeyZProperty != null;

        /// <summary>Never throws: standing down beats taking the game down on startup.</summary>
        public static YttSurface Probe(ICoreAPI api)
        {
            YttSurface surface = new YttSurface();

            try
            {
                surface.Look(api);
            }
            catch (Exception e)
            {
                surface.CanCarry = false;
                surface.CanReachEngine = false;
                surface.Complaint = "the probe itself failed: " + e.Message;
            }

            return surface;
        }

        private void Look(ICoreAPI api)
        {
            List<string> members = new List<string>();

            Type storage = api.ClassRegistry.GetEntityBehaviorClass(StorageBehavior);

            if (storage == null)
            {
                Complaint = "entity behavior '" + StorageBehavior + "' is not registered";
                return;
            }

            members.Add(Describe(StorageBehavior, storage));

            StoragePointsField = storage.GetField("StoragePoints", Anywhere);

            if (StoragePointsField == null || !IsList(StoragePointsField.FieldType))
            {
                Complaint = "SGStorage.StoragePoints is missing or is no longer a list";
                return;
            }

            members.Add(Describe("StoragePoints", StoragePointsField.FieldType));

            Type point = StoragePointsField.FieldType.GetGenericArguments()[0];
            StoragePointInventoryField = point.GetField("Inventory", Anywhere);

            if (StoragePointInventoryField == null || !typeof(InventoryBase).IsAssignableFrom(StoragePointInventoryField.FieldType))
            {
                Complaint = "a storage point no longer carries an inventory this can read";
                return;
            }

            members.Add(Describe("StoragePoint.Inventory", StoragePointInventoryField.FieldType));

            // Never called - probed because losing it would mean persistence was redesigned.
            MethodInfo store = storage.GetMethod("StoreInventories", Anywhere);

            if (store == null)
            {
                Complaint = "SGStorage.StoreInventories is gone, so saving may work differently now";
                return;
            }

            members.Add(Describe("StoreInventories", store.ReturnType));

            CanCarry = true;

            LookAtEngine(api, members);
            LookAtOffscreen(api, members);

            Surface = Fingerprint(members);
        }

        /// <summary>
        /// Probed separately, like the engine: not being able to see trains coming is no reason
        /// to refuse to unload one that has arrived.
        /// </summary>
        private void LookAtOffscreen(ICoreAPI api, List<string> members)
        {
            foreach (ModSystem system in api.ModLoader.Systems)
            {
                if (system?.GetType().Name == "OffscreenConvoySimSystem")
                {
                    OffscreenSystem = system;
                    break;
                }
            }

            if (OffscreenSystem == null) return;

            Type sim = OffscreenSystem.GetType();
            members.Add(Describe("OffscreenConvoySimSystem", sim));

            VirtualConvoysField = sim.GetField("VirtualConvoys", Anywhere);
            if (VirtualConvoysField == null || !VirtualConvoysField.FieldType.IsGenericType
                || VirtualConvoysField.FieldType.GetGenericTypeDefinition() != typeof(Dictionary<,>)) return;

            members.Add(Describe("VirtualConvoys", VirtualConvoysField.FieldType));

            Type convoy = VirtualConvoysField.FieldType.GetGenericArguments()[1];
            ConvoyAutomationField = convoy.GetField("Automation", Anywhere);
            ConvoyPosesField = convoy.GetField("FrozenPoses", Anywhere);
            ConvoyLeadIndexField = convoy.GetField("LeadIndex", Anywhere);

            if (ConvoyAutomationField == null || ConvoyPosesField == null || !IsList(ConvoyPosesField.FieldType)
                || ConvoyLeadIndexField == null || ConvoyLeadIndexField.FieldType != typeof(int)) return;

            members.Add(Describe("VirtualConvoy.Automation", ConvoyAutomationField.FieldType));
            members.Add(Describe("VirtualConvoy.FrozenPoses", ConvoyPosesField.FieldType));

            Type pose = ConvoyPosesField.FieldType.GetGenericArguments()[0];
            PoseXField = pose.GetField("X", Anywhere);
            PoseZField = pose.GetField("Z", Anywhere);

            if (PoseXField == null || PoseXField.FieldType != typeof(double)
                || PoseZField == null || PoseZField.FieldType != typeof(double)) return;

            members.Add(Describe("FrozenVehiclePose.X", PoseXField.FieldType));

            Type automation = ConvoyAutomationField.FieldType;
            AutomationStatusField = automation.GetField("Status", Anywhere);
            AutomationIndexField = automation.GetField("CurrentStationIndex", Anywhere);
            AutomationRouteField = automation.GetField("Route", Anywhere);

            if (AutomationStatusField == null || !AutomationStatusField.FieldType.IsEnum
                || AutomationIndexField == null || AutomationIndexField.FieldType != typeof(int)
                || AutomationRouteField == null || !AutomationRouteField.FieldType.IsArray) return;

            members.Add(Describe("VirtualAutomationState.Status", AutomationStatusField.FieldType));
            members.Add(Describe("VirtualAutomationState.Route", AutomationRouteField.FieldType));

            try
            {
                GoingStatus = Enum.Parse(AutomationStatusField.FieldType, "Going");
            }
            catch (Exception)
            {
                return;
            }

            Type stop = AutomationRouteField.FieldType.GetElementType();
            RouteXField = stop.GetField("X", Anywhere);
            RouteYField = stop.GetField("Y", Anywhere);
            RouteZField = stop.GetField("Z", Anywhere);

            if (RouteXField == null || RouteXField.FieldType != typeof(int)
                || RouteYField == null || RouteYField.FieldType != typeof(int)
                || RouteZField == null || RouteZField.FieldType != typeof(int)) return;

            members.Add(Describe("TimetableRouteEntryPacket.X", RouteXField.FieldType));

            CanWatchApproach = true;

            LookAtResolvedTarget(automation);
        }

        private void LookAtResolvedTarget(Type automation)
        {
            AutomationTargetField = automation.GetField("Target", Anywhere);
            if (AutomationTargetField == null) return;

            TargetStationKeyProperty = AutomationTargetField.FieldType.GetProperty("StationKey", Anywhere);
            if (TargetStationKeyProperty == null) return;

            Type key = TargetStationKeyProperty.PropertyType;
            PropertyInfo x = key.GetProperty("X", Anywhere);
            PropertyInfo y = key.GetProperty("Y", Anywhere);
            PropertyInfo z = key.GetProperty("Z", Anywhere);

            if (x?.PropertyType != typeof(int) || y?.PropertyType != typeof(int) || z?.PropertyType != typeof(int)) return;

            StationKeyXProperty = x;
            StationKeyYProperty = y;
            StationKeyZProperty = z;
        }

        /// <summary>Probed separately: no engine is no reason to refuse to unload a boxcar.</summary>
        private void LookAtEngine(ICoreAPI api, List<string> members)
        {
            SteamBehaviorType = api.ClassRegistry.GetEntityBehaviorClass(SteamBehavior);
            if (SteamBehaviorType == null) return;

            members.Add(Describe(SteamBehavior, SteamBehaviorType));

            EngineControllerMember =
                (MemberInfo)SteamBehaviorType.GetProperty("Controller", Anywhere)
                ?? SteamBehaviorType.GetField("EngineController", Anywhere);

            if (EngineControllerMember == null) return;

            Type controller = EngineControllerMember is PropertyInfo property
                ? property.PropertyType
                : ((FieldInfo)EngineControllerMember).FieldType;

            members.Add(Describe("EngineController", controller));

            string[] wanted = { "Inventory", "FuelSlot", "WaterSlot", "TemperatureC" };
            string[] wantedMethods = { "ForceExtinguishBoiler", "TryIgniteNow", "IsIgnitionSource", "IsValidWorkingLiquid" };

            foreach (string name in wanted)
            {
                PropertyInfo found = controller.GetProperty(name, Anywhere);
                if (found == null) return;

                members.Add(Describe(name, found.PropertyType));
            }

            foreach (string name in wantedMethods)
            {
                MethodInfo found = controller.GetMethod(name, Anywhere);
                if (found == null) return;

                members.Add(Describe(name, found.ReturnType));
            }

            // Without this there is no honest way to put fuel in an engine. See YttEngine.
            RequestSyncMethod = SteamBehaviorType.GetMethod("RequestSync", Anywhere);
            if (RequestSyncMethod == null) return;

            members.Add(Describe("RequestSync", RequestSyncMethod.ReturnType));

            CanReachEngine = true;
        }

        /// <summary>
        /// Name and type. The type matters as much: a member that keeps its name and changes its
        /// type still resolves, and fails later somewhere else.
        /// </summary>
        private static string Describe(string name, Type type)
        {
            return name + ":" + (type?.Name ?? "?");
        }

        private static bool IsList(Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);
        }

        /// <summary>Sorted first - reflection promises no order.</summary>
        public static string Fingerprint(IEnumerable<string> members)
        {
            List<string> sorted = new List<string>(members);
            sorted.Sort(StringComparer.Ordinal);

            using SHA1 sha = SHA1.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("|", sorted)));

            return Convert.ToHexString(hash).Substring(0, 12).ToLowerInvariant();
        }

        /// <summary>Says in the log what was found, and how surprising it was.</summary>
        public void Report(ICoreAPI api, string version)
        {
            if (!CanCarry)
            {
                api.Logger.Warning("[SignalsLink.YTT] standing down: " + Complaint
                    + ". Trains cannot be loaded or unloaded until this is sorted out.");
                return;
            }

            if (KnownGood.Contains(Surface))
            {
                api.Logger.Notification("[SignalsLink.YTT] surface " + Surface + " as expected.");
            }
            else
            {
                api.Logger.Warning("[SignalsLink.YTT] Yang's Transport Tycoon " + version
                    + " has a surface this was not written against (" + Surface + "). It looks usable and"
                    + " will be used, but if trains start behaving oddly, this line is the first thing to quote.");
            }

            if (!CanReachEngine)
            {
                api.Logger.Notification("[SignalsLink.YTT] the steam engine cannot be reached on this version;"
                    + " cargo still works, stoking and lighting do not.");
            }

            if (!CanWatchApproach)
            {
                api.Logger.Notification("[SignalsLink.YTT] the offscreen simulation cannot be read on this version;"
                    + " anchors will not wake for approaching trains.");
            }
        }
    }
}
