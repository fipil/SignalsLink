using signals.src;
using signals.src.signalNetwork;
using signals.src.transmission;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;
using HarmonyLib;

namespace SignalsLink.src.signals.managedchute
{
    public class BEManagedChute : BlockEntityOpenableContainer, IBESignalReceptor
    {
        public byte signalState;
        private int remaining;
        private bool unlimited;
        private bool placing;

        private const int PLACE_SIGNAL = 10;

        internal InventoryGeneric inventory;
        public BlockFacing[] PullFaces = Array.Empty<BlockFacing>();
        public BlockFacing[] PushFaces = Array.Empty<BlockFacing>();
        public BlockFacing[] AcceptFromFaces = Array.Empty<BlockFacing>();
        public string inventoryClassName = "hopper";
        public string ItemFlowObjectLangCode = "hopper-contents";
        public int QuantitySlots = 1;
        protected float itemFlowRate = 1f;
        public BlockFacing LastReceivedFromDir;
        public int MaxHorizontalTravel = 3;
        private int checkRateMs;
        private float itemFlowAccum;
        private static AssetLocation hopperOpen = new AssetLocation("sounds/block/hopperopen");
        private static AssetLocation hopperTumble = new AssetLocation("sounds/block/hoppertumble");

        public virtual float ItemFlowRate => this.itemFlowRate;

        public BEManagedChute()
        {
            this.OpenSound = BEManagedChute.hopperOpen;
            this.CloseSound = (AssetLocation)null;
        }

        public override InventoryBase Inventory => (InventoryBase)this.inventory;

        private void InitInventory()
        {
            this.parseBlockProperties();
            if (this.inventory != null)
                return;
            this.inventory = new InventoryGeneric(this.QuantitySlots, (string)null, (ICoreAPI)null);
            this.inventory.OnInventoryClosed += new OnInventoryClosedDelegate(this.OnInvClosed);
            this.inventory.OnInventoryOpened += new OnInventoryOpenedDelegate(this.OnInvOpened);
            this.inventory.SlotModified += new Action<int>(this.OnSlotModifid);
            this.inventory.OnGetAutoPushIntoSlot = new GetAutoPushIntoSlotDelegate(this.GetAutoPushIntoSlot);
            this.inventory.OnGetAutoPullFromSlot = new GetAutoPullFromSlotDelegate(this.GetAutoPullFromSlot);
        }

        private void parseBlockProperties()
        {
            if (this.Block?.Attributes == null)
                return;
            if (this.Block.Attributes["pullFaces"].Exists)
            {
                string[] strArray = this.Block.Attributes["pullFaces"].AsArray<string>();
                this.PullFaces = new BlockFacing[strArray.Length];
                for (int index = 0; index < strArray.Length; ++index)
                    this.PullFaces[index] = BlockFacing.FromCode(strArray[index]);
            }
            if (this.Block.Attributes["pushFaces"].Exists)
            {
                string[] strArray = this.Block.Attributes["pushFaces"].AsArray<string>();
                this.PushFaces = new BlockFacing[strArray.Length];
                for (int index = 0; index < strArray.Length; ++index)
                    this.PushFaces[index] = BlockFacing.FromCode(strArray[index]);
            }
            if (this.Block.Attributes["acceptFromFaces"].Exists)
            {
                string[] strArray = this.Block.Attributes["acceptFromFaces"].AsArray<string>();
                this.AcceptFromFaces = new BlockFacing[strArray.Length];
                for (int index = 0; index < strArray.Length; ++index)
                    this.AcceptFromFaces[index] = BlockFacing.FromCode(strArray[index]);
            }
            this.itemFlowRate = this.Block.Attributes["item-flowrate"].AsFloat(this.itemFlowRate);
            this.checkRateMs = this.Block.Attributes["item-checkrateMs"].AsInt(200);
            this.inventoryClassName = this.Block.Attributes["inventoryClassName"].AsString(this.inventoryClassName);
            this.ItemFlowObjectLangCode = this.Block.Attributes["itemFlowObjectLangCode"].AsString(this.ItemFlowObjectLangCode);
            this.QuantitySlots = this.Block.Attributes["quantitySlots"].AsInt(this.QuantitySlots);
        }

        private ItemSlot GetAutoPullFromSlot(BlockFacing atBlockFace)
        {
            ((IEnumerable<BlockFacing>)this.PushFaces).Contains<BlockFacing>(atBlockFace);
            return (ItemSlot)null;
        }

