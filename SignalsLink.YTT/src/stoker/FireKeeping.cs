using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SignalsLink.YTT.src.stoker
{
    /// <summary>
    /// Lighting and putting out a locomotive's boiler at the price a player pays by hand. Shared
    /// by the stoker and the train ticket, so the two never disagree about what a fire costs.
    /// Every method returns a lang key suffix saying what happened.
    /// </summary>
    public static class FireKeeping
    {
        /// <summary>
        /// Lights the boiler and pays for it - but only once it really caught: paying per attempt
        /// would eat a box of firestarters at a locomotive with no fuel.
        /// </summary>
        /// <param name="lighter">Finds a slot with something the other mod counts as a light, or null.</param>
        public static string TryIgnite(YttFire fire, Entity locomotive, Func<ItemSlot> lighter, IWorldAccessor world, Vec3d soundAt)
        {
            ItemSlot fuel = fire.FuelSlotOf(locomotive);
            if (fuel == null || fuel.Empty) return "nofuel";

            ItemSlot slot = lighter?.Invoke();
            if (slot == null) return "noigniter";

            fire.Ignite(locomotive);

            if (fire.IsBurning(locomotive) != true) return "failed";

            Wear(world, slot, soundAt);
            return "lit";
        }

        /// <summary>
        /// Puts the boiler out at the price of the Extinguish button in the locomotive's own
        /// dialog: a litre of water per hundred degrees, out of the LOCOMOTIVE's water, and not
        /// at all when there is not enough. The other mod's own path cannot be called - it needs
        /// a real player - so the price is taken here and the fire put out for free afterwards.
        /// </summary>
        public static string TryExtinguish(YttFire fire, Entity locomotive)
        {
            double temperature = fire.TemperatureOf(locomotive) ?? 0;
            ItemSlot water = fire.WaterSlotOf(locomotive);

            double itemsPerLitre = 1;
            if (water?.Itemstack != null)
            {
                WaterTightContainableProps props = BlockLiquidContainerBase.GetContainableProps(water.Itemstack);
                if (props != null && props.ItemsPerLitre > 0) itemsPerLitre = props.ItemsPerLitre;
            }

            int needed = StokerRules.WaterItemsFor(temperature, itemsPerLitre);

            if (needed > 0)
            {
                if (water == null || water.Empty || water.StackSize < needed) return "nowater";

                water.TakeOut(needed);
                water.MarkDirty();

                // The engine saves nothing by itself; the water is gone, so say so now.
                fire.Save(locomotive);
            }

            fire.Extinguish(locomotive);
            return "quenched";
        }

        /// <summary>
        /// One use off the firestarter. Only things with a durability wear; a torch is free.
        ///
        /// NOT left to the game to destroy: DamageItem tolerates a null entity, but DestroyItem
        /// reads the entity's position and selection box, and there is no entity here - it would
        /// light nineteen times and crash the server on the twentieth.
        /// </summary>
        public static void Wear(IWorldAccessor world, ItemSlot slot, Vec3d soundAt)
        {
            ItemStack stack = slot?.Itemstack;
            CollectibleObject collectible = stack?.Collectible;
            if (collectible == null || collectible.GetMaxDurability(stack) <= 0) return;

            collectible.DamageItem(world, null, slot, 1, destroyOnZeroDurability: false);

            if (slot.Itemstack != null && collectible.GetRemainingDurability(slot.Itemstack) <= 0)
            {
                slot.Itemstack = null;
                if (soundAt != null) world.PlaySoundAt(new AssetLocation("game:sounds/effect/toolbreak"), soundAt.X, soundAt.Y, soundAt.Z, null, false, 12);
            }

            slot.MarkDirty();
        }
    }
}
