using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SignalsLink.src.signals.cargo;
using SignalsLink.src.signals.manageddock;
using SignalsLink.src.signals.managedchute.transporting;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.Tests;

public class ServerTransferRegressionTests
{
    static PaperConditionsEvaluator Eval(string paper) { var e = new PaperConditionsEvaluator(); e.SetConditionsText(paper); return e; }
    static readonly ICoreAPI Api = DispatchProxy.Create<ICoreAPI, NullApi>();
    static ItemStackMoveOperation Op(int n = 1) => new(null, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, n);
    static QuietInventory Items(params int[] counts)
    {
        var inv = new QuietInventory(counts.Length);
        var item = new EqualItem { Code = new AssetLocation("game:firewood"), MaxStackSize = 64 };
        for (int i = 0; i < counts.Length; i++) if (counts[i] > 0) inv[i].Itemstack = new ItemStack(item, counts[i]);
        return inv;
    }

    [Theory]
    [InlineData("", 12)]
    [InlineData("source 1\n", 0)]
    [InlineData("source last\n", 0)]
    public void Batch_gathering_respects_the_source_directive(string directive, int expected)
    {
        var source = Items(4, 8); var target = new QuietInventory(1);
        var transfer = new InventoryToInventoryTransfer(Api, source, target, null, 0, 0,
            Eval("game:firewood\n" + directive + "amount 12\n"));
        Assert.Equal(expected, transfer.TryMove(Op()).MovedAmount);
        Assert.Equal(expected, target[0].StackSize);
        Assert.Equal(12 - expected, source.Sum(s => s.StackSize));
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(15)]
    public void Batch_gathering_respects_the_source_pin(byte pin)
    {
        var source = Items(4, 8); var target = new QuietInventory(1);
        var t = new InventoryToInventoryTransfer(Api, source, target, null, pin, 0, Eval("game:firewood\namount 12"));
        Assert.False(t.TryMove(Op()).Success);
        Assert.Equal(12, source.Sum(s => s.StackSize));
    }

    [Fact]
    public void Physical_failure_falls_through_and_output_below_observes_the_move()
    {
        var source = Items(8); var target = new QuietInventory(1); target[0].MaxSlotStackSize = 3;
        var t = new InventoryToInventoryTransfer(Api, source, target, null, 0, 0,
            Eval("game:firewood\namount 8\n\ngame:firewood\namount 3\n\ngame:firewood 3\noutput 15"));
        var sink = new Sink(); t.OutputSink = sink;
        Assert.Equal(3, t.TryMove(Op()).MovedAmount);
        Assert.Equal(5, source[0].StackSize); Assert.Equal(3, target[0].StackSize); Assert.Equal(15, sink.Value);
    }

    [Fact]
    public void Output_above_the_move_keeps_its_earlier_value()
    {
        var source = Items(8); var target = new QuietInventory(1);
        var t = new InventoryToInventoryTransfer(Api, source, target, null, 0, 0,
            Eval("game:firewood 0\noutput 2\n\ngame:firewood\namount 8\n\ngame:firewood 8\noutput 15"));
        var sink = new Sink(); t.OutputSink = sink;
        Assert.Equal(8, t.TryMove(Op()).MovedAmount); Assert.Equal(2, sink.Value);
    }

    [Fact]
    public void Keep_tops_up_without_waiting_for_the_full_batch()
    {
        var source = Items(3); var target = new QuietInventory(1);
        var t = new InventoryToInventoryTransfer(Api, source, target, null, 0, 0,
            Eval("game:firewood\namount 10\nkeep 3"));
        Assert.Equal(3, t.TryMove(Op()).MovedAmount);
    }

    [Theory]
    [InlineData("keep 2", 20, 2)]
    [InlineData("amount 10+", 20, 20)]
    [InlineData("amount 10", 5, 0)]
    [InlineData("amount 10-", 5, 5)]
    [InlineData("amount 10\nkeep 2", 5, 2)]
    public void Dock_liquid_uses_litres_and_honours_batch_and_level(string rules, int sourceLitres, int expected)
    {
        var source = new QuietInventory(1);
        source[0].Itemstack = TestStacks.Liquid(new ItemLiquidPortion(), "game:waterportion", sourceLitres * 100);
        var target = new InventoryGeneric(1, "test-liquid", null, (i, inv) => new QuietLiquidSlot(inv));
        var t = new InventoryToInventoryTransfer(Api, source, target, null, 0, 0, Eval("game:waterportion\n" + rules)) { CarriesLiquid = true };
        Assert.Equal(expected, t.TryMove(Op(100000)).MovedAmount);
        Assert.Equal(expected * 100, target[0].StackSize);
        Assert.Equal((sourceLitres - expected) * 100, source[0].StackSize);
    }

