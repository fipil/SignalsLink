using HarmonyLib;
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
        private readonly ICoreAPI api;
        private readonly BlockPos targetPos;
        private readonly byte mode; // targetInv signal

        public InventoryToWorldTransfer(ICoreAPI api, IInventory sourceInv, byte inputSlotSignal, BlockPos targetPos, byte mode)
            : base(sourceInv, inputSlotSignal)
        {
            this.api = api;
            this.targetPos = targetPos;
            this.mode = mode;
        }

        public int TryMoveOneItem(ItemStackMoveOperation opTemplate)
        {
            ItemSlot src = GetSourceSlot();
            if (src == null || src.Empty) return 0;

            // Zkontroluj blok pod cílem – musí být solid pro „placing na zem“
            BlockPos belowPos = targetPos.DownCopy();
            Block blockBelow = api.World.BlockAccessor.GetBlock(belowPos);
            bool hasSolidBelow = blockBelow.SideSolid[BlockFacing.UP.Index];

            // Rozhodni režim podle mode (targetInv signal)
            if (mode == 1 && hasSolidBelow)
            {
                // Pokus o umístění jako blok
                if (TryPlaceBlockOnGround(src, targetPos))
                {
                    src.MarkDirty();
                    return 1;
                }
                else 
                {
                    // Pokud se nepodaří stackovat, nespadá to dál – režim je „pouze stackovat“
                    return 0;
                }
                // Když se nepodaří, spadne to dál na ostatní režimy (stack / throw)
            }

            if (mode == 2 && hasSolidBelow)
            {
                // Pokus o stackování na zemi (pile, ingoty, apod.)
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

            // Default / fallback: vyhoď item ven jako entitu
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

            string failureCode = null;
            bool placed = stack.Block.TryPlaceBlock(api.World, null, stack, new BlockSelection
            {
                Position = pos,
                Face = BlockFacing.DOWN
            }, ref failureCode);

            if (!placed) return false;

            src.TakeOut(1);
            return true;
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