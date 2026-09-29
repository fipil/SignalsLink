using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.paperConditions
{
    /// <summary>
    /// <c>blockTemperature&gt;N</c> — how hot is the block in this scope? Asks the block, exactly
    /// like <see cref="BlockBurningCondition"/>: the position comes from the context, the inventory
    /// handed in is ignored. A block that has no temperature never satisfies it, negated or not -
    /// a paper asking about heat where there is none stays quiet.
    /// </summary>
    public sealed class BlockTemperatureCondition : IInventoryCondition
    {
        private readonly string op;
        private readonly double value;
        private readonly bool negate;

        public BlockTemperatureCondition(string op, double value, bool negate = false)
        {
            this.op = op;
            this.value = value;
            this.negate = negate;
        }

        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx)
        {
            return EvaluateAt(ctx, InventoryConditionScope.Source);
        }

        public bool Evaluate(ItemStack stack, IInventory inventory, IDictionary<string, object> ctx, InventoryConditionScope scope, bool isSelectionEvaluation)
        {
            return EvaluateAt(ctx, scope);
        }

        private bool EvaluateAt(IDictionary<string, object> ctx, InventoryConditionScope scope)
        {
            IWorldAccessor world = Get<IWorldAccessor>(ctx, "world");
            BlockPos pos = Get<BlockPos>(ctx, scope == InventoryConditionScope.Target ? "targetBlockPos" : "sourceBlockPos")
                        ?? Get<BlockPos>(ctx, "blockPos");

            if (world == null || pos == null) return false;

            double? temperature = BlockTemperatureState.Temperature(world, pos);
            if (!temperature.HasValue) return false;

            bool holds = op switch
            {
                ">" => temperature.Value > value,
                ">=" => temperature.Value >= value,
                "<" => temperature.Value < value,
                "<=" => temperature.Value <= value,
                _ => Math.Abs(temperature.Value - value) < 0.0001
            };
            return negate ? !holds : holds;
        }

        private static T Get<T>(IDictionary<string, object> ctx, string key) where T : class
        {
            return ctx != null && ctx.TryGetValue(key, out object value) ? value as T : null;
        }
    }

    /// <summary>
    /// Reads the temperature of the block at a position, if it has one. As with burning there is
    /// no interface for it: a firepit has <c>furnaceTemperature</c>, an oven <c>ovenTemperature</c>,
    /// EP generators <c>GenTemp</c>. The block entity and its behaviors are asked for a numeric
    /// member with one of those names; resolved once per type and cached.
    /// </summary>
    public static class BlockTemperatureState
    {
        private static readonly string[] MemberNames = { "furnaceTemperature", "ovenTemperature", "OvenTemperature", "GenTemp", "Temperature", "temperature" };

        private static readonly ConcurrentDictionary<Type, System.Func<object, double?>> readers = new ConcurrentDictionary<Type, System.Func<object, double?>>();

        public static double? Temperature(IWorldAccessor world, BlockPos pos)
        {
            if (world?.BlockAccessor == null || pos == null) return null;

            BlockEntity be = world.BlockAccessor.GetBlockEntity(pos);
            if (be == null) return null;

            double? fromEntity = Read(be);
            if (fromEntity.HasValue) return fromEntity;

            if (be.Behaviors != null)
            {
                foreach (BlockEntityBehavior behavior in be.Behaviors)
                {
                    double? fromBehavior = Read(behavior);
                    if (fromBehavior.HasValue) return fromBehavior;
                }
            }

            return null;
        }

        private static double? Read(object target)
        {
            if (target == null) return null;
            System.Func<object, double?> reader = readers.GetOrAdd(target.GetType(), BuildReader);
            return reader?.Invoke(target);
        }

        private static System.Func<object, double?> BuildReader(Type type)
        {
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

            foreach (string name in MemberNames)
            {
                PropertyInfo property = type.GetProperty(name, Flags);
                if (property != null && property.CanRead && IsNumber(property.PropertyType))
                {
                    return instance => SafeRead(() => Convert.ToDouble(property.GetValue(instance)));
                }

                FieldInfo field = type.GetField(name, Flags);
                if (field != null && IsNumber(field.FieldType))
                {
                    return instance => SafeRead(() => Convert.ToDouble(field.GetValue(instance)));
                }
            }

            return null;
        }

        private static bool IsNumber(Type type) => type == typeof(float) || type == typeof(double) || type == typeof(int);

        private static double? SafeRead(System.Func<double> read)
        {
            try { return read(); }
            catch (Exception) { return null; }
        }
    }
}
