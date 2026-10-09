using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SignalsLink.src.signals.managedchute.transporting;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;

namespace SignalsLink.Tests
{
    /// <summary>
    /// `recipe game:paper-parchment N`: the block lays a grid recipe out in the first nine slots of
    /// the target, out of the source, by difference - what already lies in the right cell stays.
    /// Planned dry first; a layout that cannot be completed moves nothing at all.
    /// </summary>
    public class RecipeFillTests
    {
        private static readonly ICoreAPI Api = DispatchProxy.Create<ICoreAPI, ServerTransferRegressionTests.NullApi>();

        // ------------------------------------------------------------------ reading it

        [Fact]
        public void The_directive_is_read_with_its_count_and_mark()
        {
            PaperConditionDirectives plain = Directives("recipe game:paper-parchment\n");
            Assert.True(plain.HasRecipe);
            Assert.Equal(new AssetLocation("game:paper-parchment"), plain.RecipeCode);
            Assert.Equal(1, plain.RecipeCount);
            Assert.Equal(AmountMode.Exactly, plain.RecipeMode);

            PaperConditionDirectives more = Directives("recipe paper-parchment 2+\n");
            Assert.Equal("game", more.RecipeCode.Domain);
            Assert.Equal(2, more.RecipeCount);
            Assert.Equal(AmountMode.AtLeast, more.RecipeMode);

            Assert.Equal(AmountMode.AtMost, Directives("recipe signals:el_wire 3-\n").RecipeMode);
        }

        [Theory]
        [InlineData("recipe")]
        [InlineData("recipe ")]
        [InlineData("recipe game:paper-parchment 0")]
        [InlineData("recipe game:paper-parchment x")]
        [InlineData("recipe game:paper-parchment 2 3")]
        [InlineData("recipe game:paper-* 1")]
        public void A_recipe_line_that_cannot_be_read_is_a_paper_error(string line)
        {
            Assert.Contains(Errors(line + "\n"), e => e.Reason == "recipe");
        }

        [Fact]
        public void A_recipe_block_stands_alone_without_other_directives()
        {
            IReadOnlyList<PaperConditionError> errors = Errors("recipe game:paper-parchment\namount 3\n");

            Assert.Contains(errors, e => e.Reason == "recipealone");
            Assert.DoesNotContain(PaperConditionsParser.Parse("recipe game:paper-parchment\namount 3\n").Blocks, b => b.Directives.HasRecipe);
        }

        [Fact]
        public void A_recipe_block_needs_no_selector_and_may_carry_conditions()
        {
            CompiledConditions compiled = PaperConditionsParser.Parse("recipe game:paper-parchment 2\n");
            Assert.True(Assert.Single(compiled.Blocks).Directives.HasRecipe);

            var chute = new Host();
            Assert.DoesNotContain(BlockBehaviorPaperConditions.FindErrors("recipe game:paper-parchment\n", chute), e => e.Reason == "noselector");
            Assert.DoesNotContain(BlockBehaviorPaperConditions.FindErrors("in target\n!game:stick\nrecipe game:paper-parchment\n", chute), e => e.Reason == "noselector");
        }

        // ------------------------------------------------------------------ laying it out

        // S S
        // P .
        private static GridRecipe Pin() => Recipe(2, 2, "pin", Ing("stick"), Ing("stick"), Ing("plank"), null);

        [Fact]
        public void The_recipe_is_laid_out_top_left_in_the_grid()
        {
            var source = Quiet(4, Stack("stick", 10), Stack("plank", 10));
            var target = Quiet(10);

            int moved = Fill("recipe pin\n", source, target, Pin());

            Assert.Equal(3, moved);
            Assert.Equal(new[] { "stick:1", "stick:1", "", "plank:1", "", "", "", "", "", "" }, Picture(target));
            Assert.Equal(8, source[0].StackSize);
            Assert.Equal(9, source[1].StackSize);
        }

