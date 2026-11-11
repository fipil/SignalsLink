using System.Collections.Generic;
using SignalsLink.src.signals.vehicle;
using SignalsLink.YTT.src.probe;
using SignalsLink.YTT.src.train;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SignalsLink.YTT.src.stoker
{
    /// <summary>What a look down the track found.</summary>
    public sealed class LocomotiveSighting
    {
        /// <summary>The engine of the train standing on the watched block, or null.</summary>
        public Entity Locomotive { get; init; }

        /// <summary>Some vehicle is on the watched block, engine or not.</summary>
        public bool Present { get; init; }

        /// <summary>The convoy on the block: its head's id, or the lone vehicle's own. Zero when none.</summary>
        public long ConvoyHead { get; init; }

        /// <summary>Every vehicle on the watched block has stopped.</summary>
        public bool Standing { get; init; }

        /// <summary>The vehicles on the watched block, nearest first. Never kept between ticks.</summary>
        public IReadOnlyList<Entity> Vehicles { get; init; } = System.Array.Empty<Entity>();

        /// <summary>Why there is no locomotive, as a lang key suffix: notrain, noengine.</summary>
        public string Reason { get; init; }
    }

    /// <summary>
    /// Finds the locomotive of the train standing on the block N steps in front of a device.
    ///
    /// Vehicles are recognised by domain and behavior name, never by type; the convoy is read off
    /// a plain attribute; nothing is remembered between calls but position samples, and those
    /// only by entity id. No reflection anywhere.
    /// </summary>
    public class LocomotiveFinder
    {
        /// <summary>Added to the reach when asking the world: a locomotive is seven blocks long.</summary>
        public const int BodyAllowance = 8;

        /// <summary>
        /// How far above or below the device the track may run. A sunken or raised track beside
        /// the platform is still this platform's track; a bridge over the station is not.
        /// </summary>
        public const int HeightTolerance = 2;

        private readonly StandingWatch standing = new StandingWatch();

        public LocomotiveSighting Find(IWorldAccessor world, BlockPos pos, BlockFacing facing, int reach)
        {
            Vec3d centre = pos.ToVec3d().Add(0.5, 0.5, 0.5);
            int radius = reach + BodyAllowance;

            Entity[] found = world.GetEntitiesAround(centre, radius, HeightTolerance + 2, TrainCargoHolderFinder.IsVehicle)
                ?? System.Array.Empty<Entity>();

            long now = world.ElapsedMilliseconds;
            List<Entity> covering = new List<Entity>();

            foreach (Entity entity in found)
            {
                if (WithinHeight(entity.Pos.Y, pos.Y) && Covers(entity, centre, facing, reach)) covering.Add(entity);
            }

            standing.Forget(now);

            if (covering.Count == 0) return new LocomotiveSighting { Reason = "notrain" };

            // Half a train stopped is not stopped.
            bool still = true;
            Entity locomotive = null;

            foreach (Entity entity in covering)
            {
                if (!standing.IsStanding(entity.EntityId, entity.Pos.X, entity.Pos.Y, entity.Pos.Z, now)) still = false;
                if (locomotive == null && HasEngine(entity)) locomotive = entity;
            }

            locomotive ??= ConvoyLocomotive(world, covering);

            long head = TrainKeepTogether.HeadOf(covering[0]);
            if (head == 0) head = covering[0].EntityId;

            if (locomotive == null)
            {
                return new LocomotiveSighting { Present = true, ConvoyHead = head, Standing = still, Vehicles = covering, Reason = "noengine" };
            }

            return new LocomotiveSighting { Present = true, ConvoyHead = head, Locomotive = locomotive, Standing = still, Vehicles = covering };
        }

        /// <summary>
        /// The block that many steps off the device, tested on the vehicle's BODY: a locomotive
        /// is seven blocks long and a test on its middle would answer differently from tick to
        /// tick.
        /// </summary>
        private static bool Covers(Entity entity, Vec3d centre, BlockFacing facing, int reach)
        {
            Cuboidf box = entity.SelectionBox ?? new Cuboidf(-0.5f, 0, -0.5f, 0.5f, 1, 0.5f);

            return BodyReach.Covers(
                entity.Pos.X + box.X1, entity.Pos.X + box.X2,
                entity.Pos.Z + box.Z1, entity.Pos.Z + box.Z2,
                centre, facing, reach);
        }

        /// <summary>
        /// The vehicle's feet within the tolerance of the device's floor. Half a block of slack,
        /// because a vehicle sits a little above its rail and a rail a little above its block.
        /// </summary>
        public static bool WithinHeight(double vehicleY, int deviceY)
        {
            return System.Math.Abs(vehicleY - deviceY) <= HeightTolerance + 0.5;
        }

        public static bool HasEngine(Entity entity) => entity?.GetBehavior(YttSurface.SteamBehavior) != null;

        /// <summary>The convoy a vehicle belongs to: its head's id, or its own when it rides alone.</summary>
        public static long ConvoyOf(Entity vehicle)
        {
            long head = TrainKeepTogether.HeadOf(vehicle);
            return head != 0 ? head : vehicle?.EntityId ?? 0;
        }

        /// <summary>The loaded engine of a convoy, wherever in the train it rides; null when none is loaded.</summary>
        public static Entity LocomotiveOfConvoy(IWorldAccessor world, long convoyHead)
        {
            if (world is not IServerWorldAccessor server || convoyHead == 0) return null;

            foreach (Entity other in server.LoadedEntities.Values)
            {
                if (other?.Code?.Domain != TrainCargoHolderFinder.Domain || !HasEngine(other)) continue;
                if (ConvoyOf(other) == convoyHead) return other;
            }

            return null;
        }

        /// <summary>
        /// The engine of the same convoy, wherever in the train it rides. NOT through the head of
        /// the convoy: the head is simply how the player coupled it, and need not be the engine.
        /// </summary>
        private static Entity ConvoyLocomotive(IWorldAccessor world, List<Entity> covering)
        {
            if (world is not IServerWorldAccessor server) return null;

            HashSet<long> convoys = new HashSet<long>();

            foreach (Entity entity in covering)
            {
                long head = TrainKeepTogether.HeadOf(entity);
                if (head != 0) convoys.Add(head);
            }

            if (convoys.Count == 0) return null;

            foreach (Entity other in server.LoadedEntities.Values)
            {
                if (other?.Code?.Domain != TrainCargoHolderFinder.Domain || !HasEngine(other)) continue;

                long head = TrainKeepTogether.HeadOf(other);
                if (head == 0) head = other.EntityId;

                if (convoys.Contains(head)) return other;
            }

            return null;
        }
    }
}
