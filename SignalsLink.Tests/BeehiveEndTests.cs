using SignalsLink.src.signals.bridges.beehives;
using SignalsLink.src.signals.managedchute.transporting;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SignalsLink.Tests;

/// <summary>
/// The hive as a device sees it: a frame goes into an empty slot only, one per slot, nothing
/// else goes in, and any frame comes back out.
/// </summary>
public class BeehiveEndTests
{
    static readonly ICoreAPI Api = AnchorLifecycleTests.Proxy.Make<ICoreAPI>((m, a) => AnchorLifecycleTests.Proxy.Unhandled);
    static readonly BlockPos At = new BlockPos(1, 1, 0, 0);

    static PaperConditionsEvaluator Eval(string paper = "*") { var e = new PaperConditionsEvaluator(); e.SetConditionsText(paper); return e; }
    static ItemStackMoveOperation Op(int n = 1) => new(null, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, n);
    static ItemStack Empty(int n = 1) => Stack("orekiwoofsbeehives:beehiveframe-empty", n);
    static ItemStack Filled(int n = 1) => Stack("orekiwoofsbeehives:beehiveframe-filled", n);
    static ItemStack Stone(int n = 1) => Stack("game:stone-granite", n);

    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Item> Items = new();
    static ItemStack Stack(string code, int n = 1)
        => new ItemStack(Items.GetOrAdd(code, c => new SameItem { Code = new AssetLocation(c), MaxStackSize = 64 }), n) { Attributes = new Vintagestory.API.Datastructures.TreeAttribute() };
    class SameItem : Item { public override bool Equals(ItemStack a, ItemStack b, params string[] ignored) => ReferenceEquals(a.Collectible, b.Collectible); }

    [Fact]
    public void Frames_go_in_one_per_empty_slot()
    {
        var hive = new FakeHive();
        var chest = Chest(Empty(3));

        Assert.Equal(3, Into(chest, hive).TryMove(Op(8)).MovedAmount);
        Assert.Equal(1, hive.Frames[0].StackSize);
        Assert.Equal(1, hive.Frames[1].StackSize);
        Assert.Equal(1, hive.Frames[2].StackSize);
        Assert.True(hive.Frames[3].Empty);
        Assert.True(chest[0].Empty);
        Assert.Equal(1, hive.ChangedCount);
    }

    [Fact]
    public void A_loaded_slot_takes_no_second_frame()
    {
        var hive = new FakeHive();
        hive.Frames[0].Itemstack = Empty();
        var chest = Chest(Empty(2));

        Assert.Equal(1, Into(chest, hive).TryMove(Op()).MovedAmount);
        Assert.Equal(1, hive.Frames[0].StackSize);
        Assert.Equal(1, hive.Frames[1].StackSize);
    }

    [Fact]
    public void A_full_hive_takes_nothing()
    {
        var hive = new FakeHive();
        for (int i = 0; i < 8; i++) hive.Frames[i].Itemstack = Filled();

        Assert.False(Into(Chest(Empty()), hive).TryMove(Op()).Success);
        Assert.Equal(0, hive.ChangedCount);
    }

    [Fact]
    public void Nothing_but_a_frame_goes_in()
    {
        var hive = new FakeHive();
        var chest = Chest(Stone(5), Stack("game:beehiveframe-empty"));

        Assert.False(Into(chest, hive).TryMove(Op()).Success);
        Assert.True(hive.Frames[0].Empty);
        Assert.Equal(5, chest[0].StackSize);
    }

    [Fact]
    public void A_filled_frame_comes_out_and_an_empty_one_stays()
    {
        var hive = new FakeHive();
        hive.Frames[0].Itemstack = Empty();
        hive.Frames[1].Itemstack = Filled();
        var chest = Chest();

        Assert.Equal(1, OutOf(hive, chest, "*beehiveframe-filled").TryMove(Op()).MovedAmount);
        Assert.True(hive.Frames[1].Empty);
        Assert.Equal(1, hive.Frames[0].StackSize);
        Assert.Equal("beehiveframe-filled", chest[0].Itemstack.Collectible.Code.Path);
        Assert.Equal(1, hive.ChangedCount);
    }

