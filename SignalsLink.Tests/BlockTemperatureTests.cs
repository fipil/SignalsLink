using System.Collections.Generic;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using static SignalsLink.Tests.AnchorLifecycleTests;

namespace SignalsLink.Tests;

/// <summary>
/// `blockTemperature>N` asks the block in the scope how hot it is, the way `isBurning` asks
/// whether it burns. A firepit between two logs is not burning for a moment but is still hot;
/// this is how a paper tells the two apart.
/// </summary>
public class BlockTemperatureTests
{
    [Fact]
    public void It_parses_with_every_comparison_and_the_negation()
    {
        foreach (string line in new[] { "blockTemperature>300", "blockTemperature >= 300", "blocktemperature<200", "blockTemperature<=200", "blockTemperature=20", "!blockTemperature>300" })
        {
            var errors = new List<PaperConditionError>();
            Assert.Single(PaperConditionsParser.Parse("game:firewood\nin target\n" + line + "\n", errors).Blocks);
            Assert.Empty(errors);
        }
    }

    [Fact]
    public void The_bare_word_is_a_paper_error()
    {
        var errors = new List<PaperConditionError>();
        PaperConditionsParser.Parse("game:firewood\nblockTemperature\n", errors);
        Assert.Equal("condition", Assert.Single(errors).Reason);
    }

    [Theory]
    [InlineData(">", 300, 450, true)]
    [InlineData(">", 300, 150, false)]
    [InlineData("<", 200, 150, true)]
    [InlineData("<=", 150, 150, true)]
    [InlineData("=", 20, 20, true)]
    public void It_compares_the_blocks_own_temperature(string op, double value, float actual, bool expected)
    {
        var condition = new BlockTemperatureCondition(op, value);
        Assert.Equal(expected, condition.Evaluate(null, null, Context(new Firepit { furnaceTemperature = actual }), InventoryConditionScope.Target, false));
    }

    [Fact]
    public void Negation_flips_the_answer_but_a_block_without_heat_is_never_true()
    {
        var hot = Context(new Firepit { furnaceTemperature = 450 });
        Assert.False(new BlockTemperatureCondition(">", 300, negate: true).Evaluate(null, null, hot, InventoryConditionScope.Target, false));
        Assert.True(new BlockTemperatureCondition("<", 300, negate: true).Evaluate(null, null, hot, InventoryConditionScope.Target, false));

        var chest = Context(new Chest());
        Assert.False(new BlockTemperatureCondition(">", 300).Evaluate(null, null, chest, InventoryConditionScope.Target, false));
        Assert.False(new BlockTemperatureCondition(">", 300, negate: true).Evaluate(null, null, chest, InventoryConditionScope.Target, false));
    }

    [Fact]
    public void A_property_named_GenTemp_counts_too()
    {
        Assert.True(new BlockTemperatureCondition(">", 100).Evaluate(null, null, Context(new Generator()), InventoryConditionScope.Target, false));
    }

    [Fact]
    public void Source_scope_reads_the_source_position()
    {
        var ctx = Context(new Firepit { furnaceTemperature = 450 });
        ctx["sourceBlockPos"] = ctx["targetBlockPos"]; ctx.Remove("targetBlockPos");
        Assert.True(new BlockTemperatureCondition(">", 300).Evaluate(null, null, ctx, InventoryConditionScope.Source, false));
        Assert.False(new BlockTemperatureCondition(">", 300).Evaluate(null, null, ctx, InventoryConditionScope.Target, false));
    }

    // ---------------------------------------------------------------- plumbing

    class Firepit : BlockEntity { public float furnaceTemperature; }
    class Generator : BlockEntity { public float GenTemp => 250; }
    class Chest : BlockEntity { }

    static Dictionary<string, object> Context(BlockEntity be)
    {
        var accessor = Proxy.Make<IBlockAccessor>((m, a) => m.Name == "GetBlockEntity" ? be : Proxy.Unhandled);
        var world = Proxy.Make<IWorldAccessor>((m, a) => m.Name == "get_BlockAccessor" ? accessor : Proxy.Unhandled);
        return new Dictionary<string, object> { ["world"] = world, ["targetBlockPos"] = new BlockPos(1, 2, 3, 0) };
    }
}
