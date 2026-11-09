using SignalsLink.src.signals.managedchute.transporting;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>
    /// The boiler keeps its firepit as a stage number. `target firepit` walks that number the
    /// way a player's clicks do: tinder, four logs, then logs as fuel while there is little left.
    /// </summary>
    public class BoilerFirepitTests
    {
        [Fact]
        public void Tinder_only_starts_a_bare_boiler()
        {
            Assert.True(BoilerFirepit.Step(0, 0f, firewood: false, out int stage, out float fuel));
            Assert.Equal(1, stage);
            Assert.Equal(0f, fuel);

            Assert.False(BoilerFirepit.Step(1, 0f, firewood: false, out _, out _));
            Assert.False(BoilerFirepit.Step(5, 0f, firewood: false, out _, out _));
        }

        [Fact]
        public void Four_logs_build_the_firepit_and_each_adds_two_hours()
        {
            int stage = 1; float fuel = 0f;
            for (int i = 0; i < 4; i++)
            {
                Assert.True(BoilerFirepit.Step(stage, fuel, firewood: true, out stage, out fuel));
            }

            Assert.Equal(BoilerFirepit.Cold, stage);
            Assert.Equal(8f, fuel);
        }

        [Fact]
        public void A_log_on_bare_ground_is_refused()
        {
            Assert.False(BoilerFirepit.Step(0, 0f, firewood: true, out _, out _));
        }

        [Fact]
        public void Fuel_is_added_only_while_there_is_little_of_it()
        {
            Assert.True(BoilerFirepit.Step(BoilerFirepit.Lit, 6f, firewood: true, out int stage, out float fuel));
            Assert.Equal(BoilerFirepit.Lit, stage);
            Assert.Equal(8f, fuel);

            Assert.False(BoilerFirepit.Step(BoilerFirepit.Lit, 6.5f, firewood: true, out _, out _));
            Assert.False(BoilerFirepit.WantsSomething(BoilerFirepit.Cold, 7f));
        }

        [Fact]
        public void A_burnt_out_firepit_goes_back_to_cold_with_fresh_fuel()
        {
            Assert.True(BoilerFirepit.Step(BoilerFirepit.Extinct, -2f, firewood: true, out int stage, out float fuel));
            Assert.Equal(BoilerFirepit.Cold, stage);
            Assert.Equal(2f, fuel);
        }

        [Fact]
        public void Matching_goes_by_the_item_class_the_boiler_itself_checks()
        {
            ItemStack grass = new ItemStack(new ItemDryGrass { Code = new AssetLocation("game:drygrass") });
            ItemStack log = new ItemStack(new ItemFirewood { Code = new AssetLocation("game:firewood") });
            ItemStack stick = TestStacks.Item("game:stick");

            Assert.True(BoilerFirepit.Matches(0, 0f, grass));
            Assert.False(BoilerFirepit.Matches(0, 0f, log));
            Assert.True(BoilerFirepit.Matches(2, 2f, log));
            Assert.False(BoilerFirepit.Matches(2, 2f, grass));
            Assert.False(BoilerFirepit.Matches(2, 2f, stick));
            Assert.False(BoilerFirepit.Matches(2, 2f, null));
        }

        [Fact]
        public void The_firepit_directive_reads_the_boiler_instead_of_the_block()
        {
            BlockEntityBoiler boiler = new BlockEntityBoiler { firepitStage = 3, fuelHours = 4f };
            IWorldAccessor world = World(boiler);
            BlockPos pos = new BlockPos(1, 2, 3, 0);

            Assert.True(FirepitConstruction.CanBuildAt(world, pos));
            Assert.True(FirepitConstruction.MatchesAt(world, pos, new ItemStack(new ItemFirewood { Code = new AssetLocation("game:firewood") })));
            Assert.False(FirepitConstruction.MatchesAt(world, pos, new ItemStack(new ItemDryGrass { Code = new AssetLocation("game:drygrass") })));

            boiler.firepitStage = BoilerFirepit.Lit;
            boiler.fuelHours = 9f;
            Assert.False(FirepitConstruction.CanBuildAt(world, pos));
        }

        [Fact]
        public void A_boiler_is_ready_when_cold_or_lit_and_not_when_burnt_out()
        {
            BlockEntityBoiler boiler = new BlockEntityBoiler { firepitStage = 4 };
            IWorldAccessor world = World(boiler);
            BlockPos pos = new BlockPos(1, 2, 3, 0);

            Assert.False(FirepitConstruction.IsReadyAt(world, pos));

            boiler.firepitStage = BoilerFirepit.Cold;
            Assert.True(FirepitConstruction.IsReadyAt(world, pos));

            boiler.firepitStage = BoilerFirepit.Lit;
            Assert.True(FirepitConstruction.IsReadyAt(world, pos));

            // Burnt out: a log has to come first, so the igniter gets its edge only afterwards.
            boiler.firepitStage = BoilerFirepit.Extinct;
            Assert.False(FirepitConstruction.IsReadyAt(world, pos));
        }

        [Fact]
        public void A_firepit_block_is_ready_when_it_holds_fuel()
        {
            BlockEntityFirepit firepit = new BlockEntityFirepit();
            IWorldAccessor world = World(firepit);
            BlockPos pos = new BlockPos(0, 0, 0, 0);

            Assert.False(FirepitConstruction.IsReadyAt(world, pos));

            firepit.fuelSlot.Itemstack = TestStacks.Item("game:firewood", 4);
            Assert.True(FirepitConstruction.IsReadyAt(world, pos));

            Assert.False(FirepitConstruction.IsReadyAt(World((BlockEntity)null), pos));
        }

        [Fact]
        public void The_paper_word_reads_the_target_end()
        {
            var ctx = new System.Collections.Generic.Dictionary<string, object>
            {
                ["world"] = World(new BlockEntityBoiler { firepitStage = BoilerFirepit.Cold }),
                ["targetBlockPos"] = new BlockPos(1, 2, 3, 0),
            };

            Assert.True(new SignalsLink.src.signals.paperConditions.FirepitReadyCondition(true).Evaluate(null, null, ctx, SignalsLink.src.signals.paperConditions.InventoryConditionScope.Target, false));
            Assert.False(new SignalsLink.src.signals.paperConditions.FirepitReadyCondition(false).Evaluate(null, null, ctx, SignalsLink.src.signals.paperConditions.InventoryConditionScope.Target, false));
            Assert.False(new SignalsLink.src.signals.paperConditions.FirepitReadyCondition(true).Evaluate(null, null, ctx, SignalsLink.src.signals.paperConditions.InventoryConditionScope.Source, false));
        }

        private static IWorldAccessor World(BlockEntity be)
        {
            var accessor = AnchorLifecycleTests.Proxy.Make<IBlockAccessor>((m, a) => m.Name == "GetBlockEntity" ? be : AnchorLifecycleTests.Proxy.Unhandled);
            return AnchorLifecycleTests.Proxy.Make<IWorldAccessor>((m, a) => m.Name == "get_BlockAccessor" ? accessor : AnchorLifecycleTests.Proxy.Unhandled);
        }
    }
}