        [Fact]
        public void A_count_lays_out_that_many_multiples()
        {
            var source = Quiet(4, Stack("stick", 10), Stack("plank", 10));
            var target = Quiet(9);

            Assert.Equal(9, Fill("recipe pin 3\n", source, target, Pin()));
            Assert.Equal(new[] { "stick:3", "stick:3", "", "plank:3", "", "", "", "", "" }, Picture(target));
        }

        [Fact]
        public void An_exact_count_the_source_cannot_cover_moves_nothing()
        {
            var source = Quiet(4, Stack("stick", 3), Stack("plank", 10));
            var target = Quiet(9);

            Assert.Equal(0, Fill("recipe pin 2\n", source, target, Pin()));   // 2 multiples want 4 sticks
            Assert.All(Picture(target), cell => Assert.Equal("", cell));
            Assert.Equal(3, source[0].StackSize);
        }

        [Fact]
        public void At_most_takes_what_the_source_allows()
        {
            var source = Quiet(4, Stack("stick", 3), Stack("plank", 10));
            var target = Quiet(9);

            Assert.Equal(3, Fill("recipe pin 5-\n", source, target, Pin()));   // one multiple: three sticks make one pair plus one
            Assert.Equal(new[] { "stick:1", "stick:1", "", "plank:1", "", "", "", "", "" }, Picture(target));
        }

        [Fact]
        public void At_least_reaches_for_as_many_multiples_as_fit_and_waits_below_the_floor()
        {
            var source = Quiet(4, Stack("stick", 64), Stack("stick", 64), Stack("plank", 70));
            var target = Quiet(9);

            Assert.Equal(64 * 3, Fill("recipe pin 2+\n", source, target, Pin()));   // a cell holds 64 at most
            Assert.Equal(new[] { "stick:64", "stick:64", "", "plank:64", "", "", "", "", "" }, Picture(target));

            var poor = Quiet(4, Stack("stick", 2), Stack("plank", 1));
            Assert.Equal(0, Fill("recipe pin 2+\n", poor, Quiet(9), Pin()));
        }

        [Fact]
        public void What_already_lies_in_the_right_cell_is_not_carried_again()
        {
            var source = Quiet(4, Stack("stick", 10), Stack("plank", 10));
            var target = Quiet(9, Stack("stick", 1), Stack("stick", 2));

            Assert.Equal(1 + 0 + 2, Fill("recipe pin 2\n", source, target, Pin()));
            Assert.Equal(new[] { "stick:2", "stick:2", "", "plank:2", "", "", "", "", "" }, Picture(target));
        }

        [Fact]
        public void A_tool_left_in_the_grid_is_reused()
        {
            // H S  (hammer is a tool: one per layout, whatever the multiple)
            GridRecipe recipe = Recipe(2, 1, "nail", Ing("hammer", tool: true), Ing("stick"));
            var source = Quiet(4, Stack("hammer", 1), Stack("stick", 10));
            var target = Quiet(9, Stack("hammer", 1));

            Assert.Equal(3, Fill("recipe nail 3\n", source, target, recipe));
            Assert.Equal(new[] { "hammer:1", "stick:3", "", "", "", "", "", "", "" }, Picture(target));
            Assert.Equal(1, source[0].StackSize);   // the spare hammer stays in the chest
        }

        [Fact]
        public void Something_foreign_in_the_grid_stops_the_block()
        {
            var source = Quiet(4, Stack("stick", 10), Stack("plank", 10));
            var target = Quiet(9);
            target[8].Itemstack = Stack("dirt", 1);

            Assert.Equal(0, Fill("recipe pin\n", source, target, Pin()));

            var wrong = Quiet(9, Stack("plank", 1));   // a plank where a stick belongs
            Assert.Equal(0, Fill("recipe pin\n", source, wrong, Pin()));
        }

