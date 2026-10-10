using System;
using System.Collections.Generic;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SignalsLink.src.signals.managedchute.transporting
{
    /// <summary>Where the recipes come from; the game's grid recipes in play, a hand-built list in tests.</summary>
    public interface IGridRecipeBook
    {
        IEnumerable<GridRecipe> ForOutput(AssetLocation code);
    }

    public sealed class WorldRecipeBook : IGridRecipeBook
    {
        private readonly IWorldAccessor world;

        public WorldRecipeBook(IWorldAccessor world) { this.world = world; }

        public IEnumerable<GridRecipe> ForOutput(AssetLocation code)
        {
            List<GridRecipe> recipes = world?.GridRecipes;
            if (recipes == null || code == null) yield break;

            foreach (GridRecipe recipe in recipes)
            {
                AssetLocation output = recipe.Output?.ResolvedItemStack?.Collectible?.Code;
                if (output != null && output.Equals(code)) yield return recipe;
            }
        }
    }

    /// <summary>
    /// `recipe game:paper-parchment 2`: lays a grid recipe out in the first nine slots of the target,
    /// times the multiple asked for, out of the source. Planned dry first, moved only when the whole
    /// layout can be completed - never half a recipe.
    ///
    /// It works by difference: what already lies in the right cell (a tool left behind by the last
    /// craft, a half-used stack) is counted and not carried again. Anything in the nine cells that
    /// does not belong to the layout makes the block not executable.
    /// </summary>
    public static class RecipeFill
    {
        public const int GridWidth = 3, GridSlots = GridWidth * GridWidth;
        /// <summary>No multiple is searched for above this; a stack never holds more anyway.</summary>
        public const int MaxMultiples = 256;

        public sealed class Move
        {
            public ItemSlot From, To;
            public int Quantity;
        }

        public sealed class Plan
        {
            public GridRecipe Recipe;
            public int Multiples;
            public List<Move> Moves;

            public int Pieces
            {
                get { int n = 0; foreach (Move m in Moves) n += m.Quantity; return n; }
            }
        }

        /// <summary>The first recipe and multiple that can be completed, or null.</summary>
        public static Plan Prepare(IGridRecipeBook book, IWorldAccessor world, IInventory source, IInventory target, PaperConditionDirectives directives)
        {
            if (book == null || source == null || target == null || directives == null || !directives.HasRecipe) return null;
            if (target.Count < GridSlots) return null;

            foreach (GridRecipe recipe in book.ForOutput(directives.RecipeCode))
            {
                CraftingRecipeIngredient[] cells = Cells(recipe);
                if (cells == null) continue;

                // A shapeless recipe has no cells of its own: whatever already lies in the grid keeps
                // its place (the sieve left after the last sheet), the rest goes into the first free cells.
                if (recipe.Shapeless) cells = Arrange(cells, target);
                if (cells == null) continue;

                foreach (int k in Multiples(directives, source))
                {
                    List<Move> moves = TryPlan(world, source, target, cells, k);
                    if (moves != null) return new Plan { Recipe = recipe, Multiples = k, Moves = moves };
                }
            }

            return null;
        }

        /// <summary>Carries the plan out. Returns the pieces moved.</summary>
        public static int Execute(Plan plan, IWorldAccessor world)
        {
            if (plan == null) return 0;
            int total = 0;

            foreach (Move move in plan.Moves)
            {
                var op = new ItemStackMoveOperation(world, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, move.Quantity);
                int moved = move.From.TryPutInto(move.To, ref op);
                if (moved <= 0) continue;
                total += moved;
                move.From.MarkDirty();
                move.To.MarkDirty();
            }

            return total;
        }

        /// <summary>
        /// The recipe as nine cells, row by row, top-left in the grid; null cells must stay empty.
        /// A shapeless recipe is laid out in reading order.
        /// </summary>
        public static CraftingRecipeIngredient[] Cells(GridRecipe recipe)
        {
            CraftingRecipeIngredient[] resolved = recipe?.ResolvedIngredients;
            if (resolved == null) return null;

            var cells = new CraftingRecipeIngredient[GridSlots];

            if (recipe.Shapeless)
            {
                int n = 0;
                foreach (CraftingRecipeIngredient ingredient in resolved)
                {
                    if (ingredient == null) continue;
                    if (n >= GridSlots) return null;
                    cells[n++] = ingredient;
                }
                return cells;
            }

            if (recipe.Width > GridWidth || recipe.Height > GridWidth || resolved.Length < recipe.Width * recipe.Height) return null;

            for (int row = 0; row < recipe.Height; row++)
                for (int col = 0; col < recipe.Width; col++)
                    cells[row * GridWidth + col] = resolved[row * recipe.Width + col];

            return cells;
        }

        /// <summary>
        /// Shapeless: assign each occupied cell an ingredient it satisfies, then hand the remaining
        /// ingredients the empty cells in order. Null when an occupied cell fits no ingredient.
        /// </summary>
        public static CraftingRecipeIngredient[] Arrange(CraftingRecipeIngredient[] ingredients, IInventory target)
        {
            var left = new List<CraftingRecipeIngredient>();
            foreach (CraftingRecipeIngredient ingredient in ingredients) if (ingredient != null) left.Add(ingredient);

            var cells = new CraftingRecipeIngredient[GridSlots];
            for (int i = 0; i < GridSlots; i++)
            {
                ItemSlot cell = target[i];
                if (cell == null || cell.Empty) continue;
                int at = left.FindIndex(ingredient => ingredient.SatisfiesAsIngredient(cell.Itemstack, false));
                if (at < 0) return null;
                cells[i] = left[at];
                left.RemoveAt(at);
            }

            for (int i = 0; i < GridSlots && left.Count > 0; i++)
            {
                if (cells[i] != null || !target[i].Empty) continue;
                cells[i] = left[0];
                left.RemoveAt(0);
            }

            return left.Count == 0 ? cells : null;
        }

        /// <summary>The multiples to try, best first: `N` just N, `N-` N down to one, `N+` as many as the source could hold down to N.</summary>
        private static IEnumerable<int> Multiples(PaperConditionDirectives directives, IInventory source)
        {
            int n = Math.Max(1, directives.RecipeCount);

            switch (directives.RecipeMode)
            {
                case AmountMode.AtMost:
                    for (int k = n; k >= 1; k--) yield return k;
                    break;
                case AmountMode.AtLeast:
                    int pieces = 0;
                    for (int i = 0; i < source.Count; i++) pieces += source[i]?.StackSize ?? 0;
                    for (int k = Math.Min(MaxMultiples, Math.Max(n, pieces)); k >= n; k--) yield return k;
                    break;
                default:
                    yield return n;
                    break;
            }
        }

        /// <summary>
        /// The moves that complete the layout times <paramref name="k"/>, or null when they cannot.
        /// Ingredients sharing a name (`planks-*` as "wood" twice) must resolve to the same material,
        /// as the recipe itself demands; what already lies in a cell decides the material first, and
        /// an empty cell takes the first material the source holds enough of for every cell of that name.
        /// </summary>
        private static List<Move> TryPlan(IWorldAccessor world, IInventory source, IInventory target, CraftingRecipeIngredient[] cells, int k)
        {
            var bound = new Dictionary<string, CollectibleObject>();
            var material = new CollectibleObject[GridSlots];
            var need = new int[GridSlots];
            var wanted = new int[GridSlots];
            var needByName = new Dictionary<string, int>();

            // What each cell wants and already has; a cell holding the wrong thing ends it here.
            for (int i = 0; i < GridSlots; i++)
            {
                CraftingRecipeIngredient ingredient = cells[i];
                ItemSlot cell = target[i];
                if (cell == null) return null;

                if (ingredient == null)
                {
                    if (!cell.Empty) return null;   // something foreign in the grid
                    continue;
                }

                wanted[i] = ingredient.IsTool ? 1 : ingredient.Quantity * k;
                int have = 0;
                if (!cell.Empty)
                {
                    if (!ingredient.SatisfiesAsIngredient(cell.Itemstack, false)) return null;
                    material[i] = cell.Itemstack.Collectible;
                    if (!Bind(bound, ingredient, material[i])) return null;
                    have = cell.StackSize;
                }

                need[i] = Math.Max(0, wanted[i] - have);
                if (ingredient.Name != null) needByName[ingredient.Name] = needByName.GetValueOrDefault(ingredient.Name) + need[i];
            }

            var moves = new List<Move>();
            var available = new int[source.Count];
            for (int j = 0; j < source.Count; j++) available[j] = source[j]?.Empty == false ? source[j].StackSize : 0;

            for (int i = 0; i < GridSlots; i++)
            {
                if (need[i] <= 0) continue;
                CraftingRecipeIngredient ingredient = cells[i];
                ItemSlot cell = target[i];

                if (material[i] == null)
                {
                    material[i] = bound.TryGetValue(ingredient.Name ?? "", out CollectibleObject already) ? already
                        : MaterialFor(ingredient, source, available, bound, cell, ingredient.Name != null ? needByName[ingredient.Name] : need[i]);
                    if (material[i] == null) return null;
                    Bind(bound, ingredient, material[i]);
                }
                if (!ingredient.IsTool && wanted[i] > material[i].MaxStackSize) return null;   // the multiple does not fit one cell

                int left = need[i];
                for (int j = 0; j < source.Count && left > 0; j++)
                {
                    if (available[j] <= 0) continue;
                    ItemSlot from = source[j];
                    ItemStack stack = from.Itemstack;

                    if (stack.Collectible != material[i] || !ingredient.SatisfiesAsIngredient(stack, false)) continue;
                    if (!cell.Empty && !cell.Itemstack.Equals(world, stack, GlobalConstants.IgnoredStackAttributes)) continue;
                    if (cell.Empty && !(cell.CanTakeFrom(from, EnumMergePriority.DirectMerge) && cell.CanHold(from))) continue;

                    int take = Math.Min(left, available[j]);
                    moves.Add(new Move { From = from, To = cell, Quantity = take });
                    available[j] -= take;
                    left -= take;
                }

                if (left > 0) return null;
            }

            return moves;
        }

        /// <summary>The first material in the source that satisfies the ingredient, fits the cell and is there in full.</summary>
        private static CollectibleObject MaterialFor(CraftingRecipeIngredient ingredient, IInventory source, int[] available,
            Dictionary<string, CollectibleObject> bound, ItemSlot cell, int need)
        {
            var counted = new HashSet<CollectibleObject>();
            for (int j = 0; j < source.Count; j++)
            {
                if (available[j] <= 0) continue;
                ItemSlot from = source[j];
                CollectibleObject candidate = from.Itemstack.Collectible;
                if (!counted.Add(candidate)) continue;
                if (!ingredient.SatisfiesAsIngredient(from.Itemstack, false) || !Fits(bound, ingredient, candidate)) continue;
                if (!(cell.CanTakeFrom(from, EnumMergePriority.DirectMerge) && cell.CanHold(from))) continue;

                int total = 0;
                for (int i = 0; i < source.Count; i++)
                    if (available[i] > 0 && source[i].Itemstack.Collectible == candidate) total += available[i];
                if (total >= need) return candidate;
            }
            return null;
        }

        private static bool Fits(Dictionary<string, CollectibleObject> bound, CraftingRecipeIngredient ingredient, CollectibleObject material)
            => ingredient.Name == null || !bound.TryGetValue(ingredient.Name, out CollectibleObject other) || other == material;

        private static bool Bind(Dictionary<string, CollectibleObject> bound, CraftingRecipeIngredient ingredient, CollectibleObject material)
        {
            if (ingredient.Name == null) return true;
            if (bound.TryGetValue(ingredient.Name, out CollectibleObject other)) return other == material;
            bound[ingredient.Name] = material;
            return true;
        }
    }
}
