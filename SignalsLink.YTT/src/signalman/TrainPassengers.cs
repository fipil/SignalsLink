using SignalsLink.YTT.src.train;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SignalsLink.YTT.src.signalman
{
    /// <summary>
    /// How many players ride a train. Counted from the PLAYERS, not from the vehicles: a player
    /// on a seat has the seat in plain vanilla API, and from the seat the vehicle - the same two
    /// steps the other mod takes itself. No reflection, no knowledge of its seats.
    /// </summary>
    public static class TrainPassengers
    {
        public static int Players(IWorldAccessor world, long convoyHead)
        {
            if (world == null || convoyHead == 0) return 0;

            int players = 0;

            foreach (IPlayer player in world.AllOnlinePlayers)
            {
                IMountableSeat seat = player?.Entity?.MountedOn;
                if (seat == null) continue;

                Entity vehicle = seat.Entity ?? seat.MountSupplier?.OnEntity;
                if (vehicle?.Code?.Domain != TrainCargoHolderFinder.Domain) continue;

                long head = TrainKeepTogether.HeadOf(vehicle);
                if (head == 0) head = vehicle.EntityId;

                if (head == convoyHead) players++;
            }

            return players;
        }
    }
}
