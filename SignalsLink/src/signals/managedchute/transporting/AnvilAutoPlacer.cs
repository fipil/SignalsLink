using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.managedchute.transporting
{
    public sealed class AnvilAutoPlacer
    {
        private readonly FieldInfo fiWorkItemStack;

        /// <summary>
        /// Create an instance (your chute/pipe can keep one).
        /// Reflection FieldInfo is cached per instance.
        /// </summary>
        public AnvilAutoPlacer()
        {
            fiWorkItemStack = typeof(BlockEntityAnvil).GetField("workItemStack", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        /// <summary>
        /// Places the given input onto anvil and selects a recipe.
        ///
        /// input can be:
        /// - stack of ingots (e.g. StackSize=2)
        /// - iron bloom (usually StackSize=1)
        /// - steel ingot with crust / other IAnvilWorkable
        ///
        /// The method assumes anvil is empty.
        /// </summary>
        public bool TryPlaceAndSelectRecipe(
            BlockEntityAnvil anvil,
            ItemStack input,
            out string failReason,
            int desiredIngotCountForPlate = 2
        )
        {
            failReason = null;

            if (anvil == null) { failReason = "Anvil is null."; return false; }
            if (input == null || input.StackSize == 0) { failReason = "Input is empty."; return false; }

            if (fiWorkItemStack == null)
            {
                failReason = "Reflection failed: BlockEntityAnvil.workItemStack field not found (game update?).";
                return false;
            }

            // Must be workable
            var workable = input.Collectible?.GetCollectibleInterface<IAnvilWorkable>();
            if (workable == null)
            {
                failReason = $"Item '{input.Collectible?.Code}' is not IAnvilWorkable.";
                return false;
            }

            // Determine whether we should prefer plate recipe (ingots) or use fallback-first (non-ingots)
            bool inputLooksLikeIngot = LooksLikeIngot(input);

            // 0) If anvil already has a work item, we may try to add an ingot to an existing plate workitem.
            if (anvil.WorkItemStack != null)
            {
                // Only support adding ingots to an existing ingot-based plate workitem.
                if (!inputLooksLikeIngot)
                {
                    failReason = "Anvil already has a work item and input is not an ingot.";
                    return false;
                }

                // Very simple heuristic: only if currently one ingot's worth of voxels is present.
                // We can't easily read voxel count without touching internals, so use StackSize==1 as proxy.
                if (anvil.WorkItemStack.StackSize != 1)
                {
                    failReason = "Anvil work item already has more than one ingot; refusing automatic add.";
                    return false;
                }

                var extraUnit = input.Clone();
                extraUnit.StackSize = 1;

                var addResult = workable.TryPlaceOn(extraUnit, anvil);
                if (addResult == null)
                {
                    failReason = "Failed to add ingot to existing work item (try hammering first?).";
                    return false;
                }

                // Keep internal field in sync with updated work item
                SetPrivateWorkItemStack(anvil, addResult);
                FinalizePlacement(anvil, addResult, selectedRecipeId: null);
                return true;
            }

            // 1) Place the first unit (TryPlaceOn usually expects StackSize=1)
            var firstUnit = input.Clone();
            firstUnit.StackSize = 1;

            var createdWorkItem = workable.TryPlaceOn(firstUnit, anvil);
            if (createdWorkItem == null)
            {
                failReason = "TryPlaceOn rejected the item (too cold? wrong state? anvil conditions?).";
                return false;
            }

            // Important: Anvil does not set workItemStack itself here (normally done inside BE Anvil TryPut)
            SetPrivateWorkItemStack(anvil, createdWorkItem);

            // 2) If input is ingot stack and we want more ingots (typically 2 for plate), try to add additional units.
            //    Vanilla may reject adding more voxels until the player hammers down; treat that as non-fatal.
            if (inputLooksLikeIngot && input.StackSize > 1 && desiredIngotCountForPlate > 1)
            {
                int addCount = Math.Min(desiredIngotCountForPlate, input.StackSize) - 1;
                for (int i = 0; i < addCount; i++)
                {
                    var addUnit = input.Clone();
                    addUnit.StackSize = 1;

                    var addWorkable = addUnit.Collectible?.GetCollectibleInterface<IAnvilWorkable>();
                    if (addWorkable == null) break;

                    var maybeUpdatedWorkItem = addWorkable.TryPlaceOn(addUnit, anvil);
                    if (maybeUpdatedWorkItem == null)
                    {
                        // Likely "Try hammering down before adding additional voxels"
                        break;
                    }

                    // Some implementations return a clone/updated stack; keep anvil's private field aligned
                    SetPrivateWorkItemStack(anvil, maybeUpdatedWorkItem);
                    createdWorkItem = maybeUpdatedWorkItem;
                }
            }

            // 3) Select recipe
            // We try to resolve recipe based on base material (ingot-xxx) derived from workitem.
            var api = anvil.Api;
            var baseMaterialForLookup = GetBaseMaterialStackForRecipeLookup(api, anvil.WorkItemStack ?? createdWorkItem, input);

            int recipeId = ResolveRecipeId(api, baseMaterialForLookup, preferPlate: inputLooksLikeIngot);

            // If no recipes exist:
            // - ingots => fail
            // - non-ingots => allow (place workitem only)
            if (recipeId < 0)
            {
                if (inputLooksLikeIngot)
                {
                    failReason = "No matching smithing recipe found for this ingot material.";
                    return false;
                }

                // Non-ingots: allow placing without recipe selection
                // (Some workable flows use a different mode and don't rely on smithing recipes list)
                FinalizePlacement(anvil, createdWorkItem, selectedRecipeId: null);
                return true;
            }

            // 4) Apply recipe to BE + to stack attributes so it is transferable like vanilla
            FinalizePlacement(anvil, createdWorkItem, recipeId);
            return true;
        }

        // --------------------
        // Internal helpers
        // --------------------

        private void FinalizePlacement(BlockEntityAnvil anvil, ItemStack workItem, int? selectedRecipeId)
        {
            // Ensure private field is set to the latest workItem
            SetPrivateWorkItemStack(anvil, workItem);

            if (selectedRecipeId.HasValue)
            {
                anvil.SelectedRecipeId = selectedRecipeId.Value;
                workItem.Attributes.SetInt("selectedRecipeId", selectedRecipeId.Value);
            }

            // rotation can be left as-is (0) unless you need it. If workItem contains it, you can sync it:
            // anvil.rotation = workItem.Attributes.GetInt("rotation", 0);

            anvil.MarkDirty(true);
            anvil.Api?.World?.BlockAccessor?.MarkBlockDirty(anvil.Pos);
        }

        private void SetPrivateWorkItemStack(BlockEntityAnvil anvil, ItemStack workItem)
        {
            fiWorkItemStack!.SetValue(anvil, workItem);
        }

        /// <summary>
        /// Resolve smithing recipe id for the given material.
        /// If preferPlate: try recipe Name.Path == "plate" first.
        /// If not found: use first matching recipe.
        /// Returns -1 if no matching recipes exist.
        /// </summary>
        private static int ResolveRecipeId(ICoreAPI api, ItemStack baseMaterial, bool preferPlate)
        {
            var recipes = api.GetSmithingRecipes()
                .Where(r => r.Ingredient != null && r.Ingredient.SatisfiesAsIngredient(baseMaterial))
                .ToList();

            if (recipes.Count == 0) return -1;

            if (preferPlate)
            {
                var plate = recipes.FirstOrDefault(r => r.Name?.Path == "plate");
                if (plate != null) return plate.RecipeId;
            }

            // fallback: first matching recipe
            return recipes[0].RecipeId;
        }

        /// <summary>
        /// Determines whether the stack looks like a vanilla ingot.
        /// (Skeleton heuristic; adjust if your mod uses other item codes.)
        /// </summary>
        private static bool LooksLikeIngot(ItemStack stack)
        {
            var path = stack?.Collectible?.Code?.Path;
            if (string.IsNullOrEmpty(path)) return false;
            return path.StartsWith("ingot-");
        }

        /// <summary>
        /// For recipe lookup we want base ingot material.
        /// If we already have workitem-xxx, derive ingot-xxx.
        /// Else fallback to original input.
        /// </summary>
        private static ItemStack GetBaseMaterialStackForRecipeLookup(ICoreAPI api, ItemStack workItemOrNull, ItemStack originalInput)
        {
            var code = workItemOrNull?.Collectible?.Code;
            if (code != null && code.Path != null && code.Path.StartsWith("workitem-"))
            {
                string metal = code.Path.Substring("workitem-".Length);
                var ingotCode = new AssetLocation("game", "ingot-" + metal);
                var ingotItem = api.World.GetItem(ingotCode);
                if (ingotItem != null)
                {
                    return new ItemStack(ingotItem, 1);
                }
            }

            // Fallback: use the original item (StackSize=1)
            var baseMat = originalInput.Clone();
            baseMat.StackSize = 1;
            return baseMat;
        }
    }
}
