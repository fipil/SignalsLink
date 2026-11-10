using SignalsLink.src.signals.bridges.iw;
using SignalsLink.src.signals.managedchute.transporting;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.Tests;

/// <summary>
/// The chopper as a device sees it: a log goes onto the bed only when the chopper's own gate says
/// so, a head goes onto the arm only while there is none, and nothing ever comes back out.
/// </summary>
public class ChopperEndTests
{
    // Answers nothing, but every child (world, class registry) is a proxy too, not null.
    static readonly ICoreAPI Api = AnchorLifecycleTests.Proxy.Make<ICoreAPI>((m, a) => AnchorLifecycleTests.Proxy.Unhandled);
    static readonly BlockPos Cell = new BlockPos(1, 1, 0, 0);

    static PaperConditionsEvaluator Eval(string paper = "*") { var e = new PaperConditionsEvaluator(); e.SetConditionsText(paper); return e; }
    static ItemStackMoveOperation Op(int n = 1) => new(null, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, n);
    static ItemStack Log(int n = 1) => Stack("game:log-placed-oak-ud", n);
    static ItemStack Head() => Stack("immersivewoodworking:chopperhead-copper");

    // Items that compare by reference, as a registered item does; a bare Item cannot compare
    // attributes without a game behind it.
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Item> Items = new();
    static ItemStack Stack(string code, int n = 1)
        => new ItemStack(Items.GetOrAdd(code, c => new SameItem { Code = new AssetLocation(c), MaxStackSize = 64 }), n) { Attributes = new Vintagestory.API.Datastructures.TreeAttribute() };
    class SameItem : Item { public override bool Equals(ItemStack a, ItemStack b, params string[] ignored) => ReferenceEquals(a.Collectible, b.Collectible); }

    [Fact]
    public void A_log_goes_onto_the_empty_bed_from_the_back()
    {
        var chopper = new FakeChopper();
        var chest = Chest(Log(3));

        Assert.Equal(1, Transfer(chest, chopper).TryMove(Op()).MovedAmount);
        Assert.Equal(1, chopper.Bed[0].StackSize);
        Assert.Equal(2, chest[0].StackSize);
        Assert.Equal(1, chopper.ChangedCount);
    }

    [Fact]
    public void The_chopper_decides_the_side()
    {
        var chopper = new FakeChopper();
        Assert.False(Transfer(Chest(Log()), chopper, BlockFacing.EAST).TryMove(Op()).Success);
        Assert.True(chopper.Bed[0].Empty);
    }

    [Fact]
    public void A_reader_without_a_side_puts_nothing_in()
    {
        var chopper = new FakeChopper();
        Assert.False(Transfer(Chest(Log()), chopper, null).TryMove(Op()).Success);
    }

    [Fact]
    public void A_loaded_bed_takes_no_second_log()
    {
        var chopper = new FakeChopper();
        var chest = Chest(Log(3));
        var transfer = Transfer(chest, chopper);

        Assert.True(transfer.TryMove(Op()).Success);
        Assert.False(transfer.TryMove(Op()).Success);
        Assert.Equal(1, chopper.Bed[0].StackSize);
        Assert.Equal(2, chest[0].StackSize);
    }

    [Fact]
    public void The_bed_waits_while_the_axe_is_down()
    {
        var chopper = new FakeChopper { AxeUp = false };
        Assert.False(Transfer(Chest(Log()), chopper).TryMove(Op()).Success);
        chopper.AxeUp = true;
        Assert.True(Transfer(Chest(Log()), chopper).TryMove(Op()).Success);
    }

    [Fact]
    public void A_head_goes_onto_the_arm_while_there_is_none()
    {
        var chopper = new FakeChopper();
        var chest = Chest(Head());

        Assert.Equal(1, Transfer(chest, chopper).TryMove(Op()).MovedAmount);
        Assert.NotNull(chopper.Head);
        Assert.True(chest[0].Empty);
        Assert.Equal(1, chopper.ChangedCount);
    }

    [Fact]
    public void A_fitted_head_refuses_another_until_it_is_worn_out()
    {
        var chopper = new FakeChopper { Head = Head() };
        var chest = Chest(Head());

        Assert.False(Transfer(chest, chopper).TryMove(Op()).Success);

        chopper.Head = null; // the chopper destroys the worn head itself
        Assert.True(Transfer(chest, chopper).TryMove(Op()).Success);
        Assert.NotNull(chopper.Head);
    }

    [Fact]
    public void No_arm_no_head()
    {
        var chopper = new FakeChopper { HasArm = false };
        Assert.False(Transfer(Chest(Head()), chopper).TryMove(Op()).Success);
    }

    [Fact]
    public void A_version_whose_head_cannot_be_reached_still_takes_logs()
    {
        var chopper = new FakeChopper { CanFitHead = false };
        Assert.False(Transfer(Chest(Head()), chopper).TryMove(Op()).Success);
        Assert.True(Transfer(Chest(Log()), chopper).TryMove(Op()).Success);
    }

