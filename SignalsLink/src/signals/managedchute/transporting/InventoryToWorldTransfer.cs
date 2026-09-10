using HarmonyLib;
using System.Collections.Generic;
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

        /// <summary>
        /// The target IS the ground, whether or not the paper says so.
        ///
        /// Set by a device whose other end is a place rather than a container - a storage yard,
        /// where a section header has already said that goods go on the ground and there is
        /// nothing left for `target ground` to add. Without it a block would only ever top up a
        /// pile that happened to be there already, never start one.
        /// </summary>
        public bool GroundImplied { get; set; }

        /// <summary>
        /// This column belongs to a storage yard, which holds it to two rules a loose
        /// <c>target ground</c> is not held to.
        ///
        /// <b>One kind of goods per column.</b> A column is what the paper reads as a single slot,
        /// and a slot holds one thing. Without this, growing a column steps over a pile of
        /// something else and starts a new pile on top of it - ingots, ingots, planks.
        ///
        /// <b>Stacking goods only.</b> Anything else gets PLACED as a block on the tile, one per
        /// tile, and can then never be picked up again: reading a yard understands piles and
        /// nothing else. Better not to accept it in the first place.
        /// </summary>
        public bool YardColumn { get; set; }


        protected override bool CanTransferSelection(ItemSlot slot, PaperConditionDirectives directives)
        {
            // A firepit stage wants one particular material, and the source is walked slot by slot.
            // Without this a slot of firewood standing before the dry grass would be selected -
            // the directive only says there IS something to build - and then do nothing all tick,
            // so the firepit would only ever get started when the grass happened to come first.
            if (directives.TargetFirepit)
            {
                return FirepitConstruction.Matches(api.World.BlockAccessor.GetBlock(targetPos), slot?.Itemstack);
            }

            // A column with nowhere left to put anything is not a valid target. Without this the
            // block would keep winning the evaluation after the column filled up, and every block
            // below it on the paper — an `output` that reports the column is full, say — would
            // never be reached.
            if (directives.TargetGround || GroundImplied) return HasGroundRoom(slot, directives);

            // ManagedChute may place a filled bucket, but must never eject the liquid portions
            // stored in barrels and other liquid inventories.
            return !IsLiquidContainer(slot?.Itemstack) || slot.Itemstack.Block is BlockLiquidContainerBase;
        }

        protected override void AddConditionContext(IDictionary<string, object> ctx)
        {
            if (targetPos == null) return;

            // Block-state conditions (isBurning) want to know which block is being aimed at.
            ctx["targetBlockPos"] = targetPos;

            // And the ground column standing there is offered as the target inventory, so the
            // ordinary conditions work against it: `in target game:firewood 96` counts the WHOLE
            // column, not the one pile the chute happens to point at. Same trick the anvil uses to
            // expose its work item - a throw-away inventory, read-only as far as conditions go.
            IInventory column = BuildGroundColumnInventory();
            if (column != null) ctx["targetInventory"] = column;

            if (ConditionDebug.Enabled)
            {
                ConditionDebug.Log("  ground column at " + targetPos + " block="
                    + (api.World.BlockAccessor.GetBlock(targetPos)?.Code?.ToString() ?? "?")
                    + " -> " + ConditionDebug.Describe(column));
            }
        }

        // Deliberately the ground-storage view and not the general one: this class PLACES things,
        // and it may only ever grow a column of piles. Reading a layered block here would invite it
        // to stack firewood on top of a charcoal pile.
        private IInventory BuildGroundColumnInventory()
        {
            return TargetInventoryResolver.ResolveGroundColumn(api, targetPos);
        }

        public int TryMoveOneItem(ItemStackMoveOperation opTemplate)
        {
            TransferSelection selection = GetTransferSelection();
            ItemSlot src = selection?.SourceSlot;
            if (src == null || src.Empty) return 0;

            // Is there a footing under the target? Asked through GroundSupport, so a pile of
            // firewood counts as well - a charcoal pit needs its firepit built on top of one.
            bool hasSolidBelow = GroundSupport.HasFooting(api.World, targetPos);

            // `target firepit` on open ground lays the tinder that starts a firepit off. From the
            // next stage onwards there is a block to build on and InventoryToFirepitTransfer takes
            // over, so this only ever handles the very first step.
            if (selection.Directives.TargetFirepit)
            {
                if (!hasSolidBelow) return 0;
                if (!FirepitConstruction.Advance(api.World, targetPos, src.Itemstack)) return 0;

                src.TakeOut(1);
                src.MarkDirty();
                return 1;
            }

            bool targetGround = selection.Directives.TargetGround || GroundImplied;

            // `amount N` batches the ground placement (without it a single item is placed, as before).
            // Like the chute's item batches it is atomic on the source, but the batch is gathered from
            // ALL matching source slots - N routinely exceeds the item's max stack size (ingots stack
            // to 16 while a pile holds 64), so a single-slot check could never be satisfied.
            int groundBatch = 1;
            decimal? amountDirective = selection.Directives.Amount;
            if (amountDirective.HasValue)
            {
                groundBatch = (int)decimal.Truncate(amountDirective.Value);
                if (groundBatch < 1) groundBatch = 1;
            }

            // `amount N+` reaches past its floor, so the batch is everything the source can muster;
            // the floor itself is still checked, atomically, below.
            if (selection.Directives.TakesEverythingAvailable)
            {
                int available = 0;
                foreach (ItemSlot s in GetMatchingSourceSlots(src)) available += s.StackSize;

                if (available > groundBatch) groundBatch = available;
            }

            bool groundAtomic = selection.Directives.IsAtomicAmount;

            // Without `target ground` we still top up an existing ground-storage pile at the target.
            // That is what the plain inventory route did before piles were sent here, so keeping it
            // avoids a regression; creating new piles and growing the column needs the directive.
            if (!targetGround && api.World.BlockAccessor.GetBlockEntity(targetPos) is BlockEntityGroundStorage)
            {
                int toppedUp = TryGroundStorageStack(src, 1, createNew: false, maxItems: groundBatch, atomic: groundAtomic, atomicFloor: (int)decimal.Truncate(amountDirective ?? 0));
                if (toppedUp > 0)
                {
                    src.MarkDirty();
                    return toppedUp;
                }
                return 0;
            }

            if (targetGround)
            {
                if (!hasSolidBelow) return 0;

                // On a yard, goods that cannot be stacked into a pile are refused rather than set
                // down as a block: a block on a tile can never be picked up again, because reading
                // a yard understands piles and nothing else.
                if (!YardColumn && (TryPlaceBlockOnGround(src, targetPos) || TryStackOnGround(src, targetPos)))
                {
                    src.MarkDirty();
                    return 1;
                }

                int placed = TryGroundStorageStack(src, selection.Directives.TargetGroundHeight, createNew: true, maxItems: groundBatch, atomic: groundAtomic, atomicFloor: (int)decimal.Truncate(amountDirective ?? 0));
                if (placed > 0)
                {
                    src.MarkDirty();
                    return placed;
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

        /// <summary>
        /// Modern ground storage (firewood, ingots, plates - anything with the GroundStorable
        /// behavior in the Stacking layout). Fills the pile at the target and, when it is full,
        /// climbs up to <paramref name="maxHeight"/> blocks, creating new piles on the way.
        /// The legacy ItemPileable path (coal, ore) is handled by TryStackOnGround instead.
        /// </summary>
        private int TryGroundStorageStack(ItemSlot src, int maxHeight, bool createNew, int maxItems, bool atomic, int atomicFloor = 0)
        {
            if (atomicFloor <= 0) atomicFloor = maxItems;

            ItemStack stack = src.Itemstack;
            if (stack?.Collectible == null) return 0;

            GroundStorageProperties props = stack.Collectible.GetBehavior<CollectibleBehaviorGroundStorable>()?.StorageProps;
            if (props == null || props.Layout != EnumGroundStorageLayout.Stacking) return 0;

            if (YardColumn && ColumnHoldsSomethingElse(stack, maxHeight)) return 0;

            if (maxItems < 1) maxItems = 1;

            List<ItemSlot> srcSlots = GetMatchingSourceSlots(src);

            if (atomic)
            {
                int available = 0;
                foreach (ItemSlot s in srcSlots) available += s.StackSize;
                if (available < atomicFloor) return 0; // all of it or nothing
            }

            IBlockAccessor ba = api.World.BlockAccessor;
            if (maxHeight < 1) maxHeight = 1;

            int placed = 0;

            for (int dy = 0; dy < maxHeight && placed < maxItems; dy++)
            {
                BlockPos pos = targetPos.AddCopy(0, dy, 0);

                if (ba.GetBlockEntity(pos) is BlockEntityGroundStorage pile)
                {
                    placed += TryAddToPile(pile, srcSlots, maxItems - placed);
                    continue; // full (or now full) or a different item - try the level above
                }

                if (!createNew) break;

                // Something solid in the way stops the column; only replaceable stuff (air, grass) is ok.
                if (ba.GetBlock(pos).Replaceable < 6000) break;
                if (!HasGroundSupport(pos)) break;

                int justPlaced = TryCreatePile(pos, srcSlots, props, maxItems - placed);
                if (justPlaced <= 0) break;
                placed += justPlaced;
            }

            return placed;
        }

        /// <summary>
        /// Every source slot holding the same item as the selected one, the selected slot first.
        /// A batch usually spans several slots because the item's max stack size is smaller than
        /// the pile capacity - the inventory-to-inventory transfer gathers the same way.
        /// </summary>
        private List<ItemSlot> GetMatchingSourceSlots(ItemSlot initial)
        {
            List<ItemSlot> list = new List<ItemSlot>();
            if (initial?.Itemstack == null) return list;

            list.Add(initial);
            ItemStack reference = initial.Itemstack;

            for (int i = 0; i < sourceInv.Count; i++)
            {
                ItemSlot slot = sourceInv[i];
                if (slot == null || ReferenceEquals(slot, initial) || slot.Empty) continue;
                if (slot.Itemstack?.Collectible != reference.Collectible) continue;
                if (!slot.Itemstack.Equals(api.World, reference, GlobalConstants.IgnoredStackAttributes)) continue;

                list.Add(slot);
            }

            return list;
        }

        /// <summary>
        /// Is there still somewhere in the column to put this? Either a pile of the same thing that
        /// is not full yet, or a free cell with a footing where a new pile can be started — within
        /// the height `target ground N` allows.
        /// </summary>
        private bool HasGroundRoom(ItemSlot slot, PaperConditionDirectives directives)
        {
            if (slot?.Itemstack == null || targetPos == null) return false;

            int maxHeight = System.Math.Max(1, directives.TargetGroundHeight);

            if (YardColumn)
            {
                GroundStorageProperties props = slot.Itemstack.Collectible
                    ?.GetBehavior<CollectibleBehaviorGroundStorable>()?.StorageProps;

                if (props == null || props.Layout != EnumGroundStorageLayout.Stacking) return false;
                if (ColumnHoldsSomethingElse(slot.Itemstack, maxHeight)) return false;
            }
            IBlockAccessor ba = api.World.BlockAccessor;

            for (int dy = 0; dy < maxHeight; dy++)
            {
                BlockPos pos = targetPos.AddCopy(0, dy, 0);

                if (ba.GetBlockEntity(pos) is BlockEntityGroundStorage pile)
                {
                    ItemSlot pileSlot = pile.Inventory?[0];
                    bool sameThing = pileSlot == null || pileSlot.Empty
                        || pileSlot.Itemstack.Equals(api.World, slot.Itemstack, GlobalConstants.IgnoredStackAttributes);

                    if (sameThing && pile.TotalStackSize < pile.Capacity) return true;
                    continue; // full, or holding something else - try the next one up
                }

                // Not a pile: an open cell with something to stand on can become one.
                Block block = ba.GetBlock(pos);
                if (block != null && block.Replaceable >= 6000 && GroundSupport.HasFooting(api.World, pos)) return true;

                return false; // something solid is in the way; the column ends here
            }

            return false; // the height limit is reached and every pile below it is full
        }

        /// <summary>
        /// Is there a pile in this column holding something other than what is being carried?
        ///
        /// The whole column counts, not just the top of it: a yard column is one slot as far as the
        /// paper is concerned, and half a slot of planks over ingots is not a slot of anything.
        /// </summary>
        private bool ColumnHoldsSomethingElse(ItemStack stack, int maxHeight)
        {
            IBlockAccessor ba = api.World.BlockAccessor;

            for (int dy = 0; dy < System.Math.Max(1, maxHeight); dy++)
            {
                if (ba.GetBlockEntity(targetPos.AddCopy(0, dy, 0)) is not BlockEntityGroundStorage pile) break;

                ItemSlot slot = pile.Inventory?[0];
                if (slot == null || slot.Empty) continue;

                if (!slot.Itemstack.Equals(api.World, stack, GlobalConstants.IgnoredStackAttributes)) return true;
            }

            return false;
        }

        private bool HasGroundSupport(BlockPos pos)
        {
            BlockPos below = pos.DownCopy();
            if (api.World.BlockAccessor.GetBlockEntity(below) is BlockEntityGroundStorage) return true;
            return GroundSupport.HasFooting(api.World, pos);
        }

        private int TryAddToPile(BlockEntityGroundStorage pile, List<ItemSlot> srcSlots, int maxCount)
        {
            if (pile?.Inventory == null || pile.Inventory.Count == 0) return 0;
            if (maxCount < 1 || srcSlots == null || srcSlots.Count == 0) return 0;

            int room = pile.Capacity - pile.TotalStackSize;
            if (room <= 0) return 0;

            ItemSlot slot = pile.Inventory[0]; // Stacking layout keeps everything in the first slot
            if (slot == null) return 0;

            int want = System.Math.Min(maxCount, room);
            int taken = 0;

            // NOTE: do NOT use TryPutInto here. A normal slot merge caps at the item's
            // MaxStackSize (ingots: 16), but a ground-storage pile holds StackingCapacity
            // (ingots: 64) - the pile deliberately ignores the item stack limit. The legacy
            // ItemPileable path above bumps StackSize directly for the very same reason.
            foreach (ItemSlot s in srcSlots)
            {
                if (taken >= want) break;
                if (s == null || s.Empty) continue;
                if (!slot.Empty && !slot.Itemstack.Equals(api.World, s.Itemstack, GlobalConstants.IgnoredStackAttributes)) continue;

                int count = System.Math.Min(want - taken, s.StackSize);
                if (count <= 0) continue;

                if (slot.Empty)
                {
                    ItemStack moved = s.TakeOut(count);
                    if (moved == null || moved.StackSize <= 0) continue;
                    slot.Itemstack = moved;
                    taken += moved.StackSize;
                }
                else
                {
                    slot.Itemstack.StackSize += count;
                    s.TakeOut(count);
                    taken += count;
                }

                s.MarkDirty();
            }

            if (taken <= 0) return 0;

            slot.MarkDirty();
            pile.MarkDirty(true);
            return taken;
        }

        /// <summary>
        /// Creates a new pile. BlockGroundStorage.CreateStorage needs an IPlayer we do not have,
        /// so the block is set directly and the storage props are forced onto the block entity -
        /// the same trick already used for placed liquid containers.
        /// </summary>
        private int TryCreatePile(BlockPos pos, List<ItemSlot> srcSlots, GroundStorageProperties props, int maxCount)
        {
            Block gsBlock = api.World.GetBlock(new AssetLocation("game", "groundstorage"));
            if (gsBlock == null) return 0;

            IBlockAccessor ba = api.World.BlockAccessor;
            ba.SetBlock(gsBlock.BlockId, pos);

            if (ba.GetBlockEntity(pos) is not BlockEntityGroundStorage pile)
            {
                ba.SetBlock(0, pos); // roll back
                return 0;
            }

            pile.ForceStorageProps(props);

            int placed = TryAddToPile(pile, srcSlots, maxCount);
            if (placed <= 0)
            {
                ba.SetBlock(0, pos); // roll back, never leave an empty ghost pile
                return 0;
            }

            ba.MarkBlockDirty(pos);
            return placed;
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