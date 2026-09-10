using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Vintagestory.API.Common;

namespace SignalsLink.YTT.src.probe
{
    /// <summary>
    /// <b>Layer one and two of the safety net: what the other mod looks like from outside.</b>
    ///
    /// This bridge reads private fields of another mod, so the question is never whether it works
    /// today - it is what happens the day the other author renames something. Answering that is
    /// what this class is for, and it is the ONE place allowed to know those names.
    ///
    /// <b>Layer 1 - a structural probe, without a single instance.</b> Types come from the class
    /// registry, which is ordinary public API, and everything after that is reflection over TYPES,
    /// not over objects. Nothing is constructed, nothing is called, no entity has to exist. It runs
    /// once at startup and can therefore refuse work before a single item is at risk.
    ///
    /// <b>Layer 2 - a fingerprint.</b> The names and types found are hashed into a short string
    /// which is compared against the ones this was written against. A new version of the other mod
    /// whose surface is unchanged says so in the log and carries on; one that has moved is a loud
    /// warning and a bridge that stands down.
    ///
    /// <b>Always bind with Public | NonPublic.</b> The day the author grants our request and makes
    /// something public, binding to NonPublic alone would break the bridge - being given what we
    /// asked for must not be what kills us.
    /// </summary>
    public sealed class YttSurface
    {
        public const string StorageBehavior = "SGStorage";
        public const string SteamBehavior = "SteamPowered";

        private const BindingFlags Anywhere =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>
        /// The fingerprints this bridge was written against. A version whose surface hashes to one
        /// of these is known good; anything else is reported, whether or not it turns out to work.
        /// </summary>
        private static readonly HashSet<string> KnownGood = new HashSet<string>
        {
            // YTT 0.9.0, read off the log line this very class prints on a first run. Filling it
            // in from an observed version is the point: it means the next surprise is a real
            // change in the other mod rather than a value nobody ever checked.
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

        /// <summary>
        /// Looks the other mod over. Never throws: a bridge that takes the game down on startup is
        /// worse than one that says it cannot work.
        /// </summary>
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

            // Not called - the other mod calls it itself when a slot is marked dirty. It is probed
            // because losing it would mean persistence had been redesigned, and THAT is the change
            // that quietly eats a player's goods.
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

        /// <summary>
        /// The steam engine, probed separately and allowed to fail on its own: not being able to
        /// stoke a locomotive is no reason to refuse to unload a boxcar.
        /// </summary>
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

            // The engine does not save itself off a dirty slot the way the wagons do, so
            // without this there is no honest way to put fuel in one.
            RequestSyncMethod = SteamBehaviorType.GetMethod("RequestSync", Anywhere);
            if (RequestSyncMethod == null) return;

            members.Add(Describe("RequestSync", RequestSyncMethod.ReturnType));

            CanReachEngine = true;
        }

        /// <summary>
        /// One member, as a name and a type. The type matters as much as the name: a member that
        /// keeps its name and changes its type is the dangerous case, because the lookup still
        /// succeeds and the cast fails later, somewhere else.
        /// </summary>
        private static string Describe(string name, Type type)
        {
            return name + ":" + (type?.Name ?? "?");
        }

        private static bool IsList(Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);
        }

        /// <summary>
        /// Hashes what was found. Sorted first: reflection promises no order, and a fingerprint
        /// that depended on one would cry wolf at random.
        /// </summary>
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
