using System.Reflection;
using SignalsLink.src.signals.managedchute.transporting;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.API.Datastructures;
using Xunit;

namespace SignalsLink.Tests;

public class WorldAmountTests
{
    [Theory]
    [InlineData("32-", 40, 32)]
    [InlineData("32-", 5, 5)]
    [InlineData("1+", 40, 40)]
    [InlineData("32", 31, 0)]
    [InlineData("32+", 31, 0)]
    [InlineData("32", 40, 32)]
    public void Loose_stack_obeys_amount_and_conserves_items(string amount, int available, int expected)
    {
        var f = new WorldFixture();
        var drop = f.Drop(available);
        var target = new QuietChest(2);
        var result = f.Pickup(target, "*\namount " + amount);
        Assert.Equal(expected, result.MovedAmount);
        Assert.Equal(expected, target.Sum(s => s.StackSize));
        Assert.Equal(available - expected, drop.Itemstack?.StackSize ?? 0);
        Assert.Equal(expected == available, drop.PickedUp);
    }

    [Theory]
    [InlineData("32", 32)]
    [InlineData("32-", 32)]
    [InlineData("1+", 40)]
    public void Loose_batch_combines_matching_entities(string amount, int expected)
    {
        var f = new WorldFixture(); f.Drop(20); f.Drop(20);
        var target = new QuietChest(2);
        Assert.Equal(expected, f.Pickup(target, "*\namount " + amount).MovedAmount);
        Assert.Equal(40 - expected, f.Drops.Sum(d => d.Itemstack?.StackSize ?? 0));
    }

    [Theory]
    [InlineData("32", 0)]
    [InlineData("32+", 0)]
    [InlineData("32-", 4)]
    [InlineData("1+", 4)]
    public void Loose_batch_respects_target_room(string amount, int expected)
    {
        var f = new WorldFixture(); f.Drop(40);
        var target = new QuietChest(1); target[0].Itemstack = new ItemStack(f.Item, 60);
        Assert.Equal(expected, f.Pickup(target, "*\namount " + amount).MovedAmount);
        Assert.Equal(60 + expected, target[0].StackSize);
        Assert.Equal(40 - expected, f.Drops[0].Itemstack.StackSize);
    }

    [Fact]
    public void Loose_batch_keep_and_target_slot_do_not_overfill()
    {
        var f = new WorldFixture(); f.Drop(40);
        var target = new QuietChest(2); target[1].Itemstack = new ItemStack(f.Item, 6);
        Assert.Equal(2, f.Pickup(target, "*\namount 32\nkeep 8\ntarget 2").MovedAmount);
        Assert.True(target[0].Empty); Assert.Equal(8, target[1].StackSize);
    }

    [Theory]
    [InlineData("32", 40, 32)]
    [InlineData("32-", 5, 5)]
    [InlineData("1+", 40, 40)]
    [InlineData("32", 31, 0)]
    [InlineData("32+", 31, 0)]
    public void Throwing_batches_matching_source_slots(string amount, int available, int expected)
    {
        var f = new WorldFixture(); var source = new QuietChest(2);
        source[0].Itemstack = new ItemStack(f.Item, available / 2);
        source[1].Itemstack = new ItemStack(f.Item, available - available / 2);
        var transfer = new InventoryToWorldTransfer(f.Api, source, 0, f.Pos, 0, Eval("*\namount " + amount));
        Assert.Equal(expected, transfer.TryMove(Op()).MovedAmount);
        Assert.Equal(expected, f.Spawned.Sum(s => s.StackSize));
        Assert.Equal(available - expected, source.Sum(s => s.StackSize));
    }

    [Theory]
    [InlineData("32", 0)]
    [InlineData("32+", 0)]
    [InlineData("32-", 4)]
    public void Inventory_batch_respects_item_stack_limit(string amount, int expected)
    {
        var f = new WorldFixture(); var source = new QuietChest(1); var target = new QuietChest(1);
        source[0].Itemstack = new ItemStack(f.Item, 40);
        target[0].Itemstack = new ItemStack(f.Item, 60);
        var transfer = new InventoryToInventoryTransfer(f.Api, source, target, null, 0, 0, Eval("*\namount " + amount));
        Assert.Equal(expected, transfer.TryMove(Op()).MovedAmount);
        Assert.Equal(60 + expected, target[0].StackSize);
        Assert.Equal(40 - expected, source[0].StackSize);
    }

