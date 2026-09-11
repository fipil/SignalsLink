using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.manageddock
{
    /// <summary>
    /// A slot that takes goods or a measure of liquid, whichever turns up - the way a cooking pot
    /// does.
    ///
    /// The dock is not split into a half for things and a half for liquids on purpose. A split
    /// would need a directive for saying which half a block means, and the paper already picks by
    /// what is IN a slot rather than by where the slot is. One undivided inventory keeps that true.
    ///
    /// Liquid arrives in two shapes - loose measures, or inside a bucket or barrel where it sits in
    /// the stack attributes. Both are an item stack in a slot, so both simply fit.
    /// </summary>
    public class ItemSlotUniversal : ItemSlot
    {
        /// <summary>How much loose liquid one slot holds. A bucket is 10, a barrel 50.</summary>
        public const float CapacityLitres = 50f;

        public ItemSlotUniversal(InventoryBase inventory) : base(inventory)
        {
        }

        /// <summary>
        /// Loose liquid is counted in litres, not in stack sizes, so its capacity has to be worked
        /// out from the liquid's own properties. Everything else keeps the ordinary rule.
        /// </summary>
        public override int GetRemainingSlotSpace(ItemStack forItemstack)
        {
            WaterTightContainableProps props = BlockLiquidContainerBase.GetContainableProps(forItemstack);
            if (props == null) return base.GetRemainingSlotSpace(forItemstack);

            int capacity = (int)(CapacityLitres * props.ItemsPerLitre);

            return Empty ? capacity : capacity - StackSize;
        }
    }
}
