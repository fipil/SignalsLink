using System;
using System.Reflection;

namespace SignalsLink.YTT.src.probe
{
    /// <summary>
    /// What the signalman needs of a steam engine controller to set its power: the thermostat,
    /// its lock, and the locomotive's safe maximum. Null when any of it is missing or changed.
    ///
    /// The thermostat has a PRIVATE setter and the only public way in is a packet that needs a
    /// real player, so this is the bridge's first WRITE into the other mod's insides. Kept out of
    /// the fingerprint and behind a trust of its own, so a change here costs the signalman's
    /// power command and nothing else.
    /// </summary>
    public sealed class ThrottleMembers
    {
        private const BindingFlags Anywhere =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>The target temperature; readable, and settable through its private setter.</summary>
        public PropertyInfo Limit { get; private set; }

        public PropertyInfo Locked { get; private set; }

        public PropertyInfo Configuration { get; private set; }

        /// <summary>On the configuration: a field today, a property tomorrow; either will do.</summary>
        public MemberInfo MaxTemperature { get; private set; }

        public static ThrottleMembers On(Type controller)
        {
            if (controller == null) return null;

            PropertyInfo limit = controller.GetProperty("LimitTemperatureC", Anywhere);
            if (limit == null || limit.PropertyType != typeof(double) || !limit.CanRead) return null;
            if (limit.GetSetMethod(true) == null) return null;

            PropertyInfo locked = controller.GetProperty("ThermostatLockedToMaximum", Anywhere);
            if (locked == null || locked.PropertyType != typeof(bool) || !locked.CanRead) return null;

            PropertyInfo configuration = controller.GetProperty("Configuration", Anywhere);
            if (configuration == null || !configuration.CanRead) return null;

            MemberInfo max = (MemberInfo)configuration.PropertyType.GetField("MaxTemperatureCelsius", Anywhere)
                ?? configuration.PropertyType.GetProperty("MaxTemperatureCelsius", Anywhere);

            if (TypeOf(max) != typeof(double)) return null;

            return new ThrottleMembers { Limit = limit, Locked = locked, Configuration = configuration, MaxTemperature = max };
        }

        public static Type TypeOf(MemberInfo member)
        {
            return member switch
            {
                FieldInfo field => field.FieldType,
                PropertyInfo property => property.CanRead ? property.PropertyType : null,
                _ => null,
            };
        }

        public static object Read(MemberInfo member, object target)
        {
            return member switch
            {
                FieldInfo field => field.GetValue(target),
                PropertyInfo property => property.GetValue(target),
                _ => null,
            };
        }
    }
}