    [Fact]
    public void Frames_are_the_mods_own()
    {
        Assert.True(BeehiveSlot.IsFrame(Empty()));
        Assert.True(BeehiveSlot.IsFrame(Stack("orekiwoofsbeehives:beehiveframe-filled-feed")));
        Assert.False(BeehiveSlot.IsFrame(Stack("game:beehiveframe-empty")));
        Assert.False(BeehiveSlot.IsFrame(Stone()));
        Assert.False(BeehiveSlot.IsFrame(null));
    }

    [Fact]
    public void The_view_reads_the_hive_as_it_is_now()
    {
        var hive = new FakeHive();
        var view = new BeehiveEnd(Api, hive, hive.Entity, hive.Frames).View;

        Assert.Equal(8, view.Count);
        Assert.True(view[2].Empty);
        hive.Frames[2].Itemstack = Filled();
        Assert.Equal("beehiveframe-filled", view[2].Itemstack.Collectible.Code.Path);
        Assert.Null(view[8]);
    }

    static IItemTransfer Into(InventoryGeneric chest, FakeHive hive)
    {
        var end = new BeehiveEnd(Api, hive, hive.Entity, hive.Frames);
        return new EndRefreshTransfer(Api, new InventoryToInventoryTransfer(Api, chest, end.View, At, 0, 0, Eval()), end);
    }

    static IItemTransfer OutOf(FakeHive hive, InventoryGeneric chest, string paper)
    {
        var end = new BeehiveEnd(Api, hive, hive.Entity, hive.Frames);
        return new EndRefreshTransfer(Api, new InventoryToInventoryTransfer(Api, end.View, chest, At, 0, 0, Eval(paper)), end);
    }

    static TransferEndRegistryTests.QuietInventory Chest(params ItemStack[] stacks)
    {
        var inv = new TransferEndRegistryTests.QuietInventory(4);
        for (int i = 0; i < stacks.Length; i++) inv[i].Itemstack = stacks[i];
        return inv;
    }

    sealed class FakeHive : IBeehiveAccess
    {
        public readonly BlockEntity Entity = new HiveEntity();
        public readonly TransferEndRegistryTests.QuietInventory Frames = new(8);
        public int ChangedCount;

        public bool IsHive(BlockEntity be) => ReferenceEquals(be, Entity);
        IInventory IBeehiveAccess.Frames(BlockEntity hive) => Frames;
        public void Changed(BlockEntity hive) => ChangedCount++;
    }

    sealed class HiveEntity : BlockEntity { }
}

/// <summary>What the hive bridge demands of OrekiWoof's Beehives before it touches anything.</summary>
public class BeehiveSurfaceTests
{
    [Fact]
    public void The_known_hive_reaches_its_frames()
    {
        BeehiveSurface surface = BeehiveSurface.Probe(typeof(BlockEntityReusableBeehive));

        Assert.True(surface.CanReachFrames);
        Assert.Equal("", surface.Complaint);
        Assert.Equal(typeof(BlockEntityReusableBeehive), surface.HiveType);
    }

    [Fact]
    public void The_fingerprint_is_the_one_written_against()
    {
        // OrekiWoof's Beehives 2.0.0; a change here means KnownGood needs a look.
        Assert.Equal("8a573641690c", BeehiveSurface.Probe(typeof(BlockEntityReusableBeehive)).Surface);
    }

    [Fact]
    public void A_hive_that_is_no_container_stands_the_bridge_down()
    {
        BeehiveSurface surface = BeehiveSurface.Probe(typeof(StrangeHive));

        Assert.False(surface.CanReachFrames);
        Assert.Contains("BlockEntityContainer", surface.Complaint);
    }

    [Fact]
    public void No_hive_type_stands_the_bridge_down()
    {
        BeehiveSurface surface = BeehiveSurface.Probe((System.Type)null);

        Assert.False(surface.CanReachFrames);
        Assert.Contains("not there", surface.Complaint);
    }

    // Look-alikes: the name is what the fingerprint sees, so it is the mod's own.
    abstract class BlockEntityReusableBeehive : BlockEntityContainer { }
    sealed class StrangeHive : BlockEntity { }
}
