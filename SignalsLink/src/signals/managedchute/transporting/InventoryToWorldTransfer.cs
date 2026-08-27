using HarmonyLib;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace SignalsLink.src.signals.managedchute.transporting
{
    // Přenos: inventář -> svět (spawn item entity).
    public class InventoryToWorldTransfer : InventorySourcedTransferBase, IItemTransfer
    {
        private readonly BlockPos targetPos;
        private readonly byte mode; // targetInv signal

        public InventoryToWorldTransfer(ICoreAPI api, IInventory sourceInv, byte inputSlotSignal, BlockPos targetPos, byte mode, PaperConditionsEvaluator conditionsEvaluator)
            : base(api, sourceInv, inputSlotSignal, conditionsEvaluator)
        {
            this.targetPos = targetPos;
            this.mode = mode;
        }

        protected override bool AllowsLiquidContainers => true;

        protected override bool CanTransferSelection(ItemSlot slot, PaperConditionDirectives directives)
        {
            // ManagedChute may place a filled bucket, but must never eject the liquid portions
            // stored in barrels and other liquid inventories.
            return !IsLiquidContainer(slot?.Itemstack) || slot.Itemstack.Block is BlockLiquidContainerBase;
        }

        public int TryMoveOneItem(ItemStackMoveOperation opTemplate)
        {
            TransferSelection selection = GetTransferSelection();
            ItemSlot src = selection?.SourceSlot;
            if (src == null || src.Empty) return 0;

            // Zkontroluj blok pod cílem – musí být solid pro „placing na zem“
            BlockPos belowPos = targetPos.DownCopy();
            Block blockBelow = api.World.BlockAccessor.GetBlock(belowPos);
            bool hasSolidBelow = blockBelow.SideSolid[BlockFacing.UP.Index];

            bool targetGround = selection.Directives.TargetGround;

            if (targetGround)
            {
                if (!hasSolidBelow) return 0;

                if (TryPlaceBlockOnGround(src, targetPos) || TryStackOnGround(src, targetPos))
                {
                    src.MarkDirty();
                    return 1;
                }

                return 0;
            }

            if (mode == 1 && hasSolidBelow)
            {
                if (TryPlaceBlockOnGround(src, targetPos))
                {
                    src.MarkDirty();
                    return 1;
                }

                return 0;
            }

            if (mode == 2 && hasSolidBelow)
            {
                if (TryPlaceLiquidContainerOnGround(src, targetPos))
                {
                    src.MarkDirty();
                    return 1;
                }

                if (TryStackOnGround(src, targetPos))
                {
                    src.MarkDirty();
                    return 1;
                }
                else 
                {
                    // Pokud se nepodaří stackovat, nespadá to dál – režim je „pouze stackovat“
                    return 0;
                }
                // Když se nepodaří, spadne to dál na „throw“
            }

            ItemStack taken = src.TakeOut(1);
            if (taken == null || taken.StackSize <= 0) return 0;

            Vec3d spawnPos = targetPos.ToVec3d().Add(0.5, 0.5, 0.5);
            api.World.SpawnItemEntity(taken, spawnPos);

            src.MarkDirty();
            return 1;
        }

        private bool TryPlaceBlockOnGround(ItemSlot src, BlockPos pos)
        {
            ItemStack stack = src.Itemstack;
            if (stack == null || stack.Block == null) return false;

            // Neumisťuj, pokud by to nahradilo blok stejného typu (pannable styl)
            Block blockAtTarget = api.World.BlockAccessor.GetBlock(pos);
            if (blockAtTarget.Code != null &&
                stack.Collectible?.Code != null &&
                blockAtTarget.Code.FirstCodePart() == stack.Collectible.Code.FirstCodePart())
            {
                return false;
            }

            BlockSelection blockSelection = new BlockSelection
            {
                Position = pos,
                Face = BlockFacing.DOWN
            };

            if (stack.Block is BlockLiquidContainerBase)
            {
                if (blockAtTarget.Replaceable < 6000 || api.World.BlockAccessor.GetBlockEntity(pos) != null) return false;

                string canPlaceFailureCode = null;
                if (!stack.Block.CanPlaceBlock(api.World, null, blockSelection, ref canPlaceFailureCode)) return false;

                // Liquid-container placement needs its complete stack to initialize the placed
                // block entity. Unlike player placement, this path does not require an IPlayer.
                api.World.BlockAccessor.SetBlock(stack.Block.BlockId, pos, stack);
                api.World.BlockAccessor.MarkBlockModified(pos);
                src.TakeOut(1);
                return true;
            }

            string failureCode = null;
            bool placed = stack.Block.TryPlaceBlock(api.World, null, stack, blockSelection, ref failureCode);

            if (!placed) return false;

            src.TakeOut(1);
            return true;
        }

        private bool TryPlaceLiquidContainerOnGround(ItemSlot src, BlockPos pos)
        {
            return src.Itemstack?.Block is BlockLiquidContainerBase && TryPlaceBlockOnGround(src, pos);
        }

        private bool TryStackOnGround(ItemSlot src, BlockPos pos)
        {
            ItemStack stack = src.Itemstack;
            if (stack == null) return false;

            // Zkus najít existující pile na cílovém bloku
            BlockEntityItemPile pile = api.World.BlockAccessor.GetBlockEntity<BlockEntityItemPile>(pos);
            if (pile != null)
            {
                // Musí být stejný typ itemu
                ItemSlot pileSlot = pile.inventory[0];
                if (!pileSlot.Empty &&
                    stack.Equals(api.World, pileSlot.Itemstack, GlobalConstants.IgnoredStackAttributes) &&
                    pile.OwnStackSize < pile.MaxStackSize)
                {
                    pileSlot.Itemstack.StackSize++;
                    pileSlot.MarkDirty();
                    pile.MarkDirty(false, null);

                    src.TakeOut(1);
                    return true;
                }
            }

            // Pokud není existující pile, zkus vytvořit nový, pokud je item pileable
            if (stack.Item is ItemPileable pileableItem)
            {
                var pileableItemTraverse = Traverse.Create(pileableItem);
                var pileBlock = api.World.GetBlock(pileableItemTraverse.Property("PileBlockCode").GetValue<AssetLocation>());
                if (pileBlock is IBlockItemPile pileBlockImpl)
                {
                    bool success = pileBlockImpl.Construct(src, api.World, pos, null);
                    return success;
                }
            }

            return false;
        }

    }
}