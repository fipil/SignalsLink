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

        /// <summary>Which vehicle, counted from the head of the convoy; null for all of them.</summary>
        public int? WagonIndex { get; }

        /// <summary>Only the steam engine's own holds - its fuel and its water.</summary>
        public bool EngineOnly { get; }

        public TrainSelector(BlockFacing direction, int? wagonIndex, bool engineOnly)
        {
            Direction = direction;
            WagonIndex = wagonIndex;
            EngineOnly = engineOnly;
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
            // A wagon saves itself off the back of the dirty slots the transfer already marked;
            // all that is left is to make sure it really did, once. An engine saves nothing on
            // its own and has to be asked.
            onSaved?.Invoke(entity);

            persistence.VerifyFirstTransfer(entity, snapshot);
        }

        /// <summary>Taken before anything is moved, for the check afterwards.</summary>
        public string TakeSnapshot()
        {
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
    /// Finds the train standing at the dock.
    ///
    /// Vehicles are recognised by the domain of their entity code and by the name of a behavior -
    /// never by a type. That is what keeps this bridge free of a reference to the other mod's
    /// assembly, and it is the difference between degrading and crashing when that mod changes.
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

        /// <summary>
        /// The probe is left out when only the header vocabulary is wanted - reading a header
        /// has to work with no game, no other mod and no instance of anything, which is the
        /// whole reason the keyword belongs to the finder rather than to a holder.
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
        /// A train moves, so nothing about it may be remembered between ticks - not the vehicles,
        /// not their inventories. It is also what keeps the stopped-or-not question answerable:
        /// that needs a fresh look every tick.
        /// </summary>
        public bool Cacheable => false;

        public bool TryParseHeader(IReadOnlyList<string> tokens, PaperErrorSink errors, out ICargoSelector selector)
        {
            selector = null;

            BlockFacing direction = null;
            int? wagon = null;
            bool engine = false;

            foreach (string token in tokens ?? Array.Empty<string>())
            {
                BlockFacing facing = ParseDirection(token);

                if (facing != null)
                {
                    direction = facing;
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

                // Reported rather than ignored: a header that quietly means something else than it
                // says is the worst kind of paper to debug.
                errors?.Add(token, "holderspec");
                return false;
            }

            selector = new TrainSelector(direction, wagon, engine);
            return true;
        }

        /// <summary>North, south, east, west - or just their first letter, which is what anyone
        /// writing this on a scrap of paper in a mine will reach for.</summary>
        private static BlockFacing ParseDirection(string token)
        {
            switch (token.ToLowerInvariant())
            {
                case "n": return BlockFacing.NORTH;
                case "s": return BlockFacing.SOUTH;
                case "e": return BlockFacing.EAST;
                case "w": return BlockFacing.WEST;
            }

            BlockFacing facing = BlockFacing.FromCode(token.ToLowerInvariant());

            return facing == BlockFacing.UP || facing == BlockFacing.DOWN ? null : facing;
        }

        public bool TryFind(IWorldAccessor world, BlockPos devicePos, ICargoSelector selector, out ICargoHolder holder)
        {
            holder = null;

            if (surface?.CanCarry != true || persistence?.Trusted != true) return false;
            if (world?.Api is not Vintagestory.API.Server.ICoreServerAPI) return false;

            TrainSelector wanted = selector as TrainSelector ?? new TrainSelector(null, null, false);
            Vec3d centre = devicePos.ToVec3d().Add(0.5, 0.5, 0.5);

            Entity[] found = world.GetEntitiesAround(centre, SearchRadius, SearchRadius, IsVehicle);
            if (found == null || found.Length == 0) return false;

            List<Entity> vehicles = new List<Entity>();
            HashSet<long> seen = new HashSet<long>();

            foreach (Entity entity in found)
            {
                if (!Reaches(entity, centre, wanted.Direction)) continue;

                vehicles.Add(entity);
                seen.Add(entity.EntityId);
            }

            // Vehicles that have left are forgotten, or every train that ever passed stays in
            // memory for the life of the world.
            standing.Forget(seen);

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
                    // Only when asked for by name. A firebox that quietly joined in on a plain
                    // `load train` would swallow the first coal of every delivery.
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

                // Every vehicle being used has to be standing. Half a train stopped is not a train
                // that can be unloaded - the other half is dragging it.
                if (!standing.IsStanding(entity.EntityId, entity.Pos.X, entity.Pos.Y, entity.Pos.Z))
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
        /// By the domain of the code and the name of a behavior, never by a type: naming a type
        /// would mean referencing the other mod's assembly, and then this bridge would fail to load
        /// at all rather than politely doing nothing.
        /// </summary>
        private static bool IsVehicle(Entity entity)
        {
            return entity?.Code?.Domain == Domain && entity.GetBehavior(YttSurface.StorageBehavior) != null;
        }

        private static bool Matches(Entity entity, TrainSelector wanted)
        {
            if (wanted.WagonIndex == null) return true;

            return ConvoyIndex(entity) == wanted.WagonIndex.Value;
        }

        /// <summary>
        /// Where this vehicle rides in its convoy, counted from the head.
        ///
        /// Assembled from plain attributes rather than from the other mod's own index, which is not
        /// published anywhere a bystander can read. Two things to know about it: the head is not
        /// necessarily the locomotive, and the order is stable against how the train was coupled,
        /// not against which end of the platform it stopped at. Picking wagons by what is IN them
        /// is the more reliable habit, and the paper can already do that.
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
        /// Does the vehicle reach the named side of the device?
        ///
        /// The test is on the vehicle's BODY, not on its middle. A boxcar is seven blocks long and
        /// hangs well past the device, so a test on the centre of a long train would answer
        /// differently from one tick to the next.
        /// </summary>
        private static bool Reaches(Entity entity, Vec3d centre, BlockFacing direction)
        {
            if (direction == null) return true;

            Cuboidf box = entity.SelectionBox ?? new Cuboidf(-0.5f, 0, -0.5f, 0.5f, 1, 0.5f);

            double minX = entity.Pos.X + box.X1;
            double maxX = entity.Pos.X + box.X2;
            double minZ = entity.Pos.Z + box.Z1;
            double maxZ = entity.Pos.Z + box.Z2;

            // North is -Z in Vintage Story.
            if (direction == BlockFacing.NORTH) return minZ < centre.Z;
            if (direction == BlockFacing.SOUTH) return maxZ > centre.Z;
            if (direction == BlockFacing.WEST) return minX < centre.X;
            if (direction == BlockFacing.EAST) return maxX > centre.X;

            return true;
        }

        private static double Distance(Entity entity, Vec3d centre)
        {
            return entity.Pos.XYZ.SquareDistanceTo(centre);
        }
    }
}
