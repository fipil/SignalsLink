using System;
using System.Collections;
using System.Collections.Generic;
using SignalsLink.YTT.src.probe;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SignalsLink.YTT.src.train
{
    /// <summary>
    /// Reaches into a vehicle for its inventories. The whole of the reflection lives here, behind
    /// a probe that has already checked every member it touches, and everything is wrapped: an
    /// exception from another mod's insides must not take the server tick down.
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
        /// The vehicle's holds, or nothing when they cannot be reached or trusted to save. Nothing
        /// is remembered between calls - a stale inventory belongs to a wagon that has left.
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

                    // Is the other mod listening, so the change gets saved? See YttPersistence.
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
