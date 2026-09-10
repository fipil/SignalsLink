using System;
using System.Reflection;
using SignalsLink.YTT.src.probe;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SignalsLink.YTT.src.train
{
    /// <summary>
    /// The steam engine of a locomotive: its firebox and its water, as one inventory.
    ///
    /// <b>It does not save the way the wagons do.</b> A boxcar writes itself down whenever a slot
    /// is marked dirty, so putting goods in one and marking the slot is the whole job. The engine
    /// keeps its inventory in the vehicle's own attributes and only writes it out when it is
    /// asked to, and the periodic write leaves the inventory OUT. Coal shovelled into the firebox
    /// and left at that would be there until the next reload and no longer.
    ///
    /// So every change here is followed by an explicit ask. That difference is the reason this is
    /// its own class rather than another case inside the storage one.
    /// </summary>
    public class YttEngine
    {
        private readonly YttSurface surface;

        public YttEngine(YttSurface surface)
        {
            this.surface = surface;
        }

        /// <summary>The engine's two slots, or null when this vehicle has no engine to reach.</summary>
        public InventoryBase InventoryOf(Entity entity)
        {
            if (surface?.CanReachEngine != true) return null;

            try
            {
                object controller = ControllerOf(entity);
                if (controller == null) return null;

                PropertyInfo inventory = controller.GetType().GetProperty("Inventory",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                return inventory?.GetValue(controller) as InventoryBase;
            }
            catch (Exception e)
            {
                Failed(entity, e);
                return null;
            }
        }

        /// <summary>
        /// Asks the vehicle to write its engine down, inventory and all. Called after every change,
        /// because nothing else will.
        /// </summary>
        public void Save(Entity entity)
        {
            if (surface?.CanReachEngine != true) return;

            try
            {
                EntityBehavior behavior = entity?.GetBehavior(YttSurface.SteamBehavior);
                if (behavior == null) return;

                surface.RequestSyncMethod?.Invoke(behavior, new object[] { true });
            }
            catch (Exception e)
            {
                Failed(entity, e);
            }
        }

        private object ControllerOf(Entity entity)
        {
            EntityBehavior behavior = entity?.GetBehavior(YttSurface.SteamBehavior);
            if (behavior == null) return null;

            return surface.EngineControllerMember switch
            {
                PropertyInfo property => property.GetValue(behavior),
                FieldInfo field => field.GetValue(behavior),
                _ => null,
            };
        }

        private bool complained;

        private void Failed(Entity entity, Exception e)
        {
            if (complained) return;

            complained = true;
            entity?.World?.Logger?.Error("[SignalsLink.YTT] could not reach a locomotive's engine: " + e
                + " Cargo is unaffected; stoking and watering are not available this session.");
        }
    }
}