    [Theory]
    [InlineData("2", 0)]
    [InlineData("2+", 0)]
    [InlineData("2-", 1)]
    [InlineData("1+", 1)]
    public void Placing_one_block_respects_batch_floor(string amount, int expected)
    {
        var f = new WorldFixture(); var block = new PlacedBlock { Code = new AssetLocation("game:chest") };
        var source = new QuietChest(1); source[0].Itemstack = new ItemStack(block, 4);
        var transfer = new InventoryToWorldTransfer(f.Api, source, 0, f.Pos, 1, Eval("*\namount " + amount));
        Assert.Equal(expected, transfer.TryMove(Op()).MovedAmount);
        Assert.Equal(expected, block.Placements);
        Assert.Equal(4 - expected, source[0].StackSize);
    }

    [Theory]
    [InlineData("2", 0)]
    [InlineData("2+", 0)]
    [InlineData("2-", 1)]
    [InlineData("1+", 1)]
    public void Placed_container_is_one_indivisible_item(string amount, int expected)
    {
        var f = new WorldFixture(); var target = new QuietChest(1);
        var transfer = new WorldToInventoryTransfer(f.Api, f.Pos, target, 0, Eval("*\namount " + amount));
        var block = Eval("*\namount " + amount).GetBlocks()[0];
        var method = typeof(WorldToInventoryTransfer).GetMethod("TryMovePlacedLiquidContainer", BindingFlags.NonPublic | BindingFlags.Instance);
        var result = (TransferOperationResult)method.Invoke(transfer, new object[] { new ItemStack(f.Item, 1), block });
        Assert.Equal(expected, result.MovedAmount);
        Assert.Equal(expected, target[0].StackSize);
        Assert.Equal(expected, f.BlockChanges);
    }

    [Theory]
    [InlineData("32", 40, 0, 32)]
    [InlineData("32", 31, 0, 0)]
    [InlineData("32+", 40, 0, 40)]
    [InlineData("32-", 40, 60, 4)]
    [InlineData("32", 40, 60, 0)]
    [InlineData("32+", 40, 60, 0)]
    public void Legacy_pile_obeys_batch_and_capacity(string amount, int available, int occupied, int expected)
    {
        var f = new WorldFixture(); var pile = new LegacyPile { Pos = f.Pos, inventory = new QuietChest(1) };
        if (occupied > 0) pile.inventory[0].Itemstack = new ItemStack(f.Item, occupied);
        f.TargetEntity = pile;
        var source = new QuietChest(2);
        source[0].Itemstack = new ItemStack(f.Item, available / 2);
        source[1].Itemstack = new ItemStack(f.Item, available - available / 2);
        var transfer = new InventoryToWorldTransfer(f.Api, source, 0, f.Pos, 2, Eval("*\namount " + amount));
        Assert.Equal(expected, transfer.TryMove(Op()).MovedAmount);
        Assert.Equal(occupied + expected, pile.OwnStackSize);
        Assert.Equal(available - expected, source.Sum(s => s.StackSize));
    }

    [Fact]
    public void Partial_pickup_marks_entity_stack_for_client_sync()
    {
        var f = new WorldFixture(); var drop = f.Drop(40); drop.WatchedAttributes.MarkClean();
        Assert.Equal(32, f.Pickup(new QuietChest(1), "*\namount 32-").MovedAmount);
        Assert.True(drop.WatchedAttributes.PartialDirty || drop.WatchedAttributes.AllDirty);
    }

    [Fact]
    public void Different_attributes_are_not_combined_into_an_atomic_batch()
    {
        var f = new WorldFixture(); f.Drop(20); f.Drop(20).Itemstack.Attributes.SetString("quality", "other");
        Assert.Equal(0, f.Pickup(new QuietChest(2), "*\namount 32").MovedAmount);
        Assert.Equal(40, f.Drops.Sum(d => d.Itemstack.StackSize));
    }

    [Fact]
    public void A_later_item_group_can_satisfy_the_floor_and_spread_across_target_slots()
    {
        var f = new WorldFixture(); var small = f.Drop(4); small.Itemstack = new ItemStack(new EqualItem { Code = new AssetLocation("game:stick"), MaxStackSize = 64 }, 4);
        f.Drop(50); f.Drop(50);
        var target = new QuietChest(2);
        // A floor above the stack cap holds as written: 80, not the whole 100.
        Assert.Equal(80, f.Pickup(target, "*\namount 80+").MovedAmount);
        Assert.Equal(64, target[0].StackSize); Assert.Equal(16, target[1].StackSize); Assert.Equal(4, small.Itemstack.StackSize);
    }

