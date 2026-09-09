using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.paperConditions
{
    public sealed class InventoryAmountCondition : IInventoryCondition
    {
        private readonly ICondition innerCondition;
        private readonly decimal expectedAmount;
        private readonly InventoryAmountComparison comparison;

        /// <summary>
        /// The slot this condition asks about (1-based, as everywhere else in a paper), or null
        /// for the whole inventory.
        /// </summary>
        public int? SlotNumber { get; }

        public InventoryAmountCondition(ICondition innerCondition, decimal expectedAmount, InventoryAmountComparison comparison, int? slotNumber = null)
        {
            this.innerCondition = innerCondition ?? FalseCondition.Instance;
            this.expectedAmount = expectedAmount;
            this.comparison = comparison;
            SlotNumber = slotNumber;
        }

        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx)
        {
            return innerCondition.Evaluate(stack, ctx);
        }

        public bool Evaluate(ItemStack stack, IInventory inventory, IDictionary<string, object> ctx, InventoryConditionScope scope, bool isSelectionEvaluation)
        {
            if (inventory == null) return false;

            // A condition naming a slot asks about the INVENTORY, never about the item being
            // carried, so it takes no part in choosing what to carry. It is a gate: "while slot 5
            // holds at least ten planks". Without this exemption a source-scoped `slot N` line
            // would demand that the candidate match its code too, and a paper that asks about one
            // material while moving another could never fire.
            if (SlotNumber.HasValue)
            {
                return Compare(InventoryConditionResolver.GetSlotAmount(inventory, SlotNumber.Value, ctx, innerCondition));
            }

            if (isSelectionEvaluation && scope == InventoryConditionScope.Source)
            {
                if (stack?.Collectible == null) return false;
                if (!innerCondition.Evaluate(stack, ctx)) return false;
            }

            decimal actualAmount = InventoryConditionResolver.GetMatchingAmount(inventory, ctx, innerCondition);

            return Compare(actualAmount);
        }

        private bool Compare(decimal actualAmount)
        {
            return comparison switch
            {
                InventoryAmountComparison.AtLeast => actualAmount >= expectedAmount,
                InventoryAmountComparison.AtMost => actualAmount <= expectedAmount,
                _ => actualAmount == expectedAmount
            };
        }
    }

    public enum InventoryAmountComparison
    {
        Exact,
        AtLeast,
        AtMost
    }

    public static class InventoryConditionResolver
    {
        public static bool AnyMatch(IInventory inventory, IDictionary<string, object> ctx, ICondition condition)
        {
            if (inventory == null || condition == null) return false;

            foreach (var slot in inventory)
            {
                if (slot?.Empty != false) continue;

                ItemStack slotStack = slot.Itemstack;
                if (slotStack?.Collectible == null) continue;

                if (condition.Evaluate(slotStack, ctx))
                {
                    return true;
                }

                if (slotStack.Block is BlockLiquidContainerBase liquidContainer)
                {
                    ItemStack contentStack = liquidContainer.GetContent(slotStack);
                    if (contentStack?.Collectible != null && condition.Evaluate(contentStack, ctx))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// How much of what the condition asks for sits in ONE slot. A slot number out of range
        /// reads as zero rather than as "unknown": an inventory simply does not have that slot,
        /// and the honest answer to "how much is in it" is none.
        /// </summary>
        public static decimal GetSlotAmount(IInventory inventory, int slotNumber, IDictionary<string, object> ctx, ICondition condition)
        {
            if (inventory == null || condition == null) return 0;

            int index = slotNumber - 1;
            if (index < 0 || index >= inventory.Count) return 0;

            ItemSlot slot = inventory[index];
            if (slot?.Empty != false) return 0;

            ItemStack slotStack = slot.Itemstack;
            if (slotStack?.Collectible == null) return 0;

            if (condition.Evaluate(slotStack, ctx)) return GetStackAmount(slotStack);

            if (slotStack.Block is BlockLiquidContainerBase liquidContainer)
            {
                ItemStack contentStack = liquidContainer.GetContent(slotStack);
                if (contentStack?.Collectible != null && condition.Evaluate(contentStack, ctx))
                {
                    return GetStackAmount(contentStack);
                }
            }

            return 0;
        }

        public static decimal GetMatchingAmount(IInventory inventory, IDictionary<string, object> ctx, ICondition condition)
        {
            if (inventory == null || condition == null) return 0;

            decimal totalAmount = 0;

            foreach (var slot in inventory)
            {
                if (slot?.Empty != false) continue;

                ItemStack slotStack = slot.Itemstack;
                if (slotStack?.Collectible == null) continue;

                if (condition.Evaluate(slotStack, ctx))
                {
                    totalAmount += GetStackAmount(slotStack);
                    continue;
                }

                if (slotStack.Block is BlockLiquidContainerBase liquidContainer)
                {
                    ItemStack contentStack = liquidContainer.GetContent(slotStack);
                    if (contentStack?.Collectible != null && condition.Evaluate(contentStack, ctx))
                    {
                        totalAmount += GetStackAmount(contentStack);
                    }
                }
            }

            return totalAmount;
        }

        private static decimal GetStackAmount(ItemStack stack)
        {
            var props = BlockLiquidContainerBase.GetContainableProps(stack);
            if (props != null && props.ItemsPerLitre > 0)
            {
                return decimal.Round(stack.StackSize / (decimal)props.ItemsPerLitre, 2, MidpointRounding.ToZero);
            }

            return stack?.StackSize ?? 0;
        }
    }

    public class InventoryAnyCondition : ICondition
    {
        private readonly ICondition inner;

        public InventoryAnyCondition(ICondition inner)
        {
            this.inner = inner ?? FalseCondition.Instance;
        }

        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx)
        {
            if (ctx == null)
            {
                return false;
            }

            if (!ctx.TryGetValue("inventory", out var obj) || obj is not IInventory inventory)
            {
                return false;
            }

            foreach (var slot in inventory)
            {
                if (slot?.Empty != false)
                {
                    continue;
                }

                var slotStack = slot.Itemstack;
                if (slotStack?.Collectible == null)
                {
                    continue;
                }

                if (inner.Evaluate(slotStack, ctx))
                {
                    return true;
                }

                if (slotStack.Block is BlockLiquidContainerBase liquidContainer)
                {
                    ItemStack contentStack = liquidContainer.GetContent(slotStack);
                    if (contentStack?.Collectible != null && inner.Evaluate(contentStack, ctx))
                    {
                        return true;
                    }
                }

                if (slotStack.Collectible is ILiquidInterface && inner.Evaluate(slotStack, ctx))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// `inventoryEmpty` / `inventoryFilled` — whether the scoped inventory holds anything at all.
    /// (`inventoryFilled` means "there is something in it", NOT "it is full".)
    ///
    /// This must be an <see cref="IInventoryCondition"/>, not a plain ICondition: a plain condition
    /// in the target scope is evaluated per slot via InventoryConditionResolver.AnyMatch, so an
    /// EMPTY inventory would iterate nothing and never match — exactly the case inventoryEmpty
    /// needs to detect. As an IInventoryCondition it receives the inventory directly.
    /// </summary>
    public sealed class InventoryContentCondition : IInventoryCondition
    {
        private readonly bool expectFilled;

        public InventoryContentCondition(bool expectFilled)
        {
            this.expectFilled = expectFilled;
        }

        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx)
        {
            IInventory inventory = null;
            if (ctx != null && ctx.TryGetValue("inventory", out var obj)) inventory = obj as IInventory;
            return Matches(inventory);
        }

        public bool Evaluate(ItemStack stack, IInventory inventory, IDictionary<string, object> ctx, InventoryConditionScope scope, bool isSelectionEvaluation)
        {
            return Matches(inventory);
        }

        private bool Matches(IInventory inventory)
        {
            // No inventory at all -> cannot confirm either state; stay conservative and fail.
            if (inventory == null) return false;
            bool hasAny = HasAnyContent(inventory);
            return expectFilled ? hasAny : !hasAny;
        }

        private static bool HasAnyContent(IInventory inventory)
        {
            foreach (var slot in inventory)
            {
                if (slot?.Empty == false && slot.Itemstack?.Collectible != null) return true;
            }
            return false;
        }
    }
}