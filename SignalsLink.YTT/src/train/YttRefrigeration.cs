using System;
using SignalsLink.YTT.src.probe;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SignalsLink.YTT.src.train
{
    /// <summary>
    /// The refrigerant slots of a refrigerated wagon - where the ice goes, apart from the cargo.
    ///
    /// Reached the way the cargo is and guarded the same way: a probe that checked the member at
    /// startup, the save-wiring check before anything is written, the first transfer compared
    /// with what got saved, every call wrapped. It has a trust of its own, so when any of that
    /// fails only this stands down; cargo is none of its business.
    /// </summary>
    public class YttRefrigeration
    {
        private readonly YttSurface surface;

        public YttRefrigeration(YttSurface surface, YttPersistence cargo)
        {
            this.surface = surface;

            Persistence = cargo?.For(surface?.RefrigerantTreeKey ?? YttSurface.DefaultRefrigerantTreeKey,
                "Refrigerant slots will not be refilled this session; cargo is unaffected.");
        }

        /// <summary>The save checks for these slots alone. Null when there is no game behind this.</summary>
        public YttPersistence Persistence { get; }

        /// <summary>The wagon's refrigerant slots, or null when it has none that can be trusted.</summary>
        public InventoryBase InventoryOf(Entity entity)
        {
            if (surface?.CanReachRefrigerant != true || Persistence?.Trusted != true) return null;

            EntityBehavior behavior = entity?.GetBehavior(YttSurface.RefrigerationBehavior);
            if (behavior == null) return null;

            try
            {
                if (surface.RefrigerantInventoryProperty.GetValue(behavior) is not InventoryBase inventory) return null;

                // Is the other mod listening, so the ice gets saved? See YttPersistence.
                return Persistence.IsWired(inventory) ? inventory : null;
            }
            catch (Exception e)
            {
                Failed(entity, e);
                return null;
            }
        }

        private bool complained;

        private void Failed(Entity entity, Exception e)
        {
            if (complained) return;

            complained = true;
            entity?.World?.Logger?.Error("[SignalsLink.YTT] could not reach a wagon's refrigerant slots: " + e
                + " Cargo is unaffected; `ice` is not available this session.");
        }
    }
}
