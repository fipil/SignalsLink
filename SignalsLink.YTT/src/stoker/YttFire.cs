using System;
using SignalsLink.YTT.src.probe;
using SignalsLink.YTT.src.train;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SignalsLink.YTT.src.stoker
{
    /// <summary>
    /// A locomotive's fire, reached through the probe. Every call is wrapped: an exception from
    /// the other mod's insides must not take the server tick down, and after one the stoker
    /// stands down for the session rather than trying again every second.
    /// </summary>
    public class YttFire
    {
        private readonly YttSurface surface;
        private readonly YttEngine engine;
        private readonly ILogger logger;
        private bool failed;

        public YttFire(YttSurface surface, ILogger logger)
        {
            this.surface = surface;
            this.logger = logger;
            engine = new YttEngine(surface);
        }

        /// <summary>False when this version of the other mod does not offer a fire to tend.</summary>
        public bool Available => !failed && surface?.CanTendFire == true;

        /// <summary>Null when the vehicle has no engine, or it cannot be read.</summary>
        public bool? IsBurning(Entity locomotive)
        {
            return Read(locomotive, surface.Fire.Burning) is bool burning ? burning : null;
        }

        public double? TemperatureOf(Entity locomotive)
        {
            return Read(locomotive, surface.Fire.Temperature) is double temperature ? temperature : null;
        }

        public ItemSlot FuelSlotOf(Entity locomotive) => Read(locomotive, surface.Fire.FuelSlot) as ItemSlot;

        public ItemSlot WaterSlotOf(Entity locomotive) => Read(locomotive, surface.Fire.WaterSlot) as ItemSlot;

        /// <summary>What the other mod counts as a light. False when it cannot be asked.</summary>
        public bool IsIgnitionSource(ItemStack stack)
        {
            if (!Available || stack == null) return false;

            try
            {
                return surface.Fire.IsIgnitionSource.Invoke(null, new object[] { stack }) is true;
            }
            catch (Exception e)
            {
                Failed(null, e);
                return false;
            }
        }

        public void Ignite(Entity locomotive) => Call(locomotive, surface.Fire.Ignite);

        public void Extinguish(Entity locomotive) => Call(locomotive, surface.Fire.Extinguish);

        /// <summary>After taking water out of the engine, which saves nothing by itself.</summary>
        public void Save(Entity locomotive) => engine.Save(locomotive);

        private object Read(Entity locomotive, System.Reflection.PropertyInfo property)
        {
            if (!Available) return null;

            try
            {
                object controller = engine.ControllerOf(locomotive);
                return controller == null ? null : property.GetValue(controller);
            }
            catch (Exception e)
            {
                Failed(locomotive, e);
                return null;
            }
        }

        private void Call(Entity locomotive, System.Reflection.MethodInfo method)
        {
            if (!Available) return;

            try
            {
                object controller = engine.ControllerOf(locomotive);
                if (controller != null) method.Invoke(controller, null);
            }
            catch (Exception e)
            {
                Failed(locomotive, e);
            }
        }

        private void Failed(Entity entity, Exception e)
        {
            if (failed) return;

            failed = true;
            (entity?.World?.Logger ?? logger)?.Error("[SignalsLink.YTT] could not tend a locomotive's fire: " + e
                + " Cargo and the firebox are unaffected; stokers do nothing for the rest of this session.");
        }
    }
}