    [Theory]
    [InlineData(10, 500, true, 0)]
    [InlineData(10, 500, false, 5)]
    [InlineData(2, 500, true, 2)]
    public void Liquid_atomicity_is_checked_before_either_inventory_changes(int litres, int portions, bool atomic, int expected)
    {
        var source = new QuietInventory(1); source[0].Itemstack = TestStacks.Liquid(new EqualItem(), "game:waterportion", portions);
        var target = new InventoryGeneric(1, "test-liquid", null, (i, inv) => new QuietLiquidSlot(inv));
        var r = new LiquidTransferService(Api, target, null).TryMoveFromItemSlot(source[0], target[0], litres, atomic);
        Assert.Equal(expected, r.MovedAmount); Assert.Equal(portions - expected * 100, source[0].StackSize);
        Assert.Equal(expected * 100, target[0].StackSize);
    }

    [Fact]
    public void Fractional_liquid_cost_is_exact_over_repeated_transfers()
    {
        var source = new QuietInventory(1); source[0].Itemstack = TestStacks.Liquid(new EqualItem(), "game:waterportion", 500);
        var target = new InventoryGeneric(1, "test-liquid", null, (i, inv) => new QuietLiquidSlot(inv));
        var service = new LiquidTransferService(Api, target, null);
        decimal credit = 2;
        foreach (decimal n in new[] { 0.1m, 0.1m, 1.5m, 0.3m }) credit -= service.TryMoveFromItemSlot(source[0], target[0], n, true).CreditCost;
        Assert.Equal(0, credit); Assert.Equal(200, target[0].StackSize);
    }

    [Fact]
    public void Standalone_action_closes_the_rail_and_reports_credit_with_empty_source()
    {
        var first = new Spy(); var second = new Spy();
        var e = Eval("do seal\n\ndo seal"); var blocks = (List<ConditionBlock>)e.GetBlocks();
        blocks[0] = Action(first); blocks[1] = Action(second);
        var t = new InventoryToInventoryTransfer(Api, new QuietInventory(1), new QuietInventory(1), null, 0, 0, e);
        var result = t.TryMove(Op());
        Assert.True(result.Success); Assert.True(result.ActionPerformed); Assert.Equal(1, result.CreditCost);
        Assert.Equal(1, first.Runs); Assert.Equal(0, second.Runs);
    }

    [Theory]
    [InlineData(65)] [InlineData(400)]
    public void Dock_resumes_the_search_and_eventually_reaches_distant_target(int count)
    {
        var cursor = new DockActionCursor(); var source = new[] { new Hold() }; var targets = Enumerable.Range(0, count).Select(_ => new Hold()).ToArray();
        var visited = new List<ICargoHold>(); bool moved = false;
        for (int tick = 0; tick < 7 && !moved; tick++)
        {
            cursor.BeginTick(64); int before = visited.Count;
            moved = cursor.TryRun(source, targets, true, (s, t) => { visited.Add(t); return ReferenceEquals(t, targets[^1]); });
            Assert.InRange(visited.Count - before, 0, 64); cursor.EndTick();
        }
        Assert.True(moved); Assert.Equal(count, visited.Count);
    }

    [Fact]
    public void Dock_budget_is_shared_and_lower_rule_waits_for_unfinished_higher_rule()
    {
        var cursor = new DockActionCursor(); var source = new[] { new Hold() }; var targets = Enumerable.Range(0, 65).Select(_ => new Hold()).ToArray();
        int high = 0, low = 0;
        cursor.BeginTick(64);
        Assert.False(cursor.TryRun(source, targets, true, (s,t) => { high++; return false; }));
        Assert.False(cursor.TryRun(source, targets, true, (s,t) => { low++; return true; })); cursor.EndTick();
        Assert.Equal(64, high); Assert.Equal(0, low);
        cursor.BeginTick(64);
        Assert.False(cursor.TryRun(source, targets, true, (s,t) => { high++; return false; }));
        Assert.True(cursor.TryRun(source, targets, true, (s,t) => { low++; return true; })); cursor.EndTick();
        Assert.Equal(65, high); Assert.Equal(1, low);
    }

