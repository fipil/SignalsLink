using System;
using System.Collections.Generic;
using SignalsLink.src.signals.cargo;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SignalsLink.src.signals.vehicle
{
    /// <summary>What <c>unload boat north</c> or <c>load elk</c> asked for.</summary>
    public sealed class HungSelector : ICargoSelector
    {
        /// <summary>Narrows the search to one side of the device, or null for all round.</summary>
        public BlockFacing Direction { get; }

        /// <summary>How many blocks away, counted by stepping off the device; null for any.</summary>
        public int? Distance { get; }

        public HungSelector(BlockFacing direction, int? distance)
        {
            Direction = direction;
            Distance = distance;
        }
    }

    /// <summary>Every container hung on the vehicles that matched, nearest first.</summary>
    public sealed class HungHolder : ICargoHolder
    {
        public IReadOnlyList<ICargoHold> Holds { get; }

        public bool IsReady { get; }

        public HungHolder(IReadOnlyList<ICargoHold> holds, bool ready)
        {
            Holds = holds;
            IsReady = ready;
        }
    }

    /// <summary>
    /// Finds a vanilla vehicle or animal with containers hung on it - one finder per keyword,
    /// told what to look for.
    ///
    /// A boat has to have stopped before it is loaded; an animal is loaded whenever it is in
    /// reach. It wanders, and what it already carries in its bags leaves with it - which is what
    /// loading it was for.
    /// </summary>
    public class HungCargoHolderFinder : ICargoHolderFinder
    {
        /// <summary>How far from the device to look. A sailed boat is long.</summary>
        public const int SearchRadius = 12;

        private readonly System.Func<Entity, bool> matches;
        private readonly bool mustStand;
        private readonly StandingWatch standing = new StandingWatch();
        private readonly VehicleSightings sightings = new VehicleSightings();

        public HungCargoHolderFinder(string keyword, System.Func<Entity, bool> matches, bool mustStand)
        {
            Keyword = keyword;
            this.matches = matches;
            this.mustStand = mustStand;
        }

        /// <summary>Vanilla boats: chests, baskets and crates on a sailed boat, baskets on a raft.</summary>
        public static HungCargoHolderFinder Boats()
        {
            return new HungCargoHolderFinder("boat", entity => LooksLikeBoat(entity?.Code) && HungContainers.IsCarrier(entity), mustStand: true);
        }

        /// <summary>A tamed elk with saddlebags on.</summary>
        public static HungCargoHolderFinder Elks()
        {
            return new HungCargoHolderFinder("elk", entity => LooksLikeElk(entity?.Code) && HungContainers.IsCarrier(entity), mustStand: false);
        }

        public static bool LooksLikeBoat(AssetLocation code)
        {
            return code != null && code.Domain == "game" && code.Path.StartsWith("boat");
        }

        /// <summary>
        /// The game calls the tamed elk "tameddeer" - the file is elk-tamed.json, the entity is
        /// not. Matched on the entity code, because that is what a loaded entity carries.
        /// </summary>
        public static bool LooksLikeElk(AssetLocation code)
        {
            return code != null && code.Domain == "game" && code.Path.StartsWith("tameddeer");
        }

        public string Keyword { get; }

        /// <summary>It moves, so nothing about it is remembered between ticks.</summary>
        public bool Cacheable => false;

        public bool TryParseHeader(IReadOnlyList<string> tokens, PaperErrorSink errors, out ICargoSelector selector)
        {
            selector = null;

            BlockFacing direction = null;
            int? distance = null;

            foreach (string token in tokens ?? Array.Empty<string>())
            {
                BlockFacing facing = HeaderDirection.TryParse(token, out int? steps);

                // Reported rather than ignored, or the header quietly means something else.
                if (facing == null || (steps != null && (steps < 1 || steps > SearchRadius)))
                {
                    errors?.Add(token, "vehiclespec");
                    return false;
                }

                direction = facing;
                distance = steps;
            }

            selector = new HungSelector(direction, distance);
            return true;
        }

        public bool TryFind(IWorldAccessor world, BlockPos devicePos, ICargoSelector selector, out ICargoHolder holder)
        {
            holder = null;
            if (world?.Api is not ICoreServerAPI || devicePos == null) return false;

            HungSelector wanted = selector as HungSelector ?? new HungSelector(null, null);
            Vec3d centre = devicePos.ToVec3d().Add(0.5, 0.5, 0.5);
            long now = world.ElapsedMilliseconds;

            List<Entity> vehicles = new List<Entity>();

            foreach (Entity entity in VehiclesNear(world, devicePos, centre))
            {
                if (entity != null && Reaches(entity, centre, wanted)) vehicles.Add(entity);
            }

            standing.Forget(now);

            if (vehicles.Count == 0) return false;

            vehicles.Sort((a, b) => a.Pos.XYZ.SquareDistanceTo(centre).CompareTo(b.Pos.XYZ.SquareDistanceTo(centre)));

            List<ICargoHold> holds = new List<ICargoHold>();
            bool ready = true;

            foreach (Entity entity in vehicles)
            {
                int before = holds.Count;
                holds.AddRange(HungContainers.HoldsOf(entity, Keyword));

                if (holds.Count == before) continue;

                if (mustStand && !standing.IsStanding(entity.EntityId, entity.Pos.X, entity.Pos.Y, entity.Pos.Z, now))
                {
                    ready = false;
                }
            }

            if (holds.Count == 0) return false;

            holder = new HungHolder(holds, ready);
            return true;
        }

        /// <summary>
        /// Every matching entity within reach, from the last search or a new one. Only ids are
        /// remembered, and only for half a second; they are exchanged for live entities on every
        /// call, so one that has gone comes back null instead of writable.
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

            Entity[] found = world.GetEntitiesAround(centre, SearchRadius, SearchRadius, entity => matches(entity))
                ?? Array.Empty<Entity>();

            long[] ids = new long[found.Length];
            for (int i = 0; i < found.Length; i++) ids[i] = found[i].EntityId;

            sightings.Remember(devicePos, now, ids);

            return found;
        }

        /// <summary>Tested on the body, not the middle - a sailed boat is long.</summary>
        private static bool Reaches(Entity entity, Vec3d centre, HungSelector wanted)
        {
            if (wanted.Direction == null) return true;

            Cuboidf box = entity.SelectionBox ?? new Cuboidf(-0.5f, 0, -0.5f, 0.5f, 1, 0.5f);

            return BodyReach.Covers(
                entity.Pos.X + box.X1, entity.Pos.X + box.X2,
                entity.Pos.Z + box.Z1, entity.Pos.Z + box.Z2,
                centre, wanted.Direction, wanted.Distance);
        }
    }
}