        [Fact]
        public void A_target_without_a_grid_cannot_take_a_recipe()
        {
            var source = Quiet(4, Stack("stick", 10), Stack("plank", 10));

            Assert.Equal(0, Fill("recipe pin\n", source, Quiet(4), Pin()));
        }

        [Fact]
        public void An_unknown_recipe_moves_nothing()
        {
            var source = Quiet(4, Stack("stick", 10), Stack("plank", 10));

            Assert.Equal(0, Fill("recipe game:nothing\n", source, Quiet(9), Pin()));
        }

        [Fact]
        public void Named_wildcards_resolve_to_one_material_the_source_holds_in_full()
        {
            // W W  (any planks, both the same wood)
            GridRecipe recipe = Recipe(2, 1, "board", Wild("planks-*", "wood"), Wild("planks-*", "wood"));
            var source = Quiet(4, Stack("planks-oak", 1), Stack("planks-birch", 2));
            var target = Quiet(9);

            Assert.Equal(2, Fill("recipe board\n", source, target, recipe));
            Assert.Equal(new[] { "planks-birch:1", "planks-birch:1", "", "", "", "", "", "", "" }, Picture(target));

            var mixed = Quiet(4, Stack("planks-oak", 1), Stack("planks-birch", 1));
            Assert.Equal(0, Fill("recipe board\n", mixed, Quiet(9), recipe));
        }

        [Fact]
        public void A_shapeless_recipe_is_laid_out_in_reading_order()
        {
            GridRecipe recipe = Recipe(3, 1, "mix", Ing("plank"), Ing("stick"), null);
            recipe.Shapeless = true;
            var source = Quiet(4, Stack("stick", 5), Stack("plank", 5));
            var target = Quiet(9);

            Assert.Equal(2, Fill("recipe mix\n", source, target, recipe));
            Assert.Equal(new[] { "plank:1", "stick:1", "", "", "", "", "", "", "" }, Picture(target));
        }

        [Fact]
        public void The_blocks_conditions_gate_it_like_any_other_block()
        {
            var source = Quiet(4, Stack("stick", 10), Stack("plank", 10));
            var target = Quiet(9);

            Assert.Equal(0, Fill("in target\ngame:dirt\nrecipe pin\n", source, target, Pin()));
            Assert.Equal(3, Fill("in target\n!game:dirt\nrecipe pin\n", source, target, Pin()));
        }

        [Fact]
        public void The_second_recipe_for_the_same_output_is_used_when_the_first_cannot_be()
        {
            GridRecipe fromSticks = Recipe(2, 1, "pin", Ing("stick"), Ing("stick"));
            GridRecipe fromPlanks = Recipe(2, 1, "pin", Ing("plank"), Ing("plank"));
            var source = Quiet(4, Stack("plank", 10));
            var target = Quiet(9);

            Assert.Equal(2, Fill("recipe pin\n", source, target, fromSticks, fromPlanks));
            Assert.Equal(new[] { "plank:1", "plank:1", "", "", "", "", "", "", "" }, Picture(target));
        }

        // ------------------------------------------------------------------ helpers

        private static int Fill(string paper, IInventory source, IInventory target, params GridRecipe[] recipes)
        {
            var transfer = new InventoryToInventoryTransfer(Api, source, target, null, 0, 0, Eval(paper))
            {
                RecipeBook = new Book(recipes),
                TargetIsCrate = false,
            };
            var op = new ItemStackMoveOperation(null, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, 1);
            return (int)transfer.TryMove(op).MovedAmount;
        }

        private static string[] Picture(IInventory inventory)
            => Enumerable.Range(0, inventory.Count).Select(i => inventory[i].Empty ? "" : inventory[i].Itemstack.Collectible.Code.Path + ":" + inventory[i].StackSize).ToArray();

