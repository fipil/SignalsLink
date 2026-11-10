using System;
using System.Reflection;
using Vintagestory.API.Common;

namespace SignalsLink.YTT.src.probe
{
    /// <summary>
    /// What the stoker needs of a steam engine controller: whether it burns, what it holds, and
    /// how to light it or put it out. Looked up once at startup, on the TYPE, the way the rest
    /// of the probe works; null when any of it is missing or has changed its type.
    ///
    /// Kept out of the fingerprint, like the refrigerant slots: the stoker is an extra, and the
    /// known-good surfaces must not all turn unknown because of it.
    /// </summary>
    public sealed class FireMembers
    {
        private const BindingFlags Anywhere =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public PropertyInfo Burning { get; private set; }
        public PropertyInfo Temperature { get; private set; }
        public PropertyInfo FuelSlot { get; private set; }
        public PropertyInfo WaterSlot { get; private set; }
        public MethodInfo Ignite { get; private set; }
        public MethodInfo Extinguish { get; private set; }

        /// <summary>Static: what the other mod counts as a light. Asked, never hard-coded.</summary>
        public MethodInfo IsIgnitionSource { get; private set; }

        public static FireMembers On(Type controller)
        {
            if (controller == null) return null;

            FireMembers members = new FireMembers
            {
                Burning = Property(controller, "IsBurning", typeof(bool)),
                Temperature = Property(controller, "TemperatureC", typeof(double)),
                FuelSlot = Property(controller, "FuelSlot", typeof(ItemSlot)),
                WaterSlot = Property(controller, "WaterSlot", typeof(ItemSlot)),
                Ignite = Method(controller, "TryIgniteNow", typeof(void)),
                Extinguish = Method(controller, "ForceExtinguishBoiler", typeof(void)),
                IsIgnitionSource = Method(controller, "IsIgnitionSource", typeof(bool), typeof(ItemStack)),
            };

            if (members.Burning == null || members.Temperature == null || members.FuelSlot == null
                || members.WaterSlot == null || members.Ignite == null || members.Extinguish == null
                || members.IsIgnitionSource == null || !members.IsIgnitionSource.IsStatic) return null;

            return members;
        }

        private static PropertyInfo Property(Type type, string name, Type expected)
        {
            PropertyInfo found = type.GetProperty(name, Anywhere);

            if (found == null || !found.CanRead) return null;
            if (!expected.IsAssignableFrom(found.PropertyType)) return null;

            return found;
        }

        private static MethodInfo Method(Type type, string name, Type returns, params Type[] parameters)
        {
            MethodInfo found = type.GetMethod(name, Anywhere, null, parameters, null);

            if (found == null || found.ReturnType != returns) return null;

            return found;
        }
    }
}