        private ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
        {
            ItemStack itemstack = this.inventory.FirstOrDefault<ItemSlot>((System.Func<ItemSlot, bool>)(slot => !slot.Empty))?.Itemstack;
            if (itemstack?.StackSize>0)
                return null;

            if (!((IEnumerable<BlockFacing>)this.PullFaces).Contains(atBlockFace) &&
                !((IEnumerable<BlockFacing>)this.AcceptFromFaces).Contains(atBlockFace))
                return null;

            // Lepší než natvrdo inventory[0]: najdi nejlepší slot (ať to nesype do "jiného" slotu než pak vypisuješ)
            return this.inventory.GetBestSuitedSlot(fromSlot, (ItemStackMoveOperation)null, (List<ItemSlot>)null).slot;
        }


        public override string InventoryClassName => this.inventoryClassName;

        private bool itemJustPushed = false;

        private void OnSlotModifid(int slot)
        {
            this.Api.World.BlockAccessor.GetChunkAtBlockPos(this.Pos)?.MarkModified();

        }

        protected virtual void OnInvOpened(IPlayer player) => this.inventory.PutLocked = false;

        protected virtual void OnInvClosed(IPlayer player)
        {
            this.invDialog?.Dispose();
            this.invDialog = (GuiDialogBlockEntity)null;
        }

        public override void Initialize(ICoreAPI api)
        {
            this.InitInventory();
            base.Initialize(api);

            if (!(api is ICoreServerAPI))
                return;
            this.RegisterDelayedCallback((Action<float>)(dt => this.RegisterGameTickListener(new Action<float>(this.MoveItem), this.checkRateMs)), 10 + api.World.Rand.Next(200));
        }

        public bool PushItems(int allowedNow)
        {
            if (this.PushFaces != null && this.PushFaces.Length != 0 && !this.inventory.Empty)
            {
                ItemStack itemstack = this.inventory.First<ItemSlot>((System.Func<ItemSlot, bool>)(slot => !slot.Empty)).Itemstack;
                BlockFacing pushFace = this.PushFaces[this.Api.World.Rand.Next(this.PushFaces.Length)];
                int index = itemstack.Attributes.GetInt("chuteDir", -1);
                BlockFacing blockFacing = index < 0 || !((IEnumerable<BlockFacing>)this.PushFaces).Contains<BlockFacing>(BlockFacing.ALLFACES[index]) ? (BlockFacing)null : BlockFacing.ALLFACES[index];
                if (blockFacing != null)
                {
                    if (this.Api.World.BlockAccessor.GetChunkAtBlockPos(this.Pos.AddCopy(blockFacing)) == null)
                        return false;
                    if (!this.TrySpitOut(blockFacing, allowedNow) && !this.TryPushInto(blockFacing, allowedNow) && !this.TrySpitOut(pushFace, allowedNow) && pushFace != blockFacing.Opposite && !this.TryPushInto(pushFace, allowedNow) && this.PullFaces.Length != 0)
                    {
                        BlockFacing pullFace = this.PullFaces[this.Api.World.Rand.Next(this.PullFaces.Length)];
                        if (pullFace.IsHorizontal && !this.TryPushInto(pullFace, allowedNow))
                            this.TrySpitOut(pullFace, allowedNow);
                    }
                }
                else
                {
                    if (this.Api.World.BlockAccessor.GetChunkAtBlockPos(this.Pos.AddCopy(pushFace)) == null)
                        return false;
                    if (!this.TrySpitOut(pushFace, allowedNow) && !this.TryPushInto(pushFace, allowedNow) && this.PullFaces != null && this.PullFaces.Length != 0)
                    {
                        BlockFacing pullFace = this.PullFaces[this.Api.World.Rand.Next(this.PullFaces.Length)];
                        if (pullFace.IsHorizontal && !this.TryPushInto(pullFace, allowedNow))
                            this.TrySpitOut(pullFace, allowedNow);
                    }
                }
                return true;
            }
            return false;
        }
        public void MoveItem(float dt)
        {
            if (!unlimited && remaining <= 0) return;

            itemFlowAccum = Math.Min(itemFlowAccum + ItemFlowRate, Math.Max(1f, ItemFlowRate * 2f));
            if (itemFlowAccum < 1f) return;

            int canByRate = (int)itemFlowAccum;
            int allowedNow = unlimited ? canByRate : Math.Min(canByRate, remaining);
            if (allowedNow <= 0) return;

            if (placing)
            {
                PlaceItems(allowedNow);
            }
            else
            {
                PushItems(allowedNow);
            }

            if (this.PullFaces == null || this.PullFaces.Length == 0 || !this.inventory.Empty)
                return;
            this.TryPullFrom(this.PullFaces[this.Api.World.Rand.Next(this.PullFaces.Length)], allowedNow);
        }

