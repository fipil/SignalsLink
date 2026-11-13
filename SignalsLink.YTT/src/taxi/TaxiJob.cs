using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace SignalsLink.YTT.src.taxi
{
    public enum TaxiPhase
    {
        /// <summary>Under way to the destination, or braking after "stop here".</summary>
        Going = 1,

        /// <summary>Arrived or stopped; waiting for the ticket holder to get off, then a while longer.</summary>
        Alighting = 2,

        /// <summary>Taking the train back to where the ride began.</summary>
        Returning = 3,

        /// <summary>Called by a ticket off the train: on its way to the pickup station.</summary>
        Summoned = 4,

        /// <summary>At the pickup station, waiting for the one who called.</summary>
        AtPickup = 5,

        /// <summary>The caller is aboard; waiting for a destination from the ticket.</summary>
        AwaitingDestination = 6,
    }

    /// <summary>
    /// One taxi ride, kept on the LOCOMOTIVE's own attributes so it survives a restart and needs
    /// no block entity: what the train was doing before, where it is going, and for whom.
    /// </summary>
    public sealed class TaxiJob
    {
        public const string Key = "signalslinkytt.taxi";

        public TaxiPhase Phase = TaxiPhase.Going;
        public bool Wait;

        /// <summary>True once "stop here" was asked for: the arrival is a halt, not a station.</summary>
        public bool Stopping;

        public long ConvoyHead;
        public string HolderUid = "";

        public string DestinationName = "";
        public ulong DestinationHash;
        public int DestinationDimension;

        /// <summary>The conductor's timetable as it was; null when there was none.</summary>
        public ITreeAttribute SavedTimetable;
        public bool SavedBurning;

        public double OriginX, OriginY, OriginZ;

        /// <summary>The nearest station to where the ride began; empty name when none was found.</summary>
        public string ReturnName = "";
        public ulong ReturnHash;
        public int ReturnDimension;
        public int ReturnX, ReturnY, ReturnZ;
        public byte ReturnGauge;

        /// <summary>World clock when the holder was last seen off the train; -1 while aboard.</summary>
        public long HolderLeftAtMs = -1;

        /// <summary>World clock when the train was last seen stuck; 0 while it is not.</summary>
        public long StuckSinceMs;
        public bool StuckReported;

        /// <summary>Summoning: the train waits at the pickup until the caller boards, however long.</summary>
        public bool WaitForMe;

        /// <summary>World clock when the summoned train reached the pickup; 0 before.</summary>
        public long PickupAtMs;

        public bool HasReturnStation => !string.IsNullOrEmpty(ReturnName);

        /// <summary>Nobody is served any more: on its way back the train may be called again.</summary>
        public bool Free => Phase == TaxiPhase.Returning;

        /// <summary>A job of a train that is free, or null: the train has none or is still serving.</summary>
        public static TaxiJob LoadFree(Entity locomotive)
        {
            TaxiJob job = Load(locomotive);
            return job != null && job.Free ? job : null;
        }

        /// <summary>Called again: what was set aside stays set aside, the rest starts afresh.</summary>
        public void Reuse()
        {
            Wait = false;
            Stopping = false;
            HolderLeftAtMs = -1;
            StuckSinceMs = 0;
            StuckReported = false;
            PickupAtMs = 0;
        }

        public static bool Exists(Entity locomotive) => locomotive?.Attributes?.HasAttribute(Key) == true;

        public static TaxiJob Load(Entity locomotive)
        {
            ITreeAttribute tree = locomotive?.Attributes?.GetTreeAttribute(Key);
            return tree == null ? null : FromTree(tree);
        }

        public void Save(Entity locomotive)
        {
            ITreeAttribute tree = new TreeAttribute();
            ToTree(tree);
            locomotive.Attributes[Key] = tree;
        }

        public static void Remove(Entity locomotive) => locomotive?.Attributes?.RemoveAttribute(Key);

        public static TaxiJob FromTree(ITreeAttribute tree)
        {
            TaxiJob job = new TaxiJob
            {
                Phase = (TaxiPhase)tree.GetInt("phase", (int)TaxiPhase.Going),
                Wait = tree.GetBool("wait"),
                Stopping = tree.GetBool("stopping"),
                ConvoyHead = tree.GetLong("convoyHead"),
                HolderUid = tree.GetString("holder", ""),
                DestinationName = tree.GetString("destName", ""),
                DestinationHash = unchecked((ulong)tree.GetLong("destHash")),
                DestinationDimension = tree.GetInt("destDim"),
                SavedTimetable = tree.GetTreeAttribute("savedTimetable")?.Clone() as ITreeAttribute,
                SavedBurning = tree.GetBool("savedBurning"),
                OriginX = tree.GetDouble("originX"),
                OriginY = tree.GetDouble("originY"),
                OriginZ = tree.GetDouble("originZ"),
                ReturnName = tree.GetString("returnName", ""),
                ReturnHash = unchecked((ulong)tree.GetLong("returnHash")),
                ReturnDimension = tree.GetInt("returnDim"),
                ReturnX = tree.GetInt("returnX"),
                ReturnY = tree.GetInt("returnY"),
                ReturnZ = tree.GetInt("returnZ"),
                ReturnGauge = (byte)tree.GetInt("returnGauge"),
                HolderLeftAtMs = tree.GetLong("holderLeftAt", -1),
                StuckSinceMs = tree.GetLong("stuckSince"),
                StuckReported = tree.GetBool("stuckReported"),
                WaitForMe = tree.GetBool("waitForMe"),
                PickupAtMs = tree.GetLong("pickupAt"),
            };

            return job;
        }

        public void ToTree(ITreeAttribute tree)
        {
            tree.SetInt("phase", (int)Phase);
            tree.SetBool("wait", Wait);
            tree.SetBool("stopping", Stopping);
            tree.SetLong("convoyHead", ConvoyHead);
            tree.SetString("holder", HolderUid ?? "");
            tree.SetString("destName", DestinationName ?? "");
            tree.SetLong("destHash", unchecked((long)DestinationHash));
            tree.SetInt("destDim", DestinationDimension);
            if (SavedTimetable != null) tree["savedTimetable"] = SavedTimetable.Clone();
            else tree.RemoveAttribute("savedTimetable");
            tree.SetBool("savedBurning", SavedBurning);
            tree.SetDouble("originX", OriginX);
            tree.SetDouble("originY", OriginY);
            tree.SetDouble("originZ", OriginZ);
            tree.SetString("returnName", ReturnName ?? "");
            tree.SetLong("returnHash", unchecked((long)ReturnHash));
            tree.SetInt("returnDim", ReturnDimension);
            tree.SetInt("returnX", ReturnX);
            tree.SetInt("returnY", ReturnY);
            tree.SetInt("returnZ", ReturnZ);
            tree.SetInt("returnGauge", ReturnGauge);
            tree.SetLong("holderLeftAt", HolderLeftAtMs);
            tree.SetLong("stuckSince", StuckSinceMs);
            tree.SetBool("stuckReported", StuckReported);
            tree.SetBool("waitForMe", WaitForMe);
            tree.SetLong("pickupAt", PickupAtMs);
        }

        public StationInfo ReturnStation()
        {
            if (!HasReturnStation) return null;

            return new StationInfo
            {
                Name = ReturnName, NameHash = ReturnHash, Dimension = ReturnDimension,
                X = ReturnX, Y = ReturnY, Z = ReturnZ, Gauge = ReturnGauge,
            };
        }
    }
}
