using System.Reflection;
using SignalsMachines.src.craftingmachine;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace SignalsMachines.Tests;

/// <summary>Recipes read and made without a player, on hand-built recipes.</summary>
public class MachineRecipesTests
{
    private static readonly Item Stick = new() { Code = new AssetLocation("game:stick"), MaxStackSize = 64, ItemId = 1 };
    private static readonly Item Flint = new() { Code = new AssetLocation("game:flint"), MaxStackSize = 64, ItemId = 2 };
    private static readonly Item Knife = new() { Code = new AssetLocation("game:knife"), MaxStackSize = 1, ItemId = 3 };
    private static readonly Item Bowl = new() { Code = new AssetLocation("game:bowl"), MaxStackSize = 16, ItemId = 4 };

    private static CraftingRecipeIngredient Ingredient(Item item, int quantity = 1) => new()
    {
        Type = EnumItemClass.Item, Code = item.Code, Quantity = quantity, ResolvedItemStack = new ItemStack(item, quantity), MatchingType = EnumRecipeMatchType.Exact
    };

    /// <summary>"FS" horizontally: flint over... no, flint left of stick, makes 2 arrows (sticks stand in).</summary>
    private static GridRecipe Shaped()
    {
        var output = Ingredient(Flint, 2);
        return new GridRecipe { Width = 2, Height = 1, Enabled = true, ResolvedIngredients = new[] { Ingredient(Flint), Ingredient(Stick) }, Output = output, Name = new AssetLocation("test:arrow") };
    }

    private static ItemSlot[] Grid(params (int cell, Item item, int count)[] contents)
    {
        var slots = Enumerable.Range(0, 9).Select(_ => (ItemSlot)new DummySlot()).ToArray();
        foreach (var (cell, item, count) in contents) slots[cell].Itemstack = new ItemStack(item, count);
        return slots;
    }

    private static IWorldAccessor World(params GridRecipe[] recipes)
    {
        var list = recipes.ToList();
        var proxy = DispatchProxy.Create<IWorldAccessor, WorldProxy>();
        ((WorldProxy)(object)proxy).Recipes = list;
        // items compare attributes through their api; give the test items one
        var api = DispatchProxy.Create<ICoreAPI, ApiProxy>();
        ((ApiProxy)(object)api).World = proxy;
        var field = typeof(CollectibleObject).GetField("api", BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var item in new[] { Stick, Flint, Knife, Bowl }) field.SetValue(item, api);
        return proxy;
    }
    public class ApiProxy : DispatchProxy
    {
        public IWorldAccessor World;
        protected override object Invoke(MethodInfo m, object[] a) => m.Name == "get_World" ? World : m.ReturnType.IsValueType && m.ReturnType != typeof(void) ? Activator.CreateInstance(m.ReturnType) : null;
    }

    [Fact]
    public void ATraitRecipeNeedsThePlacersTrait()
    {
        var recipe = Shaped();
        recipe.RequiresTrait = "clothier";
        var world = World(recipe);
        var grid = Grid((0, Flint, 1), (1, Stick, 1));
        Assert.Null(MachineRecipes.Find(world, grid));
        Assert.Null(MachineRecipes.Find(world, grid, t => t == "forager"));
        Assert.NotNull(MachineRecipes.Find(world, grid, t => t == "clothier"));
    }
    public class WorldProxy : DispatchProxy
    {
        public List<GridRecipe> Recipes;
        protected override object Invoke(MethodInfo m, object[] a) => m.Name == "get_GridRecipes" ? Recipes : m.ReturnType.IsValueType && m.ReturnType != typeof(void) ? Activator.CreateInstance(m.ReturnType) : null;
    }

    [Fact]
    public void ShapedRecipeIsFoundAnywhereInTheGridAndOnlyThere()
    {
        var world = World(Shaped());
        var match = MachineRecipes.Find(world, Grid((4, Flint, 3), (5, Stick, 3)));   // middle row, columns 1-2
        Assert.NotNull(match);
        Assert.Equal((1, 1), (match.Col, match.Row));
        Assert.Null(MachineRecipes.Find(world, Grid((4, Stick, 1), (5, Flint, 1))));   // mirrored: no
        Assert.Null(MachineRecipes.Find(world, Grid((4, Flint, 1), (5, Stick, 1), (0, Stick, 1))));   // a stray piece: no
        Assert.Null(MachineRecipes.Find(world, Grid()));
    }

    [Fact]
    public void OneCycleMakesAsManyAsTheCellsAllow()
    {
        var world = World(Shaped());
        var grid = Grid((0, Flint, 5), (1, Stick, 3));
        var match = MachineRecipes.Find(world, grid);
        Assert.Equal(3, MachineRecipes.Cycles(match, grid, 3));   // limited by the three sticks
        var output = new DummySlot();
        var dropped = new List<ItemStack>();
        MachineRecipes.Craft(world, match, grid, 3, output, dropped.Add);
        Assert.Equal(Flint, output.Itemstack.Item);
        Assert.Equal(6, output.Itemstack.StackSize);   // 3 cycles x 2
        Assert.Equal(2, grid[0].StackSize);           // leftovers stay on the plate
        Assert.True(grid[1].Empty);
        Assert.Empty(dropped);
    }

    [Fact]
    public void TheProductNeverExceedsOneStack()
    {
        var world = World(Shaped());
        var grid = Grid((0, Flint, 64), (1, Stick, 64));
        var match = MachineRecipes.Find(world, grid);
        Assert.Equal(32, MachineRecipes.Cycles(match, grid, 3));   // 32 x 2 = one stack of 64
    }

    [Fact]
    public void AToolWearsAndAContainerComesBackToItsCell()
    {
        var knife = Ingredient(Knife);
        knife.IsTool = true; knife.Consume = false; knife.DurabilityChange = -1;   // what Resolve derives from the json
        Knife.Durability = 100;
        var bowl = Ingredient(Bowl);
        bowl.ReturnedStack = new JsonItemStack { ResolvedItemstack = new ItemStack(Bowl) };
        var recipe = new GridRecipe { Width = 2, Height = 1, Enabled = true, ResolvedIngredients = new[] { knife, bowl }, Output = Ingredient(Stick, 1), Name = new AssetLocation("test:carve") };
        var world = World(recipe);
        var grid = Grid((0, Knife, 1), (1, Bowl, 2));
        var match = MachineRecipes.Find(world, grid);
        Assert.NotNull(match);
        var output = new DummySlot();
        MachineRecipes.Craft(world, match, grid, 3, output, _ => { });
        Assert.Equal(Stick, output.Itemstack.Item);
        Assert.Equal(2, output.Itemstack.StackSize);   // two bowls, two cycles
        Assert.False(grid[0].Empty);                   // the knife stays, two cycles worn
        Assert.Equal(98, Knife.GetRemainingDurability(grid[0].Itemstack));
        Assert.Equal(Bowl, grid[1].Itemstack.Item);    // bowls came back into their cell
        Assert.Equal(2, grid[1].StackSize);

        // a worn-out tool breaks in its cell, without the game's break sound at a missing entity
        grid[0].Itemstack.Attributes.SetInt("durability", 1);
        Assert.NotNull(MachineRecipes.Find(world, grid));
        MachineRecipes.Craft(world, MachineRecipes.Find(world, grid), grid, 3, new DummySlot(), _ => { });
        Assert.True(grid[0].Empty);
    }
}