    [Theory]
    [InlineData("source 1", "32", 0)]
    [InlineData("source 1", "1+", 20)]
    [InlineData("source last", "1+", 30)]
    public void Throwing_honours_source_slot_directives(string slot, string amount, int expected)
    {
        var f = new WorldFixture(); var source = new QuietChest(2);
        source[0].Itemstack = new ItemStack(f.Item, 20); source[1].Itemstack = new ItemStack(f.Item, 30);
        var transfer = new InventoryToWorldTransfer(f.Api, source, 0, f.Pos, 0, Eval("*\n" + slot + "\namount " + amount));
        Assert.Equal(expected, transfer.TryMove(Op()).MovedAmount);
        Assert.Equal(50 - expected, source.Sum(s => s.StackSize));
    }

    [Theory]
    [InlineData("32", 3)]
    [InlineData("32+", 3)]
    public void Keep_does_not_wait_for_unavailable_source_batch(string amount, int expected)
    {
        var f = new WorldFixture(); var source = new QuietChest(1); var target = new QuietChest(1);
        source[0].Itemstack = new ItemStack(f.Item, 3);
        var transfer = new InventoryToInventoryTransfer(f.Api, source, target, null, 0, 0, Eval("*\namount " + amount + "\nkeep 8"));
        Assert.Equal(expected, transfer.TryMove(Op()).MovedAmount);
        Assert.Equal(expected, target[0].StackSize);
    }

    [Theory]
    [InlineData("32", 32)]
    [InlineData("80", 0)]
    [InlineData("1+", 64)]
    public void New_legacy_pile_checks_capacity_before_placing_and_takes_a_whole_batch(string amount, int expected)
    {
        var f = new WorldFixture();
        var item = new LegacyItem { Code = new AssetLocation("game:ingot-copper"), MaxStackSize = 100 };
        var block = new LegacyBlock { Code = new AssetLocation("game:testpile"), BlockId = 100, EntityClass = "TestPile" };
        f.Handler = (m, args) =>
        {
            if (m.Name == "GetBlock" && args[0] is AssetLocation) return block;
            if (m.Name == "CreateBlockEntity") return new LegacyPile { inventory = new QuietChest(1) };
            if (m.Name == "SetBlock") { f.BlockChanges++; f.TargetEntity = new LegacyPile { Pos = f.Pos, inventory = new QuietChest(1) }; return null; }
            return AnchorLifecycleTests.Proxy.Unhandled;
        };
        var source = new QuietChest(1); source[0].Itemstack = new ItemStack(item, 100);
        var transfer = new InventoryToWorldTransfer(f.Api, source, 0, f.Pos, 2, Eval("*\namount " + amount));
        Assert.Equal(expected, transfer.TryMove(Op()).MovedAmount);
        Assert.Equal(expected == 0 ? 0 : 1, f.BlockChanges);
        Assert.Equal(100 - expected, source[0].StackSize);
        if (expected > 0) Assert.Equal(expected, ((LegacyPile)f.TargetEntity).OwnStackSize);
    }
    class LegacyItem : ItemPileable { protected override AssetLocation PileBlockCode => new("game:testpile"); }
    class LegacyBlock : Block, IBlockItemPile
    {
        public bool Construct(ItemSlot slot, IWorldAccessor world, BlockPos pos, IPlayer player) => throw new Exception("Machine placement must not call player-only Construct");
    }

