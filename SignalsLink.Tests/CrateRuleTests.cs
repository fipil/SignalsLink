using System.Linq;
using System.Reflection;
using SignalsLink.src.signals.managedchute.transporting;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.Tests;

/// <summary>
/// A crate takes one kind of item. Vanilla enforces that only for the player's hand and the
/// chute, so a device that asks the slots alone would happily mix one - and the player then gets
/// only the first kind back out by hand. The device asks the crate first, and only when putting in.
/// </summary>
public class CrateRuleTests
{
    static readonly ICoreAPI Api = DispatchProxy.Create<ICoreAPI, ServerTransferRegressionTests.NullApi>();
    static PaperConditionsEvaluator Eval(string paper) { var e = new PaperConditionsEvaluator(); e.SetConditionsText(paper); return e; }
    static ItemStackMoveOperation Op(int n = 1) => new(null, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, n);

    static readonly Item Firewood = new SameItem { Code = new AssetLocation("game:firewood"), MaxStackSize = 64 };
    static readonly Item Sticks = new SameItem { Code = new AssetLocation("game:stick"), MaxStackSize = 64 };

    [Fact]
    public void An_empty_crate_takes_anything()
    {
        Assert.True(CrateRule.Accepts(Crate(4), new ItemStack(Firewood, 1)));
    }

    [Fact]
    public void A_crate_takes_more_of_what_it_holds_and_nothing_else()
    {
        var crate = Crate(4, Firewood, 10);
        Assert.True(CrateRule.Accepts(crate, new ItemStack(Firewood, 1)));
        Assert.False(CrateRule.Accepts(crate, new ItemStack(Sticks, 1)));
    }

    [Fact]
    public void A_full_crate_refuses_even_its_own_kind()
    {
        var crate = Crate(1, Firewood, 64);
        Assert.False(CrateRule.Accepts(crate, new ItemStack(Firewood, 1)));
    }

    [Fact]
    public void Anything_that_is_not_an_inventory_base_is_left_alone()
    {
        Assert.True(CrateRule.Accepts(null, new ItemStack(Firewood, 1)));
        Assert.True(CrateRule.Accepts(Crate(1, Sticks, 1), null));
    }

    [Fact]
    public void A_device_does_not_mix_a_crate()
    {
        var source = Chest(Firewood, 8);
        var crate = Crate(4, Sticks, 5);
        var transfer = new InventoryToInventoryTransfer(Api, source, crate, null, 0, 0, Eval("*\namount 8")) { TargetIsCrate = true };

        Assert.False(transfer.TryMove(Op()).Success);
        Assert.Equal(8, source.Sum(s => s.StackSize));
        Assert.Equal(5, crate.Sum(s => s.StackSize));
    }

    [Fact]
    public void A_device_fills_a_crate_with_more_of_the_same()
    {
        var source = Chest(Firewood, 8);
        var crate = Crate(4, Firewood, 5);
        var transfer = new InventoryToInventoryTransfer(Api, source, crate, null, 0, 0, Eval("*\namount 8")) { TargetIsCrate = true };

        Assert.Equal(8, transfer.TryMove(Op()).MovedAmount);
        Assert.Equal(13, crate.Sum(s => s.StackSize));
    }

    [Fact]
    public void Only_a_crate_is_asked()
    {
        // The same inventory not known to be a crate: the slots decide, as everywhere else.
        var source = Chest(Firewood, 8);
        var crate = Crate(4, Sticks, 5);
        var transfer = new InventoryToInventoryTransfer(Api, source, crate, null, 0, 0, Eval("*\namount 8")) { TargetIsCrate = false };

        Assert.Equal(8, transfer.TryMove(Op()).MovedAmount);
    }

    [Fact]
    public void Taking_out_of_a_crate_is_never_gated()
    {
        // A crate that would refuse everything coming in still gives everything out.
        var crate = Crate(4, Firewood, 8);
        crate.OnGetAutoPushIntoSlot = (face, slot) => null;
        var target = new QuietInventory(2);
        var transfer = new InventoryToInventoryTransfer(Api, crate, target, null, 0, 0, Eval("*\namount 8"));

        Assert.Equal(8, transfer.TryMove(Op()).MovedAmount);
    }

    // ---------------------------------------------------------------- plumbing

    static QuietInventory Chest(Item item, int count)
    {
        var inv = new QuietInventory(2);
        inv[0].Itemstack = new ItemStack(item, count);
        return inv;
    }

    /// <summary>The vanilla crate rule, as BlockEntityCrate wires it: first kind in decides.</summary>
    static QuietInventory Crate(int slots, Item item = null, int count = 0)
    {
        var inv = new QuietInventory(slots);
        if (item != null) inv[0].Itemstack = new ItemStack(item, count);
        inv.OnGetAutoPushIntoSlot = (face, fromSlot) =>
        {
            ItemSlot first = inv.FirstNonEmptySlot;
            if (first == null) return inv[0];
            if (!ReferenceEquals(first.Itemstack.Collectible, fromSlot.Itemstack.Collectible)) return null;
            return inv.FirstOrDefault(s => s.Itemstack == null || s.StackSize < s.Itemstack.Collectible.MaxStackSize);
        };
        return inv;
    }

    class SameItem : Item { public override bool Equals(ItemStack a, ItemStack b, params string[] ignored) => ReferenceEquals(a.Collectible, b.Collectible); }
    class QuietInventory : InventoryGeneric { public QuietInventory(int n) : base(n, "test-crate", null, (i, inv) => new QuietSlot(inv)) { } public override void DidModifyItemSlot(ItemSlot s, ItemStack extracted = null) { } }
    class QuietSlot : ItemSlot { public QuietSlot(InventoryBase inv) : base(inv) { } public override void OnItemSlotModified(ItemStack s) { } public override void MarkDirty() { } }
}