    [Fact]
    public void Only_a_head_goes_onto_the_arm_and_only_a_log_onto_the_bed()
    {
        var chopper = new FakeChopper();
        Assert.False(Transfer(Chest(Stack("game:stick", 4)), chopper).TryMove(Op()).Success);

        var chest = Chest(Head(), Log(2));
        var transfer = Transfer(chest, chopper);
        Assert.True(transfer.TryMove(Op()).Success);
        Assert.True(transfer.TryMove(Op()).Success);
        Assert.NotNull(chopper.Head);
        Assert.Equal(1, chopper.Bed[0].StackSize);
    }

    [Fact]
    public void The_view_shows_the_log_and_the_head_to_conditions()
    {
        var chopper = new FakeChopper { Head = Head() };
        chopper.Bed[0].Itemstack = Log();
        IInventory view = new ChopperEnd(Api, chopper, chopper.Master, Cell, BlockFacing.NORTH).View;

        Assert.Equal(2, view.Count);
        Assert.Same(chopper.Bed[0].Itemstack, view[ChopperView.LogSlot].Itemstack);
        Assert.Same(chopper.Head, view[ChopperView.HeadSlot].Itemstack);

        chopper.Head = null;
        Assert.True(view[ChopperView.HeadSlot].Empty);
    }

    [Fact]
    public void Nothing_comes_back_out()
    {
        var chopper = new FakeChopper { Head = Head() };
        chopper.Bed[0].Itemstack = Log();
        var chest = Chest();
        var view = new ChopperEnd(Api, chopper, chopper.Master, Cell, BlockFacing.NORTH).View;

        var transfer = new InventoryToInventoryTransfer(Api, view, chest, null, 0, 0, Eval());

        Assert.False(transfer.TryMove(Op()).Success);
        Assert.Equal(1, chopper.Bed[0].StackSize);
        Assert.NotNull(chopper.Head);
        Assert.True(chest[0].Empty);
    }

    [Fact]
    public void A_head_is_told_from_a_look_alike()
    {
        Assert.True(ChopperHeadSlot.IsHead(Head()));
        Assert.False(ChopperHeadSlot.IsHead(Stack("game:chopperhead-copper")));
        Assert.False(ChopperHeadSlot.IsHead(Stack("immersivewoodworking:chopperarm")));
        Assert.False(ChopperHeadSlot.IsHead(null));
    }

    // ---------------------------------------------------------------- plumbing

    static IItemTransfer Transfer(InventoryGeneric chest, FakeChopper chopper, BlockFacing face)
    {
        var end = new ChopperEnd(Api, chopper, chopper.Master, Cell, face);
        return new EndRefreshTransfer(Api, new InventoryToInventoryTransfer(Api, chest, end.View, Cell, 0, 0, Eval()), end);
    }

    static IItemTransfer Transfer(InventoryGeneric chest, FakeChopper chopper) => Transfer(chest, chopper, BlockFacing.NORTH);

    static TransferEndRegistryTests.QuietInventory Chest(params ItemStack[] stacks)
    {
        var inv = new TransferEndRegistryTests.QuietInventory(4);
        for (int i = 0; i < stacks.Length; i++) inv[i].Itemstack = stacks[i];
        return inv;
    }

    /// <summary>A chopper that admits a log from the north while the axe is up and the bed is empty.</summary>
    sealed class FakeChopper : IChopperAccess
    {
        public readonly BlockEntity Master = new MasterEntity();
        public readonly TransferEndRegistryTests.QuietInventory Bed = new(1);
        public ItemStack Head;
        public bool HasArm = true;
        public bool AxeUp = true;
        public bool CanFitHead { get; set; } = true;
        public int ChangedCount;

        public bool IsPart(BlockEntity be) => ReferenceEquals(be, Master);
        BlockEntity IChopperAccess.Master(IBlockAccessor ba, BlockEntity be) => Master;
        public ItemSlot InputSlot(BlockEntity master) => Bed[0];

        public ItemSlot AutoPushSlot(BlockEntity master, BlockFacing face, ItemSlot from, BlockPos cell)
            => AxeUp && face == BlockFacing.NORTH && Bed[0].Empty && cell.Equals(Cell)
               && from?.Itemstack?.Collectible?.Code?.Path?.StartsWith("log-") == true
                ? Bed[0] : null;

        ItemStack IChopperAccess.Head(BlockEntity master) => Head;
        bool IChopperAccess.HasArm(BlockEntity master) => HasArm;
        public void SetHead(BlockEntity master, ItemStack head) => Head = head;
        public void Changed(BlockEntity master) => ChangedCount++;
    }

    sealed class MasterEntity : BlockEntity { }
}
