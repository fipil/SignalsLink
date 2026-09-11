using System;
using System.Reflection;
using SignalsLink.YTT.src.probe;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SignalsLink.YTT.src.train
{
    /// <summary>
    /// The steam engine's firebox and water, as one inventory.
    ///
    /// It does NOT save the way wagons do - a dirty slot is not enough, and the periodic write
    /// leaves the inventory out. Every change here is followed by an explicit
    /// <see cref="Save"/>, which is why this is its own class.
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

        /// <summary>Called after every change, because nothing else will.</summary>
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
