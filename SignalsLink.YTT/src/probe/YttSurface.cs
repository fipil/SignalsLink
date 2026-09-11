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

            Surface = Fingerprint(members);
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
        }
    }
}
