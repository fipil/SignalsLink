using SignalsLink.src.signals.managedchute.transporting;
using SignalsLink.src.signals.manageddock;
using Vintagestory.GameContent;
using Vintagestory.API.Common;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>
    /// Where a valve or a hose is allowed to pour.
    ///
    /// This used to be decided by naming the two slot classes the game ships, which is a list
    /// nobody can join. The dock's own slot takes goods or liquid, whichever turns up, and was
    /// refused — silently, because "this slot cannot take liquid" and "there is nowhere to pour"
    /// come out of the service as the same answer.
    /// </summary>
    public class LiquidTargetSlotTests
    {
        [Fact]
        public void A_slot_that_holds_its_own_litres_can_be_poured_into()
        {
            Assert.True(Service(Dock(4)).HasAnyLiquidTargetSlot(0));
        }

        [Fact]
        public void A_named_slot_that_holds_its_own_litres_can_be_poured_into()
        {
            // `target 3` on the paper picks one slot by number; it goes through the same question
            // by a different door, and the door was just as shut.
            Assert.True(Service(Dock(4)).HasAnyLiquidTargetSlot(3));
        }

        [Fact]
        public void An_ordinary_slot_still_cannot()
        {
            // The point of the change is not that everything takes liquid now.
            Assert.False(Service(Plain(4)).HasAnyLiquidTargetSlot(0));
            Assert.False(Service(Plain(4)).HasAnyLiquidTargetSlot(1));
        }

        [Fact]
        public void A_locomotive_water_slot_is_a_target()
        {
            // YTT's water slot is an ItemSlotLiquidOnly. The dock has to recognise it, or there is
            // no way to top up an engine at all.
            InventoryGeneric engine = new InventoryGeneric(2, "signalslink-fakeengine", null,
                (id, inv) => new ItemSlotLiquidOnly(inv, 50f));

            Assert.True(Service(engine).HasAnyLiquidTargetSlot(0));
        }

        [Fact]
        public void A_slot_number_past_the_end_is_no_target()
        {
            Assert.False(Service(Dock(4)).HasAnyLiquidTargetSlot(9));
        }

        [Fact]
        public void The_dock_slot_says_how_much_fits()
        {
            // A barrel's worth. The service reads this to work out what is left, so a slot that
            // answered zero would be found and then never filled.
            ILiquidHoldingSlot slot = new ItemSlotGoodsOrLiquid(null);

            Assert.Equal(ItemSlotGoodsOrLiquid.LitresPerSlot, slot.CapacityLitres);
            Assert.True(slot.CapacityLitres > 0);
        }

        // ---------------------------------------------------------------- the slot's own consent

        [Fact]
        public void A_slot_that_refuses_this_liquid_is_no_target()
        {
            // YTT's water slot takes any liquid but refuses a flammable one - the one that makes
            // the boiler explode. Everything above reasons about the slot's CLASS; only asking the
            // slot reaches that rule.
            InventoryGeneric picky = new InventoryGeneric(2, "signalslink-picky", null,
                (id, inv) => new RefusingSlot(inv));

            Assert.Null(Service(picky).GetTargetSlot(Water(), 0));
        }

        [Fact]
        public void A_slot_that_accepts_it_still_is()
        {
            Assert.NotNull(Service(Dock(2)).GetTargetSlot(Water(), 0));
        }

        [Fact]
        public void Nothing_is_poured_into_a_slot_that_refuses()
        {
            // The check is made again at the moment of writing: that call cannot be undone.
            InventoryGeneric picky = new InventoryGeneric(2, "signalslink-picky", null,
                (id, inv) => new RefusingSlot(inv));

            ItemSlot source = new DummySlot(Water());

            Assert.False(Service(picky).TryMoveFromItemSlot(source, picky[0], 10, false).Success);
            Assert.True(picky[0].Empty);
        }

        [Fact]
        public void The_dock_slot_holds_liquid_when_it_is_asked()
        {
            // The hose pouring into the dock goes through this now. It worked in game before the
            // slot was ever asked, and it has to go on working.
            Assert.True(new ItemSlotGoodsOrLiquid(null).CanHold(new DummySlot(Water())));
        }

        private static ItemStack Water()
        {
            return TestStacks.Liquid(new Item(), "game:waterportion");
        }

        /// <summary>A slot that takes litres by class and refuses this particular liquid.</summary>
        private sealed class RefusingSlot : ItemSlotLiquidOnly
        {
            public RefusingSlot(InventoryGeneric inventory) : base(inventory, 50f)
            {
            }

            public override bool CanHold(ItemSlot sourceSlot) => false;
        }

        /// <summary>No world and no position: this asks about the slots alone.</summary>
        private static LiquidTransferService Service(InventoryGeneric inventory)
        {
            return new LiquidTransferService(null, inventory, null);
        }

        private static InventoryGeneric Dock(int slots)
        {
            return new InventoryGeneric(slots, "signalslink-dock", null, (id, inv) => new ItemSlotGoodsOrLiquid(inv));
        }

        private static InventoryGeneric Plain(int slots)
        {
            return new InventoryGeneric(slots, "signalslink-plain", null);
        }
    }
}