        private BlockFacing? FindFreePushFace()
        {
            if (this.PushFaces == null) return null;

            foreach (var face in this.PushFaces)
            {
                BlockPos targetPos = this.Pos.AddCopy(face);
                Block block = this.Api.World.BlockAccessor.GetBlock(targetPos);

                if (block.Replaceable >= 6000)   // air / replaceable
                    return face;
            }

            return null;
        }

        private void PlaceItems(int allowedNow)
        {
            ItemSlot slot = this.inventory.FirstOrDefault<ItemSlot>((System.Func<ItemSlot, bool>)(s => !s.Empty));
            if (slot == null || slot.Empty)
                return;

            if (this.PushFaces?.Length == 0) return;

            BlockFacing pushFace = FindFreePushFace();
            if (pushFace == null) return;

            BlockPos targetPos = this.Pos.AddCopy(pushFace);
            Block blockAtTarget = this.Api.World.BlockAccessor.GetBlock(targetPos);

            bool placed = false;
            // Try placing normally
            placed = TryPlace(slot, targetPos, blockAtTarget);
            // If not placed, try placing as pileable item
            if (!placed && slot.Itemstack.Item is ItemPileable)
            {
                placed = TryPlacePileableItem(slot, targetPos);
            }
            if (placed)
            {
                this.itemFlowAccum -= 1;
                SubstractRemaining(1);
                this.MarkDirty();
            }
            else
            {
                PushItems(allowedNow);
            }
        }

        private void SubstractRemaining(int amount)
        {
            if (unlimited) return;
            remaining -= amount;
            if (remaining < 0) 
                remaining = 0;
        }

        private void TryPullFrom(BlockFacing inputFace, int allowedNow)
        {
            BlockPos blockPos = this.Pos.AddCopy(inputFace);
            BlockEntityContainer blockEntity = this.Api.World.BlockAccessor.GetBlock(blockPos).GetBlockEntity<BlockEntityContainer>(blockPos);
            if (blockEntity == null)
                return;
            if (blockEntity.Block is BlockChute block)
            {
                string[] source = block.Attributes["pushFaces"].AsArray<string>();
                if ((source != null ? (((IEnumerable<string>)source).Contains<string>(inputFace.Opposite.Code) ? 1 : 0) : 0) != 0)
                    return;
            }
            ItemSlot autoPullFromSlot = blockEntity.Inventory.GetAutoPullFromSlot(inputFace.Opposite);
            ItemSlot slot = autoPullFromSlot == null ? (ItemSlot)null : this.inventory.GetBestSuitedSlot(autoPullFromSlot, (ItemStackMoveOperation)null, (List<ItemSlot>)null).slot;
            BlockEntityItemFlow blockEntityItemFlow = blockEntity as BlockEntityItemFlow;
            if (autoPullFromSlot == null || slot == null || blockEntityItemFlow != null && !slot.Empty)
                return;
            ItemStackMoveOperation op = new ItemStackMoveOperation(this.Api.World, EnumMouseButton.Left, (EnumModifierKey)0, EnumMergePriority.DirectMerge, allowedNow);
            int num1 = autoPullFromSlot.Itemstack.Attributes.GetInt("chuteQHTravelled");
            if (num1 >= this.MaxHorizontalTravel)
                return;
            int num2 = autoPullFromSlot.TryPutInto(slot, ref op);
            if (num2 > 0)
            {
                if (blockEntityItemFlow != null)
                {
                    slot.Itemstack.Attributes.SetInt("chuteQHTravelled", inputFace.IsHorizontal ? num1 + 1 : 0);
                    slot.Itemstack.Attributes.SetInt("chuteDir", inputFace.Opposite.Index);
                }
                else
                {
                    slot.Itemstack.Attributes.RemoveAttribute("chuteQHTravelled");
                    slot.Itemstack.Attributes.RemoveAttribute("chuteDir");
                }
                autoPullFromSlot.MarkDirty();
                slot.MarkDirty();
                this.MarkDirty();
                blockEntityItemFlow?.MarkDirty();
            }
            if (!(num2 <= 0 || this.Api.World.Rand.NextDouble() >= 0.2))
                this.Api.World.PlaySoundAt(BEManagedChute.hopperTumble, this.Pos, 0.0, range: 8f, volume: 0.5f);
        }

