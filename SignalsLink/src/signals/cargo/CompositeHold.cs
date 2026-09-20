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
        /// Every hold, because "goods moved in this holder" is the only thing that can honestly be
        /// said from here.
        ///
        /// It used to pass this on only to holds whose inventory reported <c>IsDirty</c>. That
        /// flag means "needs resending to the client", not "was written to" - so a hold whose
        /// inventory nobody syncs never qualified, never got told, and never wrote its goods down.
        /// A cart's chest is exactly such a hold, and its cargo quietly vanished.
        ///
        /// Telling a hold that was not in fact written to is harmless here; what it used to break
        /// is fixed where it belongs, in the bridge's own check.
        /// </summary>
        public void MarkDirty()
        {
            foreach (ICargoHold hold in holds) hold.MarkDirty();
        }
    }
}
