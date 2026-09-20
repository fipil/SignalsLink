using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.paperConditions
{
    /// <summary>
    /// <c>isBurning</c> — is the block in this scope on fire right now?
    ///
    /// Unlike the other conditions this one asks about a <b>block</b>, not about an item stack, so
    /// it reads the position out of the context (<c>targetBlockPos</c> / <c>sourceBlockPos</c>,
    /// falling back to <c>blockPos</c>) rather than looking at the inventory it is handed. It is an
    /// <see cref="IInventoryCondition"/> only because that is the interface which gets told which
    /// scope it is being evaluated in.
    ///
    /// When the host does not know a position for that scope the condition is simply false — never
    /// true — so a paper that asks about something the block cannot see stays quiet.
    /// </summary>
    public sealed class BlockBurningCondition : IInventoryCondition
    {
        private readonly bool expected;

        public BlockBurningCondition(bool expected)
        {
            this.expected = expected;
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

            bool? burning = BurningState.IsBurning(world, pos);
            return burning.HasValue && burning.Value == expected;
        }

        private static T Get<T>(IDictionary<string, object> ctx, string key) where T : class
        {
            return ctx != null && ctx.TryGetValue(key, out object value) ? value as T : null;
        }
    }

    /// <summary>
    /// Works out whether the block at a position is burning.
    ///
    /// There is no interface in the game for "I am on fire" — every burning thing exposes it in its
    /// own class: a firepit and a ground-storage pile have <c>IsBurning</c>, a charcoal pit has
    /// <c>Lit</c>, and the fire block carries a burning behavior. Rather than hard-coding that list
    /// (which would go stale and would never cover another mod), the block entity and each of its
    /// behaviors are asked whether they have a boolean member with one of those names. The lookup
    /// is resolved once per type and cached, so the per-call cost is a dictionary hit.
    /// </summary>
    public static class BurningState
    {
        /// <summary>Member names, in the order they are tried. First one found on a type wins.</summary>
        private static readonly string[] MemberNames = { "IsBurning", "Lit", "IsLit", "Burning" };

        // Type -> reader, or null when the type has no such member. Concurrent because room and
        // block-entity code does not all run on the main thread.
        private static readonly ConcurrentDictionary<Type, System.Func<object, bool>> readers =
            new ConcurrentDictionary<Type, System.Func<object, bool>>();

        /// <summary>
        /// True/false when the block can say, null when nothing at that position knows about fire —
        /// which the caller must not read as "not burning" if it matters.
        /// </summary>
        public static bool? IsBurning(IWorldAccessor world, BlockPos pos)
        {
            if (world?.BlockAccessor == null || pos == null) return null;

            BlockEntity be = world.BlockAccessor.GetBlockEntity(pos);
            if (be != null)
            {
                bool? fromEntity = Read(be);
                if (fromEntity.HasValue) return fromEntity;

                if (be.Behaviors != null)
                {
                    foreach (BlockEntityBehavior behavior in be.Behaviors)
                    {
                        bool? fromBehavior = Read(behavior);
                        if (fromBehavior.HasValue) return fromBehavior;
                    }
                }
            }

            // The fire block itself has no such member to read - being fire is the whole of it.
            Block block = world.BlockAccessor.GetBlock(pos);
            if (block?.Code?.Path != null && block.Code.Path.StartsWith("fire")) return true;

            return null;
        }

        private static bool? Read(object target)
        {
            if (target == null) return null;

            System.Func<object, bool> reader = readers.GetOrAdd(target.GetType(), BuildReader);
            return reader?.Invoke(target);
        }

        private static System.Func<object, bool> BuildReader(Type type)
        {
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

            foreach (string name in MemberNames)
            {
                PropertyInfo property = type.GetProperty(name, Flags);
                if (property != null && property.PropertyType == typeof(bool) && property.CanRead)
                {
                    return instance => SafeRead(() => (bool)property.GetValue(instance));
                }

                FieldInfo field = type.GetField(name, Flags);
                if (field != null && field.FieldType == typeof(bool))
                {
                    return instance => SafeRead(() => (bool)field.GetValue(instance));
                }
            }

            return null;
        }

        private static bool SafeRead(System.Func<bool> read)
        {
            // A getter can throw when the block entity is half-initialised (mid chunk load).
            // Nothing here is worth taking a server down for.
            try { return read(); }
            catch (Exception) { return false; }
        }
    }
}