        private bool TryPushInto(BlockFacing outputFace, int allowedNow)
        {
            BlockPos blockPos = this.Pos.AddCopy(outputFace);
            BlockEntityContainer blockEntity = this.Api.World.BlockAccessor.GetBlock(blockPos).GetBlockEntity<BlockEntityContainer>(blockPos);
            if (blockEntity != null)
            {
                ItemSlot fromSlot = this.inventory.FirstOrDefault<ItemSlot>((System.Func<ItemSlot, bool>)(slot => !slot.Empty));
                if (fromSlot?.Itemstack?.StackSize == 0)
                    return false;
                int num1 = fromSlot.Itemstack.Attributes.GetInt("chuteQHTravelled");
                int num2 = fromSlot.Itemstack.Attributes.GetInt("chuteDir");
                if (outputFace.IsHorizontal && num1 >= this.MaxHorizontalTravel)
                    return false;
                fromSlot.Itemstack.Attributes.RemoveAttribute("chuteQHTravelled");
                fromSlot.Itemstack.Attributes.RemoveAttribute("chuteDir");
                ItemSlot autoPushIntoSlot = blockEntity.Inventory.GetAutoPushIntoSlot(outputFace.Opposite, fromSlot);
                BlockEntityItemFlow blockEntityItemFlow = blockEntity as BlockEntityItemFlow;
                if (autoPushIntoSlot != null && (blockEntityItemFlow == null || autoPushIntoSlot.Empty))
                {
                    ItemStackMoveOperation op = new ItemStackMoveOperation(this.Api.World, EnumMouseButton.Left, (EnumModifierKey)0, EnumMergePriority.DirectMerge, allowedNow);
                    int num3 = fromSlot.TryPutInto(autoPushIntoSlot, ref op);
                    if (num3 > 0)
                    {
                        if (this.Api.World.Rand.NextDouble() < 0.2)
                            this.Api.World.PlaySoundAt(BEManagedChute.hopperTumble, this.Pos, 0.0, range: 8f, volume: 0.5f);
                        if (blockEntityItemFlow != null)
                        {
                            autoPushIntoSlot.Itemstack.Attributes.SetInt("chuteQHTravelled", outputFace.IsHorizontal ? num1 + 1 : 0);
                            if (blockEntityItemFlow is BlockEntityArchimedesScrew)
                                autoPushIntoSlot.Itemstack.Attributes.SetInt("chuteDir", BlockFacing.UP.Index);
                            else
                                autoPushIntoSlot.Itemstack.Attributes.SetInt("chuteDir", outputFace.Index);
                        }
                        else
                        {
                            autoPushIntoSlot.Itemstack.Attributes.RemoveAttribute("chuteQHTravelled");
                            autoPushIntoSlot.Itemstack.Attributes.RemoveAttribute("chuteDir");
                        }
                        fromSlot.MarkDirty();
                        autoPushIntoSlot.MarkDirty();
                        this.MarkDirty();
                        blockEntityItemFlow?.MarkDirty();
                        this.itemFlowAccum -= (float)num3;
                        SubstractRemaining(num3);
                        return true;
                    }
                    fromSlot.Itemstack.Attributes.SetInt("chuteDir", num2);
                }
            }
            return false;
        }

