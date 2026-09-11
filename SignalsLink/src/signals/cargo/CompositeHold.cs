using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.cargo
{
    /// <summary>
    /// A whole holder as one hold, so that <c>in target game:firewood 15-</c> is one question
    /// about the train rather than one per wagon.
    /// </summary>
    public sealed class CompositeHold : ICargoHold
    {
        private readonly IReadOnlyList<ICargoHold> holds;
        private readonly CompositeInventory inventory;

        private CompositeHold(ICoreAPI api, IReadOnlyList<ICargoHold> holds)
        {
            this.holds = holds;

            inventory = new CompositeInventory(api);
            foreach (ICargoHold hold in holds) inventory.Append(hold);

            Code = holds.Count + " holds";
        }

        /// <summary>
        /// One hold standing for all of them, or the list unchanged. Places in the world are never
        /// composed - there is no single position to write to.
        /// </summary>
        public static IReadOnlyList<ICargoHold> Over(ICoreAPI api, IReadOnlyList<ICargoHold> holds)
        {
            if (holds == null || holds.Count < 2) return holds;

            foreach (ICargoHold hold in holds)
            {
                if (!hold.IsContainer || hold.Inventory == null) return holds;
            }

            return new ICargoHold[] { new CompositeHold(api, holds) };
        }

        public string Code { get; }

        public IInventory Inventory => inventory;

        public BlockPos Pos => null;

        public bool IsContainer => true;

        public bool IsEmpty
        {
            get
            {
                foreach (ICargoHold hold in holds)
                {
                    if (!hold.IsEmpty) return false;
                }

                return true;
            }
        }

        /// <summary>
        /// Only the holds that changed. Telling an untouched wagon it was written to fails the
        /// YTT bridge's post-transfer check and stands the bridge down for the session.
        /// </summary>
        public void MarkDirty()
        {
            foreach (ICargoHold hold in holds)
            {
                if (hold.Inventory is InventoryBase inventoryBase && !inventoryBase.IsDirty) continue;

                hold.MarkDirty();
            }
        }
    }
}
