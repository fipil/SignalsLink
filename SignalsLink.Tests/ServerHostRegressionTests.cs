using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using SignalsLink.src.signals.cargo;
using SignalsLink.src.signals.hose;
using SignalsLink.src.signals.manageddock;
using SignalsLink.src.signals.paperConditions;
using SignalsLink.src.signals.yard;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using static SignalsLink.Tests.AnchorLifecycleTests;

namespace SignalsLink.Tests;

public class ServerHostRegressionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Dock_without_output_does_not_materialize_400_yard_inventories(bool actionsBlocked)
    {
        var api = Proxy.Make<ICoreAPI>((m,a) => m.Name == "get_ElapsedMilliseconds" ? 0L : Proxy.Unhandled);
        var dock = new DockProbe { Api = api, Pos = new BlockPos(0,0,0) };
        var own = new CountingHold();
        typeof(BEManagedDock).GetField("ownHolds", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dock, new[] { own });
        var e = new PaperConditionsEvaluator(); e.SetConditionsText("load yard\n\ngame:firewood");
        var section = e.GetSections()[0];
        var yard = new Holder { Holds = Enumerable.Range(0,400).Select(i => (ICargoHold)new CountingHold { Pos = new BlockPos(i,1,0) }).ToArray() };
        var cacheType = typeof(BEManagedDock).GetNestedType("HolderSearch", BindingFlags.NonPublic);
        var cache = (IDictionary)Field(dock, "holders");
        cache[section.Header + " " + string.Join(" ", section.TargetTokens)] = Activator.CreateInstance(cacheType, yard, 0L);
        typeof(BEManagedDock).GetField("unlimited", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dock, true);
        ((DockActionCursor)Field(dock, "actionCursor")).BeginTick(64);
        Call(dock, "RunSection", section, actionsBlocked, false);
        if (actionsBlocked) Assert.Equal(0, own.Reads);
        else Assert.True(own.Reads > 0);
        Assert.All(yard.Holds.Cast<CountingHold>(), h => Assert.Equal(0, h.Reads));
    }

    [Fact]
    public void Fractional_credit_survives_save_reload_and_accepts_old_integer_saves()
    {
        var tree = new TreeAttribute(); tree.SetInt("remaining", 7);
        Assert.Equal(7, LiquidCredit.Read(tree));
        LiquidCredit.Write(tree, 1.9m);
        var bytes = tree.ToBytes(); var restored = new TreeAttribute(); restored.FromBytes(bytes);
        Assert.Equal(1.9m, LiquidCredit.Read(restored));
        LiquidCredit.Write(restored, LiquidCredit.Read(restored) - 1.5m);
        Assert.Equal(0.4m, LiquidCredit.Read(restored));
    }

    [Fact]
    public void Yard_reads_layered_charcoal_and_invalidates_the_snapshot_when_emptied()
    {
        var pos = new BlockPos(0,1,0);
        var layer = new Block { Code = new AssetLocation("game:charcoalpile-3"),
            Attributes = new JsonObject(JObject.Parse("{\"layerGroupCode\":\"height\"}")),
            Variant = new Vintagestory.API.Util.RelaxedReadOnlyDictionary<string,string>(new Dictionary<string,string> { ["height"] = "3" }) };

        var drop = new BlockDropItemStack { Quantity = NatFloat.createUniform(1, 0) };
        drop.ResolvedItemstack = TestStacks.Item("game:charcoal");
        layer.Drops = new[] { drop };
        bool filled = true; int reads = 0;
        var api = Proxy.Make<ICoreAPI>((m,a) =>
        {
            if (m.Name == "GetBlockEntity") return null;
            if (m.Name == "GetBlock") { reads++; return filled && ((BlockPos)a[0]).Equals(pos) ? layer : new Block(); }
            return Proxy.Unhandled;
        });
        var hold = new YardHold(api.World, pos);
        Assert.False(hold.IsEmpty);
        Assert.Equal(3, hold.Inventory[0].StackSize);
        int snapshotReads = reads;
        Assert.Equal(3, hold.Inventory[0].StackSize); Assert.Equal(snapshotReads, reads);
        filled = false; hold.Invalidate();
        Assert.True(hold.IsEmpty); Assert.True(hold.Inventory.Empty);
    }

    [Fact]
    public void Hose_source_last_draws_the_last_liquid_and_reports_its_exact_cost()
    {
        var host = new ContainerProbe();
        host.Inventory[0].Itemstack = TestStacks.Liquid(new Item(), "game:waterportion", 500);
        host.Inventory[1].Itemstack = TestStacks.Liquid(new Item(), "game:saltwaterportion", 500);
        var far = new BlockPos(1,2,3); var hostPos = far.DownCopy();
        var api = Proxy.Make<ICoreAPI>((m,a) =>
        {
            if (m.Name == "GetBlockEntity") return ((BlockPos)a[0]).Equals(hostPos) ? host : null;
            if (m.Name == "GetBlock") return new Block();
            return Proxy.Unhandled;
        });
        var e = new PaperConditionsEvaluator(); e.SetConditionsText("game:saltwaterportion\nsource last\namount 0.1");
        var hose = new HoseLiquidTransfer(api, null, new BlockPos(0,0,0), new signals.src.signalNetwork.NodePos(far,0), e, true);
        var result = hose.TryMove(1);
        Assert.True(result.Transfer.Success); Assert.Equal(0.1m, result.Transfer.CreditCost);
        Assert.Equal(500, host.Inventory[0].StackSize); Assert.Equal(490, host.Inventory[1].StackSize);
    }

    [Fact]
    public void Dock_tries_higher_rule_on_both_holds_before_lower_rule_on_nearest_hold()
    {
        var api = Proxy.Make<ICoreAPI>((m,a) =>
        {
            if (m.Name == "get_ElapsedMilliseconds") return 0L;
            if (m.Name == "GetBlockEntity") return null;
            if (m.Name == "GetBlock") return new Block();
            return Proxy.Unhandled;
        });
        var dock = new DockProbe { Api = api, Pos = new BlockPos(0,0,0) };
        typeof(BEManagedDock).GetField("ownHolds", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dock, new[] { new CountingHold() });
        typeof(BEManagedDock).GetField("remaining", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(dock, 2);
        var whole = new PaperConditionsEvaluator(); whole.SetConditionsText("unload yard\n\ndo seal\n\ndo seal");
        var section = whole.GetSections()[0];
        var e = new PaperConditionsEvaluator(); e.SetConditionsText(section.Text);
        var first = new PositionAction(2); var lower = new PositionAction(1);
        ((List<IConditionAction>)e.GetBlocks()[0].Actions)[0] = first;
        ((List<IConditionAction>)e.GetBlocks()[1].Actions)[0] = lower;
        ((Dictionary<string,PaperConditionsEvaluator>)Field(dock, "sectionEvaluators"))[section.Header] = e;
        var yard = new Holder { Holds = new[] { new CountingHold { Pos = new BlockPos(1,1,0) }, new CountingHold { Pos = new BlockPos(2,1,0) } } };
        var cacheType = typeof(BEManagedDock).GetNestedType("HolderSearch", BindingFlags.NonPublic);
        ((IDictionary)Field(dock, "holders"))[section.Header + " " + string.Join(" ", section.SourceTokens)] = Activator.CreateInstance(cacheType, yard, 0L);
        ((DockActionCursor)Field(dock, "actionCursor")).BeginTick(64);
        var result = (SectionPassResult)Call(dock, "RunSection", section, false, false);
        Assert.True(result.ActionPerformed); Assert.Equal(new[] { 1,2 }, first.Visited); Assert.Empty(lower.Visited);
        Assert.Equal(1, (int)Field(dock, "remaining"));
    }

    class PositionAction : IConditionAction
    {
        private readonly int successfulX;
        public List<int> Visited = new();
        public PositionAction(int successfulX) { this.successfulX = successfulX; }
        public bool Execute(IDictionary<string,object> ctx)
        {
            int x = ((BlockPos)ctx["sourceBlockPos"]).X; Visited.Add(x); return x == successfulX;
        }
    }

    class ContainerProbe : Vintagestory.GameContent.BlockEntityContainer
    {
        private readonly InventoryGeneric inventory = new(2, "test-host", null, (i, inv) => new QuietSlot(inv));
        public override InventoryBase Inventory => inventory;
        public override string InventoryClassName => "test";
        public override void MarkDirty(bool redrawOnClient = false, IPlayer skipPlayer = null) { }
    }
    class QuietSlot : ItemSlot
    {
        public QuietSlot(InventoryBase inv) : base(inv) { }
        public override void MarkDirty() { }
        public override void OnItemSlotModified(ItemStack stack) { }
    }

    class DockProbe : BEManagedDock { public override void MarkDirty(bool redrawOnClient = false, IPlayer skipPlayer = null) { } }
    class Holder : ICargoHolder { public IReadOnlyList<ICargoHold> Holds { get; set; } public bool IsReady => true; }
    class CountingHold : ICargoHold
    {
        public int Reads;
        public string Code => "test";
        public BlockPos Pos { get; set; }
        public IInventory Inventory { get { Reads++; return TestStacks.Inventory(1); } }
        public bool IsEmpty => false;
        public void MarkDirty() { }
    }
}