        private bool TrySpitOut(BlockFacing outputFace, int allowedNow)
        {
            // Must be able to spit into an (air/replaceable) block
            if (this.Api.World.BlockAccessor.GetBlock(this.Pos.AddCopy(outputFace)).Replaceable < 6000)
                return false;

            // Compute spawn position first and ensure the chunk for that position is loaded.
            // This prevents "TakeOut()" removing items when the target chunk is unloaded,
            // which can lead to item loss on chunk unload/load boundaries.
            Vec3d spawnPos = this.Pos.ToVec3d().Add(
                0.5 + (double)outputFace.Normalf.X / 2.0,
                0.5 + (double)outputFace.Normalf.Y / 2.0,
                0.5 + (double)outputFace.Normalf.Z / 2.0
            );

            BlockPos spawnBlockPos = new BlockPos(
                (int)Math.Floor(spawnPos.X),
                (int)Math.Floor(spawnPos.Y),
                (int)Math.Floor(spawnPos.Z)
            );

            if (this.Api.World.BlockAccessor.GetChunkAtBlockPos(spawnBlockPos) == null)
                return false;

            ItemSlot itemSlot = this.inventory.FirstOrDefault<ItemSlot>((System.Func<ItemSlot, bool>)(slot => !slot.Empty));
            if (itemSlot == null || itemSlot.Empty)
                return false;

            int takeQty = Math.Min(allowedNow, itemSlot.StackSize);
            ItemStack itemstack = itemSlot.TakeOut(takeQty);
            if (itemstack == null || itemstack.StackSize <= 0)
                return false;

            this.itemFlowAccum -= (float)itemstack.StackSize;
            SubstractRemaining(itemstack.StackSize);

            itemstack.Attributes.RemoveAttribute("chuteQHTravelled");
            itemstack.Attributes.RemoveAttribute("chuteDir");

            float x = (float)((double)outputFace.Normalf.X / 10.0 + (this.Api.World.Rand.NextDouble() / 20.0 - 0.05000000074505806) * (double)Math.Sign(outputFace.Normalf.X));
            float y = (float)((double)outputFace.Normalf.Y / 10.0 + (this.Api.World.Rand.NextDouble() / 20.0 - 0.05000000074505806) * (double)Math.Sign(outputFace.Normalf.Y));
            float z = (float)((double)outputFace.Normalf.Z / 10.0 + (this.Api.World.Rand.NextDouble() / 20.0 - 0.05000000074505806) * (double)Math.Sign(outputFace.Normalf.Z));

            this.Api.World.SpawnItemEntity(itemstack, spawnPos, new Vec3d((double)x, (double)y, (double)z));

            itemSlot.MarkDirty();
            this.MarkDirty();
            return true;
        }

        private bool TryPlace(ItemSlot slot, BlockPos pos, Block blockAtTarget)
        {
            if (blockAtTarget.Code.FirstCodePart() == slot.Itemstack.Collectible.Code.FirstCodePart()) return false; //Prevent it from replacing itself with variants (like with pannable blocks)

            if(slot.Itemstack.Block==null) return false;

            string failureCode = null;
            var placed = slot.Itemstack.Block.TryPlaceBlock(Api.World, null, slot.Itemstack, new BlockSelection
            {
                Position = pos,
                Face = BlockFacing.DOWN
            }, ref failureCode);

            if (placed)
            {
                itemFlowAccum -= 1;
                SubstractRemaining(1);
                slot.TakeOut(1);
                slot.MarkDirty();
                MarkDirty(false, null);
            }
            return placed;
        }

        private bool TryPlacePileableItem(ItemSlot slot, BlockPos pos)
        {
            var IteratingPos = pos.Copy();
            while (IteratingPos.Y > -1)
            {
                var nextBlock = Api.World.BlockAccessor.GetBlock(IteratingPos);
                if (nextBlock.Id == 0)
                {
                    //Slowly traverse down if no block was found
                    IteratingPos.Y--;
                    continue;
                }

                //See if there already is a pile
                var entity = Api.World.BlockAccessor.GetBlockEntity<BlockEntityItemPile>(IteratingPos);
                if (entity != null)
                {
                    if (!slot.Itemstack.Equals(Api.World, entity.inventory[0].Itemstack, GlobalConstants.IgnoredStackAttributes))
                    {
                        //Throw item if it can't be added to the pile
                        return false;
                    }

                    if (entity.MaxStackSize > entity.OwnStackSize)
                    {
                        //If pile isn't already full
                        Api.World.PlaySoundAt(entity.SoundLocation, IteratingPos.X, IteratingPos.Y, IteratingPos.Z, null, 0.88f + (float)Api.World.Rand.NextDouble() * 0.24f, 16f, 1f);
                        var itemSlot = entity.inventory[0];
                        itemSlot.Itemstack.StackSize++;
                        itemSlot.MarkDirty();
                        entity.MarkDirty(false, null);

                        slot.TakeOut(1);
                        slot.MarkDirty();
                        MarkDirty(false, null);

                        if (entity is BlockEntityCoalPile coalPileEntity) Traverse.Create(coalPileEntity).Method("TriggerPileChanged").GetValue();
                        return true;
                    }
                }

                //Go up a block to make a new pile
                IteratingPos.Y++;

                //Ensure the new pile location is actually underneath the chute block placer
                if (IteratingPos.Y > pos.Y) return false;

                var pileableItem = slot.Itemstack.Item as ItemPileable;
                var pileableItemTraverse = Traverse.Create(pileableItem);
                var pileBlock = Api.World.GetBlock(pileableItemTraverse.Property("PileBlockCode").GetValue<AssetLocation>());
                if (pileBlock != null)
                {
                    var success = ((IBlockItemPile)pileBlock).Construct(slot, Api.World, IteratingPos, null);
                    MarkDirty(false, null);
                    return success;
                }
                Api.Logger.Log(EnumLogType.Warning, $"Pileable item does not have a pileblock to put down? ({pileableItem.Code})");
            }

            return false;
        }


        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (this.Api.World is IServerWorldAccessor)
            {
                byte[] bytes = BlockEntityContainerOpen.ToBytes("BlockEntityItemFlowDialog", Lang.Get(this.ItemFlowObjectLangCode), (byte)4, (InventoryBase)this.inventory);
                ((ICoreServerAPI)this.Api).Network.SendBlockEntityPacket((IServerPlayer)byPlayer, this.Pos, 5000, bytes);
                byPlayer.InventoryManager.OpenInventory((IInventory)this.inventory);
            }
            return true;
        }

