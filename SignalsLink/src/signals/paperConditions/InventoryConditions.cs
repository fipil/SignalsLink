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

        public InventoryAmountCondition(ICondition innerCondition, decimal expectedAmount, InventoryAmountComparison comparison)
        {
            this.innerCondition = innerCondition ?? FalseCondition.Instance;
            this.expectedAmount = expectedAmount;
            this.comparison = comparison;
        }

        public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx)
        {
            return innerCondition.Evaluate(stack, ctx);
        }

        public bool Evaluate(ItemStack stack, IInventory inventory, IDictionary<string, object> ctx, InventoryConditionScope scope, bool isSelectionEvaluation)
        {
            if (inventory == null) return false;

            if (isSelectionEvaluation && scope == InventoryConditionScope.Source)
            {
                if (stack?.Collectible == null) return false;
                if (!innerCondition.Evaluate(stack, ctx)) return false;
            }

            decimal actualAmount = InventoryConditionResolver.GetMatchingAmount(inventory, ctx, innerCondition);

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
}