using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.managedchute.transporting
{
    public sealed class LiquidTransferService
    {
        private readonly ICoreAPI api;
        private readonly IInventory targetInv;
        private readonly BlockPos targetPos;

        public LiquidTransferService(ICoreAPI api, IInventory targetInv, BlockPos targetPos)
        {
            this.api = api;
            this.targetInv = targetInv;
            this.targetPos = targetPos;
        }

        public void AddConditionContext(IDictionary<string, object> ctx)
        {
            ctx["targetInventory"] = targetInv;
        }

        public ItemStack GetLiquidStackForTransfer(ItemStack sourceStack)
        {
            if (sourceStack == null) return null;
            if (sourceStack.Collectible.IsLiquid()) return sourceStack;

            BlockLiquidContainerBase liquidContainer = sourceStack.Block as BlockLiquidContainerBase;
            return liquidContainer?.GetContent(sourceStack);
        }

        public bool HasAnyLiquidTargetSlot(byte targetSlotSignal)
        {
            if (targetSlotSignal > 0)
            {
                int index = targetSlotSignal - 1;
                if (index < 0 || index >= targetInv.Count) return false;
                return CanAcceptLiquid(targetInv[index], index);
            }

            for (int i = 0; i < targetInv.Count; i++)
            {
                if (CanAcceptLiquid(targetInv[i], i)) return true;
            }

            return false;
        }

        public ItemSlot GetTargetSlot(ItemStack sourceStack, byte targetSlotSignal)
        {
            ItemStack liquidStack = GetLiquidStackForTransfer(sourceStack);
            if (liquidStack == null) return null;

            if (targetSlotSignal > 0)
            {
                int index = targetSlotSignal - 1;
                if (index < 0 || index >= targetInv.Count) return null;

                ItemSlot explicitSlot = targetInv[index];
                return CanAcceptLiquid(explicitSlot, index) && TryGetRemainingLiquidCapacityLitres(explicitSlot, liquidStack, index, out float remainingLitres) && remainingLitres > 0
                    ? explicitSlot
                    : null;
            }

            for (int i = 0; i < targetInv.Count; i++)
            {
                ItemSlot slot = targetInv[i];
                if (!CanAcceptLiquid(slot, i)) continue;
                if (TryGetRemainingLiquidCapacityLitres(slot, liquidStack, i, out float remainingLitres) && remainingLitres > 0) return slot;
            }

            return null;
        }

        public bool RequiresLiquidTransfer(ItemStack sourceStack, ItemSlot dst)
        {
            if (GetLiquidStackForTransfer(sourceStack) == null) return false;
            return CanAcceptLiquid(dst);
        }

        public TransferOperationResult TryMoveFromItemSlot(ItemSlot src, ItemSlot dst, decimal requestedAmount, bool hasAmountOverride)
        {
            if (src?.Itemstack == null || src.Itemstack.StackSize <= 0) return TransferOperationResult.None;
            if (!CanAcceptLiquid(dst)) return TransferOperationResult.None;

            decimal litresToMove = NormalizeLiquidAmount(requestedAmount);
            if (litresToMove <= 0) return TransferOperationResult.None;

            ItemStack liquidStack = GetLiquidStackForTransfer(src.Itemstack);
            if (liquidStack == null) return TransferOperationResult.None;

            int moved = TryMoveLiquidStackToTarget(liquidStack, dst, litresToMove);
            if (moved <= 0) return TransferOperationResult.None;

            if (src.Itemstack.Block is BlockLiquidContainerBase liquidContainer)
            {
                int quantityPerContainer = moved / src.Itemstack.StackSize;
                if (quantityPerContainer <= 0) return TransferOperationResult.None;

                ItemStack taken = liquidContainer.TryTakeContent(src.Itemstack, quantityPerContainer);
                if (taken == null || taken.StackSize <= 0) return TransferOperationResult.None;
            }
            else
            {
                src.TakeOut(moved);
            }

            return CreateLiquidTransferResult(liquidStack, moved, hasAmountOverride);
        }

        public TransferOperationResult TryMoveFromWorldSource(BlockPos sourcePos, ItemSlot dst, decimal requestedAmount, bool hasAmountOverride)
        {
            if (!TryResolveWorldLiquidSource(sourcePos, out ItemStack liquidStack)) return TransferOperationResult.None;
            if (!CanAcceptLiquid(dst)) return TransferOperationResult.None;

            decimal litresToMove = NormalizeLiquidAmount(requestedAmount);
            if (litresToMove <= 0) return TransferOperationResult.None;

            int moved = TryMoveLiquidStackToTarget(liquidStack, dst, litresToMove);
            return moved > 0 ? CreateLiquidTransferResult(liquidStack, moved, hasAmountOverride) : TransferOperationResult.None;
        }

        public bool TryResolveWorldLiquidSource(BlockPos sourcePos, out ItemStack liquidStack)
        {
            liquidStack = null;

            Block block = api.World.BlockAccessor.GetBlock(sourcePos, BlockLayersAccess.FluidOrSolid);
            if (!IsAllowedWorldLiquidBlock(block)) return false;

            WaterTightContainableProps props = block.Attributes?["waterTightContainerProps"]?.AsObject<WaterTightContainableProps>();
            if (props?.WhenFilled?.Stack == null || !props.Containable) return false;

            props.WhenFilled.Stack.Resolve(api.World, "managedchute-worldliquid");
            liquidStack = props.WhenFilled.Stack.ResolvedItemstack?.Clone();
            if (liquidStack == null) return false;

            liquidStack.StackSize = int.MaxValue / 4;
            return true;
        }

        private int TryMoveLiquidStackToTarget(ItemStack liquidStack, ItemSlot dst, decimal litresToMove)
        {
            if (liquidStack == null) return 0;

            int slotIndex = targetInv.GetSlotId(dst);
            if (!TryGetRemainingLiquidCapacityLitres(dst, liquidStack, slotIndex, out float remainingLitres) || remainingLitres <= 0) return 0;

            if (RequiresBlockLevelLiquidSink(dst, slotIndex))
            {
                if (api.World.BlockAccessor.GetBlock(targetPos) is not ILiquidSink liquidSink) return 0;
                return liquidSink.TryPutLiquid(targetPos, liquidStack, (float)Math.Min(litresToMove, (decimal)remainingLitres));
            }

            var liquidProps = BlockLiquidContainerBase.GetContainableProps(liquidStack);
            if (liquidProps == null || liquidProps.ItemsPerLitre <= 0) return 0;

            float allowedLitres = Math.Min((float)litresToMove, remainingLitres);
            int moveQuantity = (int)(liquidProps.ItemsPerLitre * allowedLitres);
            moveQuantity = Math.Min(moveQuantity, liquidStack.StackSize);
            if (moveQuantity <= 0) return 0;

            if (dst.Empty)
            {
                ItemStack movedStack = liquidStack.Clone();
                movedStack.StackSize = moveQuantity;
                dst.Itemstack = movedStack;
            }
            else
            {
                dst.Itemstack.StackSize += moveQuantity;
            }

            return moveQuantity;
        }

        private bool CanAcceptLiquid(ItemSlot slot)
        {
            int slotIndex = slot == null ? -1 : targetInv.GetSlotId(slot);
            return CanAcceptLiquid(slot, slotIndex);
        }

        private bool CanAcceptLiquid(ItemSlot slot, int slotIndex)
        {
            if (slot == null) return false;
            if (slot is ItemSlotWatertight) return !IsSmeltingCookingTarget(slotIndex) || HasCookingContainer();
            if (slot is ItemSlotLiquidOnly) return true;
            if (RequiresBlockLevelLiquidSink(slot, slotIndex)) return true;
            return false;
        }

        private bool TryGetRemainingLiquidCapacityLitres(ItemSlot slot, ItemStack liquidStack, int slotIndex, out float remainingLitres)
        {
            remainingLitres = 0;

            if (!CanAcceptLiquid(slot, slotIndex) || liquidStack == null) return false;

            var liquidProps = BlockLiquidContainerBase.GetContainableProps(liquidStack);
            if (liquidProps == null || liquidProps.ItemsPerLitre <= 0) return false;

            if (RequiresBlockLevelLiquidSink(slot, slotIndex))
            {
                float blockCapacityLitres = GetBlockLevelCapacityLitres();
                if (blockCapacityLitres <= 0) return false;

                float blockCurrentLitres = slot.StackSize / liquidProps.ItemsPerLitre;
                remainingLitres = blockCapacityLitres - blockCurrentLitres;
                return remainingLitres >= 0;
            }

            if (slot.Itemstack != null && !slot.Itemstack.Equals(api.World, liquidStack, GlobalConstants.IgnoredStackAttributes)) return false;

            float currentLitres = slot.StackSize / liquidProps.ItemsPerLitre;
            float capacityLitres = slot switch
            {
                ItemSlotWatertight watertightSlot => watertightSlot.capacityLitres,
                ItemSlotLiquidOnly liquidOnlySlot => liquidOnlySlot.CapacityLitres,
                _ => 0
            };

            remainingLitres = capacityLitres - currentLitres;
            return true;
        }

        private bool RequiresBlockLevelLiquidSink(ItemSlot slot, int slotIndex)
        {
            if (targetPos == null) return false;
            if (slotIndex != 0) return false;
            if (slot is ItemSlotWatertight || slot is ItemSlotLiquidOnly) return false;

            return api.World.BlockAccessor.GetBlock(targetPos) is ILiquidSink;
        }

        private float GetBlockLevelCapacityLitres()
        {
            return api.World.BlockAccessor.GetBlock(targetPos) is ILiquidInterface liquidInterface ? liquidInterface.CapacityLitres : 0;
        }

        private bool HasCookingContainer()
        {
            return targetInv is InventorySmelting smeltingInventory && smeltingInventory.HaveCookingContainer;
        }

        private bool IsSmeltingCookingTarget(int slotIndex)
        {
            return targetInv.ClassName.Equals("smelting", StringComparison.OrdinalIgnoreCase) && slotIndex >= 3 && slotIndex <= 6;
        }

        private static bool IsAllowedWorldLiquidBlock(Block block)
        {
            string path = block?.Code?.Path;
            if (path == null) return false;

            return path.Equals("water-still-7", StringComparison.OrdinalIgnoreCase)
                || path.Equals("saltwater-still-7", StringComparison.OrdinalIgnoreCase);
        }

        private static decimal NormalizeLiquidAmount(decimal amount)
        {
            if (amount <= 0) return 0;
            return decimal.Round(amount, 2, MidpointRounding.ToZero);
        }

        private static TransferOperationResult CreateLiquidTransferResult(ItemStack liquidStack, int movedItems, bool hasAmountOverride)
        {
            var liquidProps = BlockLiquidContainerBase.GetContainableProps(liquidStack);
            if (liquidProps == null || liquidProps.ItemsPerLitre <= 0) return TransferOperationResult.None;

            decimal movedLitres = decimal.Round(movedItems / (decimal)liquidProps.ItemsPerLitre, 2, MidpointRounding.ToZero);
            int triggerCost = hasAmountOverride ? 1 : (int)movedLitres;
            if (triggerCost <= 0) triggerCost = 1;

            return movedLitres > 0 ? new TransferOperationResult(movedLitres, triggerCost, true) : TransferOperationResult.None;
        }
    }
}
