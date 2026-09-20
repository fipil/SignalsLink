using SignalsLink.src.signals.managedchute.transporting;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.manageddock
{
    /// <summary>
    /// A slot that takes goods or a measure of liquid, whichever turns up. The dock is deliberately
    /// not split into a half for each: the paper picks by what is IN a slot, not by where it is.
    /// </summary>
    public class ItemSlotGoodsOrLiquid : ItemSlot, ILiquidHoldingSlot
    {
        /// <summary>A barrel's worth.</summary>
        public const float LitresPerSlot = 50f;

        public ItemSlotGoodsOrLiquid(InventoryBase inventory) : base(inventory)
        {
        }

        public float CapacityLitres => LitresPerSlot;

        /// <summary>
        /// The base class judges by storage flags, which a liquid portion does not carry. Saying
        /// yes here is what the slot is for - it is asked before anything is poured in.
        /// </summary>
        public override bool CanHold(ItemSlot sourceSlot)
        {
            return BlockLiquidContainerBase.GetContainableProps(sourceSlot?.Itemstack) != null
                || base.CanHold(sourceSlot);
        }

        /// <summary>Loose liquid is counted in litres, everything else in stack sizes.</summary>
        public override int GetRemainingSlotSpace(ItemStack forItemstack)
        {
            WaterTightContainableProps props = BlockLiquidContainerBase.GetContainableProps(forItemstack);
            if (props == null) return base.GetRemainingSlotSpace(forItemstack);

            int capacity = (int)(LitresPerSlot * props.ItemsPerLitre);

            return Empty ? capacity : capacity - StackSize;
        }
    }
}
