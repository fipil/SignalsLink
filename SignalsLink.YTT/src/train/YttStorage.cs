using System;
using System.Collections;
using System.Collections.Generic;
using SignalsLink.YTT.src.probe;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SignalsLink.YTT.src.train
{
    /// <summary>
    /// Reaches into a vehicle for its inventories.
    ///
    /// This is the whole of the reflection, and it is deliberately four lines of work behind a
    /// probe that has already checked every member it touches. When the other mod publishes an
    /// interface for this - which has been asked for - the inside of this class is what gets
    /// thrown away, and nothing else has to change.
    ///
    /// Everything is wrapped: an exception from here would come from another mod's insides on a
    /// server tick, and it must never be allowed to take the tick down. It is reported once and
    /// the bridge stands down.
    /// </summary>
    public class YttStorage
    {
        private static readonly InventoryBase[] None = Array.Empty<InventoryBase>();

        private readonly YttSurface surface;
        private readonly YttPersistence persistence;

        public YttStorage(YttSurface surface, YttPersistence persistence)
        {
            this.surface = surface;
            this.persistence = persistence;
        }

        /// <summary>
        /// The vehicle's holds, or nothing at all when they cannot be reached or cannot be trusted
        /// to save. Nothing is remembered between calls: a vehicle can leave or unload at any
        /// moment, and a stale inventory object would be written into a wagon that is no longer
        /// there.
        /// </summary>
        public IReadOnlyList<InventoryBase> InventoriesOf(Entity entity)
        {
            if (surface?.CanCarry != true || persistence?.Trusted != true) return None;

            EntityBehavior behavior = entity?.GetBehavior(YttSurface.StorageBehavior);
            if (behavior == null) return None;

            try
            {
                if (surface.StoragePointsField.GetValue(behavior) is not IEnumerable points) return None;

                List<InventoryBase> inventories = new List<InventoryBase>();

                foreach (object point in points)
                {
                    if (surface.StoragePointInventoryField.GetValue(point) is not InventoryBase inventory) continue;

                    // Before anything is written: is the other mod listening for the change, so
                    // that it gets saved? See YttPersistence.
                    if (!persistence.IsWired(inventory)) return None;

                    inventories.Add(inventory);
                }

                return inventories;
            }
            catch (Exception e)
            {
                Failed(entity, e);
                return None;
            }
        }

        private bool complained;

        private void Failed(Entity entity, Exception e)
        {
            if (complained) return;

            complained = true;
            entity?.World?.Logger?.Error("[SignalsLink.YTT] could not read a vehicle's holds: " + e
                + " This is what the surface probe is for; please report it with the fingerprint from startup.");
        }
    }
}
