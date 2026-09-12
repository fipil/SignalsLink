using System;
using System.Collections.Generic;
using SignalsLink.src.signals.cargo;
using SignalsLink.src.signals.paperConditions;
using SignalsLink.YTT.src.probe;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SignalsLink.YTT.src.train
{
    /// <summary>What <c>unload train north 3 engine</c> asked for.</summary>
    public sealed class TrainSelector : ICargoSelector
    {
        /// <summary>Narrows the search to one side of the device, or null for all round.</summary>
        public BlockFacing Direction { get; }

        /// <summary>
        /// How many blocks away the track is, counted by stepping off the device - <c>north5</c>.
        /// Null means any distance on that side.
        /// </summary>
        public int? Distance { get; }

        /// <summary>Which vehicle, counted from the head of the convoy; null for all of them.</summary>
        public int? WagonIndex { get; }

        /// <summary>Only the steam engine's own holds - its fuel and its water.</summary>
        public bool EngineOnly { get; }

        public TrainSelector(BlockFacing direction, int? wagonIndex, bool engineOnly, int? distance = null)
        {
            Direction = direction;
            WagonIndex = wagonIndex;
            EngineOnly = engineOnly;
            Distance = distance;
        }
    }

    /// <summary>One storage point of one vehicle.</summary>
    public sealed class TrainHold : ICargoHold
    {
        private readonly YttPersistence persistence;
        private readonly Entity entity;
        private readonly InventoryBase inventory;
        private readonly Action<Entity> onSaved;

        public TrainHold(YttPersistence persistence, Entity entity, InventoryBase inventory, string code,
            Action<Entity> onSaved = null)
        {
            this.persistence = persistence;
            this.entity = entity;
            this.inventory = inventory;
            this.onSaved = onSaved;

            Code = code + " " + entity.EntityId;
        }

        public string Code { get; }

        public IInventory Inventory => inventory;

        /// <summary>A wagon is an inventory, not a place: its slots are written to directly.</summary>
        public BlockPos Pos => null;

        public bool IsEmpty => inventory.Empty;

        public void MarkDirty()
        {
            // A wagon saves itself off its dirty slots; an engine has to be asked.
            onSaved?.Invoke(entity);

            persistence.VerifyFirstTransfer(entity, snapshot);
        }

        /// <summary>Before anything moves, and only while that one check is still to come.</summary>
        public string TakeSnapshot()
        {
            if (persistence?.NeedsSnapshot != true) return null;

            snapshot ??= persistence.Snapshot(entity);
            return snapshot;
        }

        private string snapshot;
    }

    /// <summary>
    /// A train standing at the platform: every hold of every vehicle that matched, nearest first.
    /// </summary>
    public sealed class TrainHolder : ICargoHolder
    {
        public IReadOnlyList<ICargoHold> Holds { get; }

        /// <summary>Stopped - which for a train is what "ready" means.</summary>
        public bool IsReady { get; }

        public TrainHolder(IReadOnlyList<ICargoHold> holds, bool ready)
        {
            Holds = holds;
            IsReady = ready;
        }

        public void Refresh()
        {
            foreach (ICargoHold hold in Holds)
            {
                if (hold is TrainHold train) train.TakeSnapshot();
            }
        }
    }

    /// <summary>
    /// Finds the train standing at the dock. Vehicles are recognised by the domain of their
    /// entity code and the name of a behavior, never by a type.
    /// </summary>
    public class TrainCargoHolderFinder : ICargoHolderFinder
    {
        public const string KeywordText = "train";

        /// <summary>The mod whose vehicles these are.</summary>
        public const string Domain = "yangtransport";

        /// <summary>How far from the device to look. A boxcar alone is seven blocks long.</summary>
        public const int SearchRadius = 12;

        private readonly YttSurface surface;
        private readonly YttPersistence persistence;
        private readonly YttStorage storage;
        private readonly YttEngine engine;
        private readonly StandingWatch standing = new StandingWatch();
        private readonly VehicleSightings sightings = new VehicleSightings();

        /// <summary>
        /// The probe is left out when only the header vocabulary is wanted - reading a header must
        /// work with no game and no other mod.
        /// </summary>
        public TrainCargoHolderFinder(YttSurface surface = null, YttPersistence persistence = null)
        {
            this.surface = surface;
            this.persistence = persistence;
            storage = new YttStorage(surface, persistence);
            engine = new YttEngine(surface);
        }

        public string Keyword => KeywordText;

        /// <summary>
        /// A train moves, so nothing about it may be remembered between ticks - and stopped-or-not
        /// needs a fresh look anyway.
        /// </summary>
        public bool Cacheable => false;

        public bool TryParseHeader(IReadOnlyList<string> tokens, PaperErrorSink errors, out ICargoSelector selector)
        {
            selector = null;

            BlockFacing direction = null;
            int? distance = null;
            int? wagon = null;
            bool engine = false;

            foreach (string token in tokens ?? Array.Empty<string>())
            {
                BlockFacing facing = ParseDirection(token, out int? steps);

                if (facing != null)
                {
                    // Out of reach can never match, and a header that matches nothing is the
                    // hardest kind to debug. Say so while the paper is being written.
                    if (steps != null && (steps < 1 || steps > SearchRadius))
                    {
                        errors?.Add(token, "holderspec");
                        return false;
                    }

                    direction = facing;
                    distance = steps;
                    continue;
                }

                if (token.Equals("engine", StringComparison.OrdinalIgnoreCase))
                {
                    engine = true;
                    continue;
                }

                if (int.TryParse(token, out int index) && index >= 1)
                {
                    wagon = index;
                    continue;
                }

                // Reported rather than ignored, or the header quietly means something else.
                errors?.Add(token, "holderspec");
                return false;
            }

            selector = new TrainSelector(direction, wagon, engine, distance);
            return true;
        }

        /// <summary>
        /// North, south, east, west - or just their first letter - and optionally how many blocks
        /// away, written against the word: <c>north5</c>.
        ///
        /// Against the word, not after it, because <c>train north 5</c> would be arguing with the
        /// wagon number over the same token.
        /// </summary>
        private static BlockFacing ParseDirection(string token, out int? steps)
        {
            steps = null;

            string word = token.ToLowerInvariant();
            int digits = word.Length;

            while (digits > 0 && char.IsDigit(word[digits - 1])) digits--;

            if (digits < word.Length && digits > 0)
            {
                if (!int.TryParse(word.Substring(digits), out int parsed)) return null;

                steps = parsed;
                word = word.Substring(0, digits);
            }

            switch (word)
            {
                case "n": return BlockFacing.NORTH;
                case "s": return BlockFacing.SOUTH;
                case "e": return BlockFacing.EAST;
                case "w": return BlockFacing.WEST;
            }

            BlockFacing facing = BlockFacing.FromCode(word);

            return facing == BlockFacing.UP || facing == BlockFacing.DOWN ? null : facing;
        }

        public bool TryFind(IWorldAccessor world, BlockPos devicePos, ICargoSelector selector, out ICargoHolder holder)
        {
            holder = null;

            if (surface?.CanCarry != true || persistence?.Trusted != true) return false;
            if (world?.Api is not Vintagestory.API.Server.ICoreServerAPI) return false;

            TrainSelector wanted = selector as TrainSelector ?? new TrainSelector(null, null, false);
            Vec3d centre = devicePos.ToVec3d().Add(0.5, 0.5, 0.5);

            IReadOnlyList<Entity> found = VehiclesNear(world, devicePos, centre);
            if (found.Count == 0) return false;

            long now = world.ElapsedMilliseconds;
            List<Entity> vehicles = new List<Entity>();

            foreach (Entity entity in found)
            {
                if (!Reaches(entity, centre, wanted)) continue;

                vehicles.Add(entity);
            }

            // By age, not by what this one device can see: one finder serves every dock, and
            // clearing the rest wiped the other dock's train on every tick.
            standing.Forget(now);

            if (vehicles.Count == 0) return false;

            vehicles.Sort((a, b) => Distance(a, centre).CompareTo(Distance(b, centre)));

            List<ICargoHold> holds = new List<ICargoHold>();
            bool ready = true;

            foreach (Entity entity in vehicles)
            {
                if (!Matches(entity, wanted)) continue;

                int before = holds.Count;

                if (wanted.EngineOnly)
                {
                    // Only when asked for by name, or a firebox swallows every delivery's
                    // first coal on a plain `load train`.
                    InventoryBase inventory = engine.InventoryOf(entity);

                    if (inventory != null)
                    {
                        holds.Add(new TrainHold(persistence, entity, inventory, "engine", engine.Save));
                    }
                }
                else
                {
                    int index = 0;

                    foreach (InventoryBase inventory in storage.InventoriesOf(entity))
                    {
                        holds.Add(new TrainHold(persistence, entity, inventory, "wagon" + index++));
                    }
                }

                if (holds.Count == before) continue;

                // Every vehicle in use has to be standing; half a train stopped is not stopped.
                if (!standing.IsStanding(entity.EntityId, entity.Pos.X, entity.Pos.Y, entity.Pos.Z, now))
                {
                    ready = false;
                }
            }

            if (holds.Count == 0) return false;

            TrainHolder train = new TrainHolder(holds, ready);
            train.Refresh();

            holder = train;
            return true;
        }

        /// <summary>
        /// Every vehicle within reach of the device, from the last search or from a new one.
        ///
        /// What is remembered is ids, and only for half a second. They are exchanged for live
        /// entities on every call, so a position sample is always fresh and a vehicle that has
        /// gone comes back null instead of coming back writable.
        ///
        /// The search itself depends on nothing but the device's position - direction, wagon index
        /// and `engine` are applied to its result - so one sighting serves whatever the header says.
        /// </summary>
        private IReadOnlyList<Entity> VehiclesNear(IWorldAccessor world, BlockPos devicePos, Vec3d centre)
        {
            long now = world.ElapsedMilliseconds;
            long[] remembered = sightings.Recall(devicePos, now, id => world.GetEntityById(id) != null);

            if (remembered != null)
            {
                List<Entity> live = new List<Entity>(remembered.Length);

                foreach (long id in remembered) live.Add(world.GetEntityById(id));

                return live;
            }

            Entity[] found = world.GetEntitiesAround(centre, SearchRadius, SearchRadius, IsVehicle)
                ?? Array.Empty<Entity>();

            long[] ids = new long[found.Length];
            for (int i = 0; i < found.Length; i++) ids[i] = found[i].EntityId;

            sightings.Remember(devicePos, now, ids);

            return found;
        }

        /// <summary>
        /// By domain and behavior name, never by type - naming a type would mean referencing the
        /// other mod's assembly.
        ///
        /// A locomotive carries no SGStorage, only SteamPowered, so asking for cargo alone left it
        /// invisible and `load train engine` could never find one. Its holds still only appear when
        /// the header asks for them by name.
        /// </summary>
        private static bool IsVehicle(Entity entity)
        {
            if (entity?.Code?.Domain != Domain) return false;

            return entity.GetBehavior(YttSurface.StorageBehavior) != null
                || entity.GetBehavior(YttSurface.SteamBehavior) != null;
        }

        private static bool Matches(Entity entity, TrainSelector wanted)
        {
            if (wanted.WagonIndex == null) return true;

            return ConvoyIndex(entity) == wanted.WagonIndex.Value;
        }

        /// <summary>
        /// Where this vehicle rides in its convoy, counted from the head - assembled from plain
        /// attributes because the other mod publishes no index. The head is not necessarily the
        /// locomotive, and the order follows how the train was coupled, not which way it parked.
        /// </summary>
        private static int ConvoyIndex(Entity entity)
        {
            ITreeAttribute attributes = entity.Attributes;
            if (attributes == null) return 1;

            double distance = attributes.GetDouble("convoyDistanceBehindHead", 0);

            // Roughly a wagon length apart; the head itself is 1.
            return 1 + (int)System.Math.Round(distance / 1.2);
        }

        /// <summary>
        /// Does the vehicle reach where the header says? Tested on its BODY, not its middle - a
        /// boxcar is seven blocks long. See <see cref="TrainReach"/>.
        /// </summary>
        private static bool Reaches(Entity entity, Vec3d centre, TrainSelector wanted)
        {
            if (wanted.Direction == null) return true;

            Cuboidf box = entity.SelectionBox ?? new Cuboidf(-0.5f, 0, -0.5f, 0.5f, 1, 0.5f);

            return TrainReach.Covers(
                entity.Pos.X + box.X1, entity.Pos.X + box.X2,
                entity.Pos.Z + box.Z1, entity.Pos.Z + box.Z2,
                centre, wanted.Direction, wanted.Distance);
        }

        private static double Distance(Entity entity, Vec3d centre)
        {
            return entity.Pos.XYZ.SquareDistanceTo(centre);
        }
    }
}