    [Theory]
    [InlineData("32", 40, 0, 32)]
    [InlineData("32", 31, 0, 0)]
    [InlineData("1+", 40, 0, 40)]
    [InlineData("32-", 40, 60, 4)]
    [InlineData("32", 40, 60, 0)]
    [InlineData("32+", 40, 60, 0)]
    [InlineData("32\nkeep 8", 3, 0, 3)]
    public void Modern_pile_pickup_obeys_amount_capacity_and_keep(string amount, int available, int occupied, int expected)
    {
        var f = new WorldFixture(); var pile = new ModernPile { Pos = f.Pos };
        pile.Inventory[0].Itemstack = new ItemStack(f.Item, available); f.TargetEntity = pile;
        var target = new QuietChest(1);
        if (occupied > 0) target[0].Itemstack = new ItemStack(f.Item, occupied);
        Assert.Equal(expected, f.Pickup(target, "*\namount " + amount).MovedAmount);
        Assert.Equal(occupied + expected, target[0].StackSize);
        Assert.Equal(available - expected, pile.TotalStackSize);
    }
    class ModernPile : BlockEntityGroundStorage
    {
        public ModernPile() { inventory = new QuietChest(1); StorageProps = new GroundStorageProperties { Layout = EnumGroundStorageLayout.Stacking, StackingCapacity = 128 }; }
        public override void MarkDirty(bool redrawOnClient = false, IPlayer skipPlayer = null) { }
    }

    class PlacedBlock : Block
    {
        public int Placements;
        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer player, ItemStack stack, BlockSelection selection, ref string failureCode) { Placements++; return true; }
    }
    class LegacyPile : BlockEntityItemPile
    {
        public override string BlockCode => "test-pile";
        public override AssetLocation SoundLocation => null;
        public override int MaxStackSize => 64;
        public override void MarkDirty(bool redrawOnClient = false, IPlayer skipPlayer = null) { }
    }

    internal static PaperConditionsEvaluator Eval(string paper) { var e = new PaperConditionsEvaluator(); e.SetConditionsText(paper); return e; }
    internal static ItemStackMoveOperation Op() => new(null, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, 1);
    internal class QuietChest : InventoryGeneric
    {
        public QuietChest(int n) : base(n, "test-worldamount", null, (i, inv) => new QuietSlot(inv)) { }
        public override void DidModifyItemSlot(ItemSlot slot, ItemStack extracted = null) { }
    }
    class QuietSlot : ItemSlot
    {
        public QuietSlot(InventoryBase inv) : base(inv) { }
        public override void OnItemSlotModified(ItemStack stack) { }
        public override void MarkDirty() { }
    }
    internal class EqualItem : Item
    {
        public override bool Equals(ItemStack a, ItemStack b, params string[] ignored) => ReferenceEquals(a.Collectible, b.Collectible) && a.Attributes.ToJsonToken() == b.Attributes.ToJsonToken();
    }
    internal class DropEntity : EntityItem
    {
        public bool PickedUp;
        public override void Die(EnumDespawnReason reason = EnumDespawnReason.Death, DamageSource damageSourceForDeath = null) { PickedUp = true; }
    }
    internal class WorldFixture
    {
        public BlockPos Pos = new(10, 10, 10);
        public EqualItem Item = new() { Code = new AssetLocation("game:firewood"), MaxStackSize = 64 };
        public List<DropEntity> Drops = new();
        public List<ItemStack> Spawned = new();
        public ICoreAPI Api;
        public BlockEntity TargetEntity;
        public System.Func<MethodInfo, object[], object> Handler;
        public int BlockChanges;
        public WorldFixture()
        {
            Api = AnchorLifecycleTests.Proxy.Make<ICoreAPI>((m, args) =>
            {
                if (Handler != null) { var result = Handler(m, args); if (!ReferenceEquals(result, AnchorLifecycleTests.Proxy.Unhandled)) return result; }
                if (m.Name == "GetBlock") return new Block { Code = new AssetLocation("game:air"), Replaceable = 10000, SideSolid = new SmallBoolArray(63) };
                if (m.Name == "GetBlockEntity") return args[0] is BlockPos pos && pos.Equals(Pos) ? TargetEntity : null;
                if (m.Name == "SetBlock") { BlockChanges++; return null; }
                if (m.Name == "GetEntitiesInsideCuboid")
                {
                    var predicate = (ActionConsumable<Entity>)args[2];
                    return Drops.Where(d => !d.PickedUp && predicate(d)).Cast<Entity>().ToArray();
                }
                if (m.Name == "SpawnItemEntity") { Spawned.Add(((ItemStack)args[0]).Clone()); return null; }
                return AnchorLifecycleTests.Proxy.Unhandled;
            });
        }
        public DropEntity Drop(int count) { var d = new DropEntity { Itemstack = new ItemStack(Item, count) }; Drops.Add(d); return d; }
        public TransferOperationResult Pickup(IInventory target, string paper) => new WorldToInventoryTransfer(Api, Pos, target, 0, Eval(paper)).TryMove(Op());
    }
}