        public override void OnReceivedServerPacket(int packetid, byte[] data)
        {
            base.OnReceivedServerPacket(packetid, data);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            this.InitInventory();
            int index = tree.GetInt("lastReceivedFromDir");
            this.LastReceivedFromDir = index >= 0 ? BlockFacing.ALLFACES[index] : (BlockFacing)null;

            signalState = tree.GetBytes("state", new byte[1] { 0 })[0];

            base.FromTreeAttributes(tree, worldForResolving);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            ITreeAttribute treeAttribute = tree;
            BlockFacing lastReceivedFromDir = this.LastReceivedFromDir;
            int num = lastReceivedFromDir != null ? lastReceivedFromDir.Index : -1;
            treeAttribute.SetInt("lastReceivedFromDir", num);
            tree.SetBytes("state", new byte[1] { signalState });
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
        {
            if (this.Block is ManagedChute)
            {
                foreach (BlockEntityBehavior behavior in this.Behaviors)
                    behavior.GetBlockInfo(forPlayer, sb);
                sb.AppendLine(Lang.Get("Transporting: {0}", this.inventory[0].Empty ? (object)Lang.Get("nothing") : (object)$"{this.inventory[0].StackSize.ToString()}x {this.inventory[0].GetStackName()}"));
                sb.AppendLine("                                                             ");
            }
            else
            {
                base.GetBlockInfo(forPlayer, sb);
                sb.AppendLine(Lang.Get("Contents: {0}", this.inventory[0].Empty ? (object)Lang.Get("Empty") : (object)$"{this.inventory[0].StackSize.ToString()}x {this.inventory[0].GetStackName()}"));
            }
        }

        public override void OnBlockBroken(IPlayer byPlayer = null)
        {
            if (this.Api.World is IServerWorldAccessor)
                this.DropContents();
            base.OnBlockBroken(byPlayer);
        }

        private void DropContents()
        {
            Vec3d position = this.Pos.ToVec3d().Add(0.5, 0.5, 0.5);
            foreach (ItemSlot itemSlot in (InventoryBase)this.inventory)
            {
                if (itemSlot.Itemstack != null)
                {
                    itemSlot.Itemstack.Attributes.RemoveAttribute("chuteQHTravelled");
                    itemSlot.Itemstack.Attributes.RemoveAttribute("chuteDir");
                    this.Api.World.SpawnItemEntity(itemSlot.Itemstack, position);
                    itemSlot.Itemstack = (ItemStack)null;
                    itemSlot.MarkDirty();
                }
            }
        }

        public override void OnBlockRemoved()
        {
            if (this.Api.World is IServerWorldAccessor)
                this.DropContents();
            base.OnBlockRemoved();
        }

        public override void OnExchanged(Block block)
        {
            base.OnExchanged(block);
            this.parseBlockProperties();
        }

        public void OnValueChanged(NodePos pos, byte value)
        {
            if (pos.index != 0) return;
            if (signalState == value) return;

            if (value >= 1 && value <= 7)
            {
                remaining += 1 << (value - 1);
                // volitelně omez max, aby ti to nepřeteklo při blbnutí signálem
                // remaining = Math.Min(remaining, 1000000);
                placing = false;
            }
            else if (value == PLACE_SIGNAL)
            {
                remaining += 1;
                placing = true;
            }

            // 8 = trvale otevřeno
            if (value == 8)
            {
                unlimited = true;
                placing = false;
            }
            else if (signalState == 8 && value != 8)
            {
                // odchod z 8: zavři "unlimited", ale kredit nech jak byl
                unlimited = false;
            }
            

            signalState = value;

            this.MarkDirty();
        }

    }
}
