using System;
using System.Collections.Generic;
using SignalsLink.src.signals.chunkanchor;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace SignalsLink.YTT.src.train
{
    /// <summary>
    /// Every loaded convoy, as the chunk columns its vehicles stand in, so a chunk anchor never
    /// unloads half of one.
    ///
    /// No reflection: a vehicle carries its convoy's head id as an ordinary entity attribute, and
    /// that is all this needs. It therefore works whatever the surface probe made of the rest.
    /// </summary>
    public sealed class TrainKeepTogether : IKeepTogetherSource
    {
        /// <summary>
        /// Set on every vehicle of a coupled convoy, the head included. A mine cart keeps it in the
        /// entity's plain attributes only; a locomotive mirrors it into the watched ones as well -
        /// so the plain ones are what to read, and the watched ones are only a fallback.
        /// </summary>
        public const string HeadAttribute = "convoyHeadId";

        public static long HeadOf(Entity entity)
        {
            if (entity == null) return 0;

            long head = entity.Attributes?.GetLong(HeadAttribute, 0) ?? 0;
            if (head != 0) return head;

            return entity.WatchedAttributes?.GetLong(HeadAttribute, 0) ?? 0;
        }

        public IEnumerable<IReadOnlyCollection<long>> Groups(IWorldAccessor world, System.Func<double, double, long> columnOf)
        {
            if (world is not IServerWorldAccessor server || columnOf == null) yield break;

            Dictionary<long, HashSet<long>> byHead = new Dictionary<long, HashSet<long>>();

            foreach (Entity entity in server.LoadedEntities.Values)
            {
                if (entity?.Code?.Domain != TrainCargoHolderFinder.Domain) continue;

                long head = HeadOf(entity);
                if (head == 0) continue;

                if (!byHead.TryGetValue(head, out HashSet<long> columns))
                {
                    columns = new HashSet<long>();
                    byHead[head] = columns;
                }

                columns.Add(columnOf(entity.ServerPos.X, entity.ServerPos.Z));
            }

            foreach (HashSet<long> columns in byHead.Values)
            {
                if (columns.Count > 1) yield return columns;
            }
        }
    }
}
