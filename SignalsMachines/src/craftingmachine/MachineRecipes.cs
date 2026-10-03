using System.Reflection;
using Vintagestory.API.Common;

namespace SignalsMachines.src.craftingmachine;

/// <summary>
/// Grid recipes for a machine with no player: the game's own matching needs a player for trait hooks
/// and its consumption hands returned items to one, so the shaped match and the consumption are done
/// here on the public recipe API. Shapeless matching is the game's own (wildcards, merging), reached
/// by reflection because it is protected. Recipes that require a player trait are made when the
/// machine's placer has that trait (the machine takes over the class of whoever placed it).
/// </summary>
public static class MachineRecipes
{
    /// <summary>A matching recipe and where it sits in the grid (Col/Row = -1 for shapeless).</summary>
    public sealed record Match(GridRecipe Recipe, int Col, int Row);

    private static readonly MethodInfo ShapelessMatch = typeof(RecipeBase).GetMethod("MatchesShapeLess", BindingFlags.NonPublic | BindingFlags.Instance);

    /// <param name="hasTrait">Whether the machine's placer has a trait; null = no trait recipes at all.</param>
    public static Match Find(IWorldAccessor world, ItemSlot[] grid, System.Func<string, bool> hasTrait = null, int width = BECraftingMachine.GridSize)
    {
        if (grid.All(s => s.Empty)) return null;
        foreach (var recipe in world.GridRecipes)
        {
            if (!recipe.Enabled || recipe.ResolvedIngredients == null) continue;
            if (recipe.RequiresTrait != null && (hasTrait == null || !hasTrait(recipe.RequiresTrait))) continue;
            var match = MatchOne(world, recipe, grid, width);
            if (match != null) return match;
        }
        return null;
    }

    public static Match MatchOne(IWorldAccessor world, GridRecipe recipe, ItemSlot[] grid, int width)
    {
        int height = grid.Length / width;
        if (width < recipe.Width || height < recipe.Height) return null;
        if (recipe.Shapeless)
            return (bool)ShapelessMatch.Invoke(recipe, new object[] { grid, world, recipe.ResolvedIngredients }) ? new Match(recipe, -1, -1) : null;
        for (int col = 0; col <= width - recipe.Width; col++)
            for (int row = 0; row <= height - recipe.Height; row++)
                if (MatchesAt(recipe, grid, width, col, row)) return new Match(recipe, col, row);
        return null;
    }

    // Same rule as the game: every cell either holds what the recipe wants there, or is empty where the recipe is empty.
    private static bool MatchesAt(GridRecipe recipe, ItemSlot[] grid, int width, int col, int row)
    {
        int height = grid.Length / width;
        for (int c = 0; c < width; c++)
            for (int r = 0; r < height; r++)
            {
                var stack = grid[r * width + c].Itemstack;
                var ingredient = IngredientAt(recipe, r - row, c - col);
                if (stack == null && ingredient == null) continue;
                if (stack == null || ingredient == null) return false;
                if (!ingredient.SatisfiesAsIngredient(stack) || !stack.Collectible.MatchesForCrafting(stack, recipe, ingredient)) return false;
            }
        return true;
    }

    private static CraftingRecipeIngredient IngredientAt(GridRecipe recipe, int r, int c) =>
        r < 0 || c < 0 || r >= recipe.Height || c >= recipe.Width ? null : recipe.ResolvedIngredients[r * recipe.Width + c];

    /// <summary>Which ingredient each cell serves; null for cells the recipe does not use.</summary>
    public static CraftingRecipeIngredient[] Assignment(Match match, ItemSlot[] grid, int width)
    {
        var result = new CraftingRecipeIngredient[grid.Length];
        if (match.Col >= 0)
        {
            for (int i = 0; i < grid.Length; i++) result[i] = IngredientAt(match.Recipe, i / width - match.Row, i % width - match.Col);
            return result;
        }
        // shapeless: each ingredient takes the first fitting cell not yet spoken for
        var free = Enumerable.Range(0, grid.Length).Where(i => !grid[i].Empty).ToList();
        foreach (var ingredient in match.Recipe.ResolvedIngredients)
        {
            if (ingredient == null) continue;
            int cell = free.FirstOrDefault(i => ingredient.SatisfiesAsIngredient(grid[i].Itemstack, false), -1);
            if (cell < 0) continue;
            result[cell] = ingredient;
            free.Remove(cell);
        }
        return result;
    }

    /// <summary>Pieces one cycle takes from a cell for its ingredient.</summary>
    public static int PiecesPerCycle(CraftingRecipeIngredient ingredient) =>
        !ingredient.ConsumeProperties.Consume ? 0 : ingredient.MatchingType == EnumRecipeMatchType.Exact ? ingredient.ResolvedItemStack?.StackSize ?? ingredient.Quantity : ingredient.Quantity;

    /// <summary>
    /// How many times the recipe runs in one cycle: as often as the cells allow and the product still
    /// fits one stack (contract 11: one cycle uses up what it can).
    /// </summary>
    public static int Cycles(Match match, ItemSlot[] grid, int width)
    {
        var assignment = Assignment(match, grid, width);
        int cycles = int.MaxValue;
        for (int i = 0; i < grid.Length; i++)
        {
            int pieces = assignment[i] == null ? 0 : PiecesPerCycle(assignment[i]);
            if (pieces > 0) cycles = Math.Min(cycles, grid[i].StackSize / pieces);
        }
        if (cycles == int.MaxValue) cycles = 1;   // nothing consumed (tools only)
        var output = match.Recipe.Output?.ResolvedItemStack;
        if (output != null) cycles = Math.Min(cycles, Math.Max(1, output.Collectible.MaxStackSize / Math.Max(1, output.StackSize)));
        return Math.Max(1, cycles);
    }

    /// <summary>
    /// Runs the recipe: the product (all cycles in one stack) goes to the output slot, ingredients are
    /// taken from their cells, tools wear, returned items stay in the emptied cell or are handed to
    /// <paramref name="overflow"/>.
    /// </summary>
    public static int Craft(IWorldAccessor world, Match match, ItemSlot[] grid, int width, ItemSlot output, Action<ItemStack> overflow)
    {
        int cycles = Cycles(match, grid, width);
        var assignment = Assignment(match, grid, width);
        match.Recipe.GenerateOutputStack(grid, output);   // reads the inputs for attribute copying: before consuming
        output.Itemstack.StackSize *= cycles;
        for (int i = 0; i < grid.Length; i++)
        {
            var ingredient = assignment[i];
            var slot = grid[i];
            if (ingredient == null || slot.Empty) continue;
            var props = ingredient.ConsumeProperties;
            if (props.Consume)
            {
                slot.Itemstack.StackSize -= PiecesPerCycle(ingredient) * cycles;
                if (slot.Itemstack.StackSize <= 0) slot.Itemstack = null;
            }
            else if (props.DurabilityChange < 0)
            {
                for (int k = 0; k < cycles && slot.Itemstack != null; k++)
                    slot.Itemstack.Collectible.DamageItem(world, null, slot, props.DurabilityCost, props.BreakOnZeroDurability);
            }
            if (ingredient.ReturnedStack?.ResolvedItemStack != null)
            {
                var returned = ingredient.ReturnedStack.ResolvedItemStack.Clone();
                returned.StackSize *= cycles;
                if (slot.Empty) slot.Itemstack = returned; else overflow(returned);
            }
            slot.MarkDirty();
        }
        output.MarkDirty();
        return cycles;
    }
}
