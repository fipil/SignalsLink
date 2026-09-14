using System.Collections;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace YttRepro
{
    /// <summary>
    /// The only place that knows YTT member names. Everything is looked up by name at call time,
    /// so this mod never references yangtransport.dll and survives a rebuild of it.
    /// </summary>
    internal static class YttPeek
    {
        private const BindingFlags Anywhere =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public static ModSystem System(ICoreAPI api, string shortName)
        {
            return api.ModLoader.GetModSystem("YangTransport." + shortName);
        }

        /// <summary>The entity seen as YTT's public IRailwayConvoyVehicle, or null.</summary>
        public static bool IsVehicle(Entity entity)
        {
            return entity != null && entity.GetType().GetInterface("IRailwayConvoyVehicle") != null;
        }

        /// <summary>A field or property by name, private or not, declared anywhere up the hierarchy.</summary>
        public static object Get(object target, string name)
        {
            if (target == null) return null;

            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, Anywhere | BindingFlags.DeclaredOnly);
                if (field != null) return field.GetValue(target);

                PropertyInfo property = type.GetProperty(name, Anywhere | BindingFlags.DeclaredOnly);
                if (property != null && property.GetIndexParameters().Length == 0) return property.GetValue(target);
            }

            foreach (Type contract in target.GetType().GetInterfaces())
            {
                PropertyInfo property = contract.GetProperty(name);
                if (property != null) return property.GetValue(target);
            }

            return null;
        }

        public static long GetLong(object target, string name) => Convert.ToInt64(Get(target, name) ?? 0L);
        public static int GetInt(object target, string name) => Convert.ToInt32(Get(target, name) ?? 0);
        public static bool GetBool(object target, string name) => Get(target, name) is bool b && b;

        /// <summary>A public or private method by name and argument count.</summary>
        public static object Call(object target, string name, params object[] args)
        {
            if (target == null) return null;

            foreach (MethodInfo method in target.GetType().GetMethods(Anywhere))
            {
                if (method.Name != name || method.GetParameters().Length != args.Length) continue;
                return method.Invoke(target, args);
            }

            throw new MissingMethodException(target.GetType().Name, name);
        }

        /// <summary>Entry of a private Dictionary&lt;long, T&gt; by key, or null.</summary>
        public static object Entry(object dictionary, long key)
        {
            return dictionary is IDictionary map && map.Contains(key) ? map[key] : null;
        }

        public static int Count(object collection)
        {
            return collection is ICollection c ? c.Count : -1;
        }
    }
}