        // Hand-built items: the game's Satisfies and Equals walk attribute trees through the item's
        // api, which these do not have, so they answer by code. One instance per code, like the registry.
        private sealed class CodeItem : Item
        {
            public override bool Satisfies(ItemStack thisStack, ItemStack otherStack)
                => otherStack?.Collectible?.Code != null && thisStack.Collectible.Code.Equals(otherStack.Collectible.Code);
            public override bool Equals(ItemStack thisStack, ItemStack otherStack, params string[] ignoreAttributeSubTrees)
                => otherStack?.Collectible?.Code != null && thisStack.Collectible.Code.Equals(otherStack.Collectible.Code);
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, CodeItem> items = new();

        private static ItemStack Stack(string code, int size = 1)
        {
            CodeItem item = items.GetOrAdd(code, c => new CodeItem { Code = new AssetLocation(c), MaxStackSize = 64 });
            return new ItemStack(item, size) { Attributes = new Vintagestory.API.Datastructures.TreeAttribute() };
        }

        // Inventories whose slots do not report into a world that is not there.
        private sealed class QuietInventory : InventoryGeneric
        {
            public QuietInventory(int n) : base(n, "test-recipe", null, (i, inv) => new QuietSlot(inv)) { }
            public override void DidModifyItemSlot(ItemSlot s, ItemStack extracted = null) { }
        }
        private sealed class QuietSlot : ItemSlot
        {
            public QuietSlot(InventoryBase inv) : base(inv) { }
            public override void OnItemSlotModified(ItemStack s) { }
            public override void MarkDirty() { }
        }

        private static QuietInventory Quiet(int slots, params ItemStack[] stacks)
        {
            var inv = new QuietInventory(slots);
            for (int i = 0; i < stacks.Length && i < slots; i++) inv[i].Itemstack = stacks[i];
            return inv;
        }

        private static CraftingRecipeIngredient Ing(string code, int quantity = 1, bool tool = false)
            => new CraftingRecipeIngredient { Type = EnumItemClass.Item, Code = new AssetLocation(code), StackSize = quantity, IsTool = tool,
                ResolvedItemStack = Stack(code) };

        // The game's wildcard ingredient also checks tags through the item's api; here only the code pattern counts.
        private sealed class WildIngredient : CraftingRecipeIngredient
        {
            public override bool SatisfiesAsIngredient(ItemStack inputStack, bool checkStackSize = true)
                => inputStack?.Collectible?.Code != null && Vintagestory.API.Util.WildcardUtil.Match(Code, inputStack.Collectible.Code, AllowedVariants);
        }

        private static CraftingRecipeIngredient Wild(string pattern, string name)
            => new WildIngredient { Type = EnumItemClass.Item, Code = new AssetLocation(pattern), Name = name, MatchingType = EnumRecipeMatchType.Wildcard };

        private static GridRecipe Recipe(int width, int height, string output, params CraftingRecipeIngredient[] cells)
            => new GridRecipe { Width = width, Height = height, ResolvedIngredients = cells, Output = Ing(output), Name = new AssetLocation(output) };

        private static PaperConditionsEvaluator Eval(string paper)
        {
            var evaluator = new PaperConditionsEvaluator();
            evaluator.SetConditionsText(paper);
            return evaluator;
        }

        private static PaperConditionDirectives Directives(string paper)
            => Assert.Single(PaperConditionsParser.Parse(paper).Blocks).Directives;

        private static IReadOnlyList<PaperConditionError> Errors(string paper)
        {
            var errors = new List<PaperConditionError>();
            PaperConditionsParser.Parse(paper, errors);
            return errors;
        }

        private sealed class Book : IGridRecipeBook
        {
            private readonly GridRecipe[] recipes;
            public Book(GridRecipe[] recipes) { this.recipes = recipes; }
            public IEnumerable<GridRecipe> ForOutput(AssetLocation code)
                => recipes.Where(r => r.Output.ResolvedItemStack.Collectible.Code.Equals(code));
        }

        private sealed class Host : IPaperConditionsHost
        {
            public string ConditionsText { get; set; }
            public int SignalInputsCount => 4;
            public bool SupportsGates => true;
        }
    }
}