    [Fact]
    public void Dock_attempts_explicit_actions_even_on_empty_sources()
    {
        var cursor = new DockActionCursor(); cursor.BeginTick(64);
        Assert.True(cursor.TryRun(new[] { new Hold { Empty = true } }, new[] { new Hold() }, false, (s,t) => true));
    }

    [Theory]
    [InlineData("@^(.+)+Z$")]
    [InlineData("! @^(.+)+Z$")]
    public void Pathological_regex_invalidates_the_whole_block_and_does_not_stop_following_output(string regex)
    {
        var e = Eval(regex + "\noutput 15\n\n*\noutput 2");
        var ctx = new Dictionary<string, object> { ["targetInventory"] = Items(1) };
        ((IInventory)ctx["targetInventory"])[0].Itemstack = TestStacks.Item("game:" + new string('a', 120));
        for (int attempt = 0; attempt < 3; attempt++)
        {
            var result = ConditionDriver.Run(e.GetBlocks(), true, b => b.OutputConditionsHold(ctx), null);
            Assert.Equal(2, result.GetOutput());
        }
    }

    [Fact]
    public void Atomic_batch_can_fill_several_target_slots_without_reselecting_the_source()
    {
        var source = Items(4, 8); var target = new QuietInventory(2);
        target[0].MaxSlotStackSize = 6; target[1].MaxSlotStackSize = 6;
        var t = new InventoryToInventoryTransfer(Api, source, target, null, 0, 0, Eval("game:firewood\namount 12"));
        Assert.Equal(12, t.TryMove(Op()).MovedAmount);
        Assert.Equal(6, target[0].StackSize); Assert.Equal(6, target[1].StackSize); Assert.True(source.Empty);
    }

    [Fact]
    public void Attached_action_still_runs_after_the_transfer_consumes_its_source_condition()
    {
        var spy = new Spy(); var e = Eval("game:firewood 8\namount 8\ndo seal");
        var block = e.GetBlocks()[0]; ((List<IConditionAction>)block.Actions)[0] = spy;
        var source = Items(8); var target = new QuietInventory(1);
        var t = new InventoryToInventoryTransfer(Api, source, target, null, 0, 0, e);
        Assert.Equal(8, t.TryMove(Op()).MovedAmount); Assert.Equal(1, spy.Runs);
    }

