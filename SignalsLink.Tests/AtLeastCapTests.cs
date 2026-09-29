using System.Linq;
using System.Reflection;
using SignalsLink.src.signals.managedchute.transporting;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;

namespace SignalsLink.Tests;

/// <summary>
/// `amount N+` reaches past its floor, but only a stack at a time. Without the cap `amount 1+`
/// emptied a chest of stone in one pass, which made it a cheat rather than a directive.
/// </summary>
public class AtLeastCapTests
{
    static readonly ICoreAPI Api = DispatchProxy.Create<ICoreAPI, ServerTransferRegressionTests.NullApi>();
    static PaperConditionsEvaluator Eval(string paper) { var e = new PaperConditionsEvaluator(); e.SetConditionsText(paper); return e; }
    static ItemStackMoveOperation Op(int n = 1) => new(null, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, n);
    static readonly Item Stone = new SameItem { Code = new AssetLocation("game:stone"), MaxStackSize = 64 };

    [Fact]
    public void One_plus_moves_one_stack_per_pass_not_the_whole_chest()
    {
        var source = Chest(4, 64);
        var target = new QuietInventory(8);
        var transfer = new InventoryToInventoryTransfer(Api, source, target, null, 0, 0, Eval("*\namount 1+"));

        Assert.Equal(64, transfer.TryMove(Op()).MovedAmount);
        Assert.Equal(192, source.Sum(s => s.StackSize));
    }

    [Fact]
    public void A_floor_above_the_cap_still_holds()
    {
        var source = Chest(4, 40);
        var target = new QuietInventory(8);
        var transfer = new InventoryToInventoryTransfer(Api, source, target, null, 0, 0, Eval("*\namount 100+"));

        Assert.Equal(100, transfer.TryMove(Op()).MovedAmount);
    }

    [Fact]
    public void Below_the_cap_everything_still_goes()
    {
        var source = Chest(2, 10);
        var target = new QuietInventory(8);
        var transfer = new InventoryToInventoryTransfer(Api, source, target, null, 0, 0, Eval("*\namount 10+"));

        Assert.Equal(20, transfer.TryMove(Op()).MovedAmount);
    }

    [Fact]
    public void The_cap_is_one_stack_of_the_item()
    {
        var directives = Assert.Single(PaperConditionsParser.Parse("*\namount 1+").Blocks).Directives;
        var stone = new ItemStack(Stone, 1);
        var ingot = new ItemStack(new SameItem { Code = new AssetLocation("game:ingot-copper"), MaxStackSize = 16 }, 1);
        Assert.Equal(64, directives.Reach(500, 1, stone));
        Assert.Equal(16, directives.Reach(500, 1, ingot));
        Assert.Equal(100, directives.Reach(500, 100, stone));   // a floor above the stack holds
        Assert.Equal(30, directives.Reach(30, 1, stone));       // less than a stack all goes
        Assert.Equal(PaperConditionDirectives.DefaultStack, directives.Reach(500, 1, null));
    }

    [Fact]
    public void Ingots_go_sixteen_at_a_time()
    {
        var ingot = new SameItem { Code = new AssetLocation("game:ingot-copper"), MaxStackSize = 16 };
        var source = new QuietInventory(4);
        for (int i = 0; i < 4; i++) source[i].Itemstack = new ItemStack(ingot, 16);
        var target = new QuietInventory(8);
        var transfer = new InventoryToInventoryTransfer(Api, source, target, null, 0, 0, Eval("*\namount 1+"));

        Assert.Equal(16, transfer.TryMove(Op()).MovedAmount);
    }

    [Fact]
    public void For_liquids_the_cap_is_ten_litres()
    {
        var directives = Assert.Single(PaperConditionsParser.Parse("*\namount 1+").Blocks).Directives;
        Assert.Equal(10m, PaperConditionDirectives.AtLeastCapLitres);
        Assert.Equal(10m, directives.ReachLitres(50m, 1m));     // a full barrel is not poured in one go
        Assert.Equal(25m, directives.ReachLitres(50m, 25m));    // a floor above the cap holds
        Assert.Equal(4m, directives.ReachLitres(4m, 1m));       // and less than the cap all goes
        Assert.Equal(1m, Assert.Single(PaperConditionsParser.Parse("*\namount 1").Blocks).Directives.ReachLitres(50m, 1m));
    }

    // ---------------------------------------------------------------- plumbing

    static QuietInventory Chest(int slots, int each)
    {
        var inv = new QuietInventory(slots);
        for (int i = 0; i < slots; i++) inv[i].Itemstack = new ItemStack(Stone, each);
        return inv;
    }

    class SameItem : Item { public override bool Equals(ItemStack a, ItemStack b, params string[] ignored) => ReferenceEquals(a.Collectible, b.Collectible); }
    class QuietInventory : InventoryGeneric { public QuietInventory(int n) : base(n, "test-cap", null, (i, inv) => new QuietSlot(inv)) { } public override void DidModifyItemSlot(ItemSlot s, ItemStack extracted = null) { } }
    class QuietSlot : ItemSlot { public QuietSlot(InventoryBase inv) : base(inv) { } public override void OnItemSlotModified(ItemStack s) { } public override void MarkDirty() { } }
}
