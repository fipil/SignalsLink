using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace SignalsLink.YTT.src.taxi
{
    /// <summary>One stop, as the conductor's timetable names it: by name hash first, place second.</summary>
    public sealed class StationInfo
    {
        public string Name { get; init; } = "";
        public ulong NameHash { get; init; }
        public int X { get; init; }
        public int Y { get; init; }
        public int Z { get; init; }
        public int Dimension { get; init; }
        public byte Gauge { get; init; }
    }

    /// <summary>
    /// The conductor's timetable, which the other mod keeps as a PLAIN attribute tree on the
    /// locomotive: a count, the index of the current stop, a change serial the automation
    /// watches, and one subtree per stop. Read, set aside, written and put back here without
    /// reflection. The layout is MIRRORED from ConductorTimetableStorage; only the key is read
    /// off the other mod's constant at startup.
    ///
    /// Works on any tree, so it can be checked on a table; the entity overloads are one line each.
    /// </summary>
    public static class Timetable
    {
        /// <summary>Set from the probe at startup; the default is what 1.0 uses.</summary>
        public static string Key { get; set; } = probe.TaxiMembers.DefaultTimetableKey;

        /// <summary>A whole day: the single stop of a taxi ride is never "done with".</summary>
        public const int HoldSeconds = 24 * 60 * 60;

        /// <summary>A second less on the way back, so an off-screen train on its return reads as free.</summary>
        public const int ReturnHoldSeconds = HoldSeconds - 1;

        public static ITreeAttribute Snapshot(Entity locomotive) => Snapshot(locomotive?.Attributes);

        public static bool HasRoute(Entity locomotive) => Count(locomotive?.Attributes) > 0;

        public static void WriteSingle(Entity locomotive, StationInfo stop, int holdSeconds = HoldSeconds) => WriteSingle(locomotive.Attributes, stop, holdSeconds);

        public static void Clear(Entity locomotive) => Clear(locomotive.Attributes);

        public static void Restore(Entity locomotive, ITreeAttribute saved) => Restore(locomotive.Attributes, saved);

        public static ulong CurrentDestinationHash(Entity locomotive) => CurrentDestinationHash(locomotive?.Attributes);

        // ---------------------------------------------------------------- on a tree

        /// <summary>A copy of the tree as it is, or null when there is none. Put back with Restore.</summary>
        public static ITreeAttribute Snapshot(ITreeAttribute attributes)
        {
            return attributes?.GetTreeAttribute(Key)?.Clone() as ITreeAttribute;
        }

        public static int Count(ITreeAttribute attributes)
        {
            return attributes?.GetTreeAttribute(Key)?.GetInt("count", 0) ?? 0;
        }

        /// <summary>The name hash of the stop the conductor is heading for, or zero.</summary>
        public static ulong CurrentDestinationHash(ITreeAttribute attributes)
        {
            ITreeAttribute root = attributes?.GetTreeAttribute(Key);
            if (root == null) return 0;

            int index = root.GetInt("currentStationIndex", -1);
            if (index < 0 || index >= root.GetInt("count", 0)) index = 0;

            return unchecked((ulong)(root.GetTreeAttribute("entry" + index)?.GetLong("nameHash", 0) ?? 0));
        }

        /// <summary>A timetable of one stop: there, and stay. The conductor notices the serial.</summary>
        public static void WriteSingle(ITreeAttribute attributes, StationInfo stop, int holdSeconds = HoldSeconds)
        {
            ITreeAttribute root = attributes.GetOrAddTreeAttribute(Key);
            int oldCount = root.GetInt("count", 0);

            Bump(root);
            root.SetInt("count", 1);
            root.SetInt("currentStationIndex", 0);

            ITreeAttribute entry = root.GetOrAddTreeAttribute("entry0");
            entry.SetString("stationName", stop.Name ?? "");
            entry.SetLong("nameHash", unchecked((long)stop.NameHash));
            entry.SetInt("x", stop.X);
            entry.SetInt("y", stop.Y);
            entry.SetInt("z", stop.Z);
            entry.SetInt("dim", stop.Dimension);
            entry.SetInt("timeElapsedSeconds", holdSeconds);
            entry.SetInt("mileageLeftMinutes", 0);
            entry.SetBool("announceOnArrival", true);
            entry.SetBool("announceOnDeparture", false);

            for (int i = 1; i < oldCount; i++) root.RemoveAttribute("entry" + i);
        }

        /// <summary>No stops at all: a conductor without a timetable brakes and holds.</summary>
        public static void Clear(ITreeAttribute attributes)
        {
            ITreeAttribute root = attributes.GetOrAddTreeAttribute(Key);
            int oldCount = root.GetInt("count", 0);

            Bump(root);
            root.SetInt("count", 0);
            root.SetInt("currentStationIndex", -1);

            for (int i = 0; i < oldCount; i++) root.RemoveAttribute("entry" + i);
        }

        /// <summary>
        /// Puts a snapshot back, with a serial above whatever is there now so the conductor
        /// reloads it. A null snapshot means there was no timetable: the tree is emptied.
        /// </summary>
        public static void Restore(ITreeAttribute attributes, ITreeAttribute saved)
        {
            int serial = attributes.GetTreeAttribute(Key)?.GetInt("changeSerial", 0) ?? 0;

            if (saved == null)
            {
                Clear(attributes);
                return;
            }

            ITreeAttribute copy = saved.Clone() as ITreeAttribute;
            if (copy == null) return;

            int savedSerial = copy.GetInt("changeSerial", 0);
            copy.SetInt("changeSerial", System.Math.Max(serial, savedSerial) + 1);
            attributes[Key] = copy;
        }

        private static void Bump(ITreeAttribute root)
        {
            root.SetInt("changeSerial", root.GetInt("changeSerial", 0) + 1);
        }
    }
}