    [Fact]
    public void Nested_regex_passes_share_budget_without_disabling_a_valid_expression()
    {
        var expression = new CodeRegexCondition(new System.Text.RegularExpressions.Regex("firewood"));
        using (var outer = RegexEvaluationBudget.Begin())
        {
            typeof(RegexEvaluationBudget).GetField("remaining", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(outer, 0L);
            using (RegexEvaluationBudget.Begin())
                Assert.False(expression.Evaluate(TestStacks.Item("game:firewood"), null));
        }
        using (RegexEvaluationBudget.Begin()) Assert.True(expression.Evaluate(TestStacks.Item("game:firewood"), null));
    }

    [Fact]
    public void Timeout_warns_once_and_does_not_disable_later_valid_matches()
    {
        var expression = new CodeRegexCondition(new System.Text.RegularExpressions.Regex("^game:(a+)+$"));
        var logger = DispatchProxy.Create<ILogger, WarningLogger>();
        var api = DispatchProxy.Create<ICoreAPI, WarningApi>();
        ((WarningApi)api).Logger = logger;
        using var diagnostic = RegexDiagnostics.Begin(api, new BlockPos(12, 34, 56), "HoseValve");
        Assert.False(expression.Evaluate(TestStacks.Item("game:" + new string('a', 10000) + "!"), null));
        Assert.True(expression.Evaluate(TestStacks.Item("game:aaa"), null));
        using (var budget = RegexEvaluationBudget.Begin())
        {
            typeof(RegexEvaluationBudget).GetField("remaining", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(budget, 0L);
            Assert.False(expression.Evaluate(TestStacks.Item("game:aaa"), null));
        }
        var messages = ((WarningLogger)logger).Messages;
        Assert.Single(messages);
        Assert.Contains("1000 ms", messages[0]);
        Assert.Contains("HoseValve", messages[0]);
        Assert.Contains("12", messages[0]);
        Assert.Contains("@^game:(a+)+$", messages[0]);
    }

    [Fact]
    public void Budget_warning_is_reported_and_exact_codes_still_work()
    {
        var logger = DispatchProxy.Create<ILogger, WarningLogger>();
        var api = DispatchProxy.Create<ICoreAPI, WarningApi>();
        ((WarningApi)api).Logger = logger;
        using var diagnostic = RegexDiagnostics.Begin(api, new BlockPos(1, 2, 3), "ManagedDock");
        using var budget = RegexEvaluationBudget.Begin();
        typeof(RegexEvaluationBudget).GetField("remaining", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(budget, 0L);
        var stack = TestStacks.Item("game:firewood");
        Assert.False(new CodeRegexCondition(new System.Text.RegularExpressions.Regex("firewood")).Evaluate(stack, null));
        Assert.True(Eval("game:firewood").GetBlocks()[0].TryMatch(stack, null));
        Assert.Contains("5000 ms", Assert.Single(((WarningLogger)logger).Messages));
    }

    public class WarningLogger : DispatchProxy
    {
        public List<string> Messages = new();
        protected override object Invoke(MethodInfo m, object[] args)
        {
            if (m.Name == "Warning") Messages.Add((string)args[0]);
            return m.ReturnType.IsValueType && m.ReturnType != typeof(void) ? Activator.CreateInstance(m.ReturnType) : null;
        }
    }
    public class WarningApi : DispatchProxy
    {
        public ILogger Logger;
        protected override object Invoke(MethodInfo m, object[] args) => m.Name == "get_Logger" ? Logger
            : m.Name == "get_Side" ? EnumAppSide.Server
            : m.ReturnType.IsValueType && m.ReturnType != typeof(void) ? Activator.CreateInstance(m.ReturnType) : null;
    }

    [Fact]
    public void Exhausted_regex_budget_cannot_claim_zero_output_or_create_keep_room()
    {
        var e = Eval("@^game:firewood$ 0\noutput 15");
        var keep = Eval("@^game:firewood$\nkeep 8");
        var target = Items(8);
        var ctx = new Dictionary<string, object> { ["targetInventory"] = target };
        using var scope = RegexEvaluationBudget.Begin();
        typeof(RegexEvaluationBudget).GetField("remaining", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(scope, 0L);
        Assert.False(e.GetBlocks()[0].OutputConditionsHold(ctx));
        Assert.Equal(decimal.MaxValue, keep.GetBlocks()[0].CountInTarget(target, ctx));
    }

    [Theory]
    [InlineData("game:*wood", "game:firewood", true)]
    [InlineData("game:?irewood", "game:firewood", true)]
    [InlineData("*a*a*a*a*a*a*a*a*b", "game:aaaaaaaaaaaaaaaaaaaa", false)]
    [InlineData("game:firewood", "game:firewood-long", false)]
    public void Glob_matching_is_anchored_and_handles_repeated_stars(string pattern, string code, bool expected)
        => Assert.Equal(expected, new CodeGlobCondition(pattern).Evaluate(TestStacks.Item(code), null));

    static ConditionBlock Action(Spy action) => new(new List<ScopedCondition>(), 0, false, PaperConditionDirectives.Empty, new List<IConditionAction> { action });
    sealed class Sink : IConditionOutputSink { public byte Value; public void ApplyOutput(int pin, byte value) => Value = value; }
    sealed class Spy : IConditionAction { public int Runs; public bool Execute(IDictionary<string, object> ctx) { Runs++; return true; } }
    public class NullApi : DispatchProxy { protected override object Invoke(MethodInfo m, object[] args) => m.ReturnType.IsValueType ? Activator.CreateInstance(m.ReturnType) : null; }
    class EqualItem : Item { public override bool Equals(ItemStack a, ItemStack b, params string[] ignored) => ReferenceEquals(a.Collectible, b.Collectible); }
    class ItemLiquidPortion : EqualItem { }
    class QuietInventory : InventoryGeneric { public QuietInventory(int n) : base(n, "test-quiet", null, (i, inv) => new QuietSlot(inv)) { } public override void DidModifyItemSlot(ItemSlot s, ItemStack extracted = null) { } }
    class QuietSlot : ItemSlot { public QuietSlot(InventoryBase inv) : base(inv) { } public override void OnItemSlotModified(ItemStack s) { } public override void MarkDirty() { } }
    class QuietLiquidSlot : ItemSlotGoodsOrLiquid { public QuietLiquidSlot(InventoryBase inv) : base(inv) { } public override void OnItemSlotModified(ItemStack s) { } public override void MarkDirty() { } }
    class Hold : ICargoHold { public bool Empty; public string Code => "test"; public IInventory Inventory => throw new Exception("The scheduler must not materialize inventories"); public BlockPos Pos => null; public bool IsEmpty => Empty; public void MarkDirty() { } }
}
