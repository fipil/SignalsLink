using SignalsLink.src.signals.testchest;
using SignalsLink.src.signals.managedchute.transporting;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using static SignalsLink.Tests.AnchorLifecycleTests;

namespace SignalsLink.Tests;

public class TestChestTests
{
    private static readonly ICoreAPI Api = Proxy.Make<ICoreAPI>((m, a) => Proxy.Unhandled);
    private static InventoryGeneric Inventory(int count = 2) => new(count, "test-chest", Api);
    private static ItemStack Stack(string code, int count) => new(new TestItem { Code = new AssetLocation(code), MaxStackSize = 64 }, count);
    private sealed class TestItem : Item
    {
        public override bool Equals(ItemStack a, ItemStack b, params string[] ignored) => a.Collectible.Code == b.Collectible.Code;
        public override void OnModifiedInInventorySlot(IWorldAccessor world, ItemSlot slot, ItemStack extractedStack = null) { }
    }
    private static void Put(InventoryBase inventory, int slot, ItemStack stack)
    { inventory[slot].Itemstack = stack; inventory[slot].MarkDirty(); }

    private sealed class RigSink : IConditionOutputSink
    { public byte Value; public void ApplyOutput(int pin,byte value)=>Value=value; }
    [Theory]
    [InlineData(0,false)] [InlineData(5,false)] [InlineData(10,true)]
    public void Controlled_rig_papers_meter_eight_and_observe_target_independently_of_recycler(int interval,bool half)
    {
        var supply=Inventory(); var source=Inventory(); var target=Inventory(); var recycler=Inventory();
        using var infinite=new ChestStock(supply,null,true,null);
        using var disposal=new ChestStock(recycler,null,false,null); disposal.Configure(interval,half);
        Put(supply,0,Stack("game:firewood",4)); Put(supply,1,Stack("game:firewood",12));
        InventoryToInventoryTransfer Transfer(InventoryGeneric from,InventoryGeneric to,string text)
        { var e=new PaperConditionsEvaluator(); e.SetConditionsText(text); return new(Api,from,to,null,0,0,e); }
        var fill=Transfer(supply,source,SignalsLink.Testing.PersistentChuteRig.FillPaper);
        var test=Transfer(source,target,SignalsLink.Testing.PersistentChuteRig.Paper);
        var drain=Transfer(target,recycler,SignalsLink.Testing.PersistentChuteRig.DrainPaper);
        var sink=new RigSink(); test.OutputSink=sink;
        ItemStackMoveOperation Op()=>new(Api.World,EnumMouseButton.Left,0,EnumMergePriority.DirectMerge,1);
        for(int cycle=0;cycle<40;cycle++)
        {
            Assert.Equal(8,fill.TryMove(Op()).MovedAmount);
            Assert.False(fill.TryMove(Op()).Success); // Target gate prevents a second batch.
            test.EvaluateOutputs(); Assert.Equal(0,sink.Value); Assert.Equal(8,source.Sum(s=>s.StackSize));
            Assert.Equal(8,test.TryMove(Op()).MovedAmount);
            Assert.True(source.Empty); Assert.Equal(8,target.Sum(s=>s.StackSize));
            test.EvaluateOutputs(); Assert.Equal(9,sink.Value); // Source is empty, target is not.
            var move=drain.TryMove(Op());
            if(!move.Success) { disposal.Tick(10); move=drain.TryMove(Op()); }
            Assert.Equal(8,move.MovedAmount); Assert.True(target.Empty);
            test.EvaluateOutputs(); Assert.Equal(0,sink.Value);
            Assert.Equal((cycle+1)*8,infinite.Supplied); Assert.Equal(infinite.Supplied,disposal.Received);
            Assert.Equal(disposal.Received,recycler.Sum(s=>s.StackSize)+disposal.Recycled);
        }
    }
    [Fact]
    public void Each_source_slot_refills_its_own_item_and_original_count()
    {
        var inventory = Inventory(); using var stock = new ChestStock(inventory, null, true, null);
        Put(inventory, 0, Stack("game:firewood", 4)); Put(inventory, 1, Stack("game:stone", 12));
        Assert.Equal(4, inventory[0].TakeOut(4).StackSize);
        Assert.Equal(3, inventory[1].TakeOut(3).StackSize); inventory[1].MarkDirty();
        Assert.Equal(4, inventory[0].StackSize); Assert.Equal(12, inventory[1].StackSize);
        Assert.Equal("game:stone", inventory[1].Itemstack.Collectible.Code.ToString());
        Assert.Equal(7, stock.Supplied); Assert.Equal(2, stock.TemplateCount);
        stock.Clear(); Assert.True(inventory.Empty); Assert.Equal(0, stock.TemplateCount);
    }
    [Fact]
    public void Normal_transfer_and_paper_conditions_can_read_the_real_source_inventory()
    {
        var source = Inventory(); var target = Inventory();
        using var stock = new ChestStock(source, null, true, null);
        Put(source, 0, Stack("game:firewood", 4)); Put(source, 1, Stack("game:stone", 12));
        var paper = new PaperConditionsEvaluator(); paper.SetConditionsText("game:stone\namount 3");
        var transfer = new InventoryToInventoryTransfer(Api, source, target, null, 0, 0, paper);
        var result = transfer.TryMove(new ItemStackMoveOperation(Api.World, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, 1));
        Assert.Equal(3, result.MovedAmount); Assert.Equal(12, source[1].StackSize);
        Assert.Equal("game:stone", target[0].Itemstack.Collectible.Code.ToString());
    }
    [Fact]
    public void Recycler_waits_until_full_then_five_seconds_and_preserves_arrival_order()
    {
        var inventory = Inventory(); using var stock = new ChestStock(inventory, null, false, null);
        Put(inventory, 1, Stack("game:stone", 64)); stock.Tick(20);
        Assert.Equal(64, inventory[1].StackSize); Assert.False(stock.Draining);
        Put(inventory, 0, Stack("game:firewood", 64)); stock.Tick(4.9);
        Assert.Equal(64, inventory[1].StackSize); stock.Tick(.2);
        Assert.True(inventory[1].Empty); Assert.Equal(64, inventory[0].StackSize);
        stock.Tick(5); Assert.True(inventory.Empty); Assert.Equal(128, stock.Recycled);
    }
    [Fact]
    public void Merges_and_external_withdrawals_do_not_corrupt_recycler_order()
    {
        var inventory = Inventory(); using var stock = new ChestStock(inventory, null, false, null);
        var wood = Stack("game:firewood", 32); Put(inventory, 0, wood);
        Put(inventory, 1, Stack("game:stone", 64)); wood.StackSize = 64; inventory[0].MarkDirty();
        inventory[0].TakeOut(16); inventory[0].MarkDirty(); stock.Tick(5);
        Assert.Equal(32, inventory[0].StackSize); Assert.Equal(64, inventory[1].StackSize);
        stock.Tick(5); Assert.True(inventory[1].Empty); Assert.Equal(32, inventory[0].StackSize);
    }
    [Fact]
    public void Recycler_queue_and_remaining_delay_survive_reload()
    {
        var inventory = Inventory(); var stock = new ChestStock(inventory, null, false, null);
        Put(inventory, 1, Stack("game:stone", 64)); Put(inventory, 0, Stack("game:firewood", 64)); stock.Tick(3);
        var tree = new TreeAttribute(); stock.Write(tree); stock.Dispose();
        using var loaded = new ChestStock(inventory, null, false, null); loaded.Read(tree);
        loaded.Tick(1); Assert.False(inventory[1].Empty); loaded.Tick(1);
        Assert.True(inventory[1].Empty); Assert.Equal(64, inventory[0].StackSize); Assert.Equal(128, loaded.Received);
    }
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(5)] [InlineData(10)]
    public void Recycler_respects_configured_delay(int seconds)
    {
        var inventory = Inventory(1); using var stock = new ChestStock(inventory, null, false, null);
        stock.Configure(seconds, false); Put(inventory, 0, Stack("game:firewood", 64));
        stock.Tick(seconds - .1); Assert.False(inventory.Empty);
        stock.Tick(.2); Assert.True(inventory.Empty); Assert.Equal(64, stock.Recycled);
    }
    [Fact]
    public void Half_threshold_counts_partial_stacks_and_survives_reload()
    {
        var inventory = Inventory(); var stock = new ChestStock(inventory, null, false, null);
        stock.Configure(10, true);
        Put(inventory, 0, Stack("game:firewood", 32)); Assert.False(stock.Draining);
        Put(inventory, 1, Stack("game:stone", 32)); Assert.True(stock.Draining); stock.Tick(3);
        var tree = new TreeAttribute(); stock.Write(tree); stock.Dispose();
        using var loaded = new ChestStock(inventory, null, false, null); loaded.Read(tree);
        Assert.Equal(10, loaded.IntervalSeconds); Assert.True(loaded.StartAtHalf);
        Assert.Equal(7, loaded.DelayRemaining); loaded.Tick(7);
        Assert.True(inventory[0].Empty); Assert.Equal(32, inventory[1].StackSize);
    }
    [Fact]
    public void Immediate_mode_does_not_break_the_real_transfer_that_fills_the_chest()
    {
        var source = Inventory(1); var target = Inventory(1);
        using var stock = new ChestStock(target, null, false, null); stock.Configure(0, false);
        Put(source, 0, Stack("game:firewood", 64));
        var paper = new PaperConditionsEvaluator(); paper.SetConditionsText("game:firewood\namount 64");
        var transfer = new InventoryToInventoryTransfer(Api, source, target, null, 0, 0, paper);
        var result = transfer.TryMove(new ItemStackMoveOperation(Api.World, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, 1));
        Assert.Equal(64, result.MovedAmount); Assert.True(source.Empty); Assert.True(target.Empty);
        Assert.Equal(64, stock.Received); Assert.Equal(64, stock.Recycled);
    }
    [Fact]
    public void Immediate_half_mode_waits_for_threshold_then_drains_all_fifo_batches()
    {
        var inventory = Inventory(4); using var stock = new ChestStock(inventory, null, false, null);
        stock.Configure(0, true); Put(inventory, 2, Stack("game:stone", 64)); Assert.False(inventory.Empty);
        Put(inventory, 0, Stack("game:firewood", 64)); Assert.True(inventory.Empty); Assert.Equal(128, stock.Recycled);
        Assert.Throws<ArgumentOutOfRangeException>(() => stock.Configure(3, false));
    }

    [Fact]
    public void Source_templates_survive_reload_and_can_be_replaced()
    {
        var inventory = Inventory(); var stock = new ChestStock(inventory, null, true, null);
        Put(inventory, 0, Stack("game:firewood", 8)); Put(inventory, 1, Stack("game:stone", 12));
        var tree = new TreeAttribute(); stock.Write(tree); stock.Dispose();
        using var loaded = new ChestStock(inventory, null, true, null); loaded.Read(tree);
        inventory[1].TakeOut(12); Assert.Equal(12, inventory[1].StackSize);
        Put(inventory, 0, Stack("game:stone", 3)); inventory[0].TakeOut(2); inventory[0].MarkDirty(); Assert.Equal(3, inventory[0].StackSize);
    }
}
