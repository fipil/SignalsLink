using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SignalsLink.src.signals.managedchute.transporting;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>
    /// Loose liquid belongs to hoses. The dock is the one exception, because it has to top up a
    /// locomotive - and it has to ask for that itself, since the transfer class is shared with the
    /// chute and the damper, which are deliberately not liquid handlers.
    /// </summary>
    public class LiquidIsForHosesTests
    {
        [Fact]
        public void A_chute_will_not_pick_up_loose_liquid()
        {
            Assert.False(Tank(carriesLiquid: false).Accepts(0));
        }

        [Fact]
        public void The_dock_will()
        {
            Assert.True(Tank(carriesLiquid: true).Accepts(0));
        }

        [Fact]
        public void A_device_carries_no_liquid_unless_it_says_so()
        {
            // The default is what the chute and the damper get, so it is the one that matters.
            Assert.False(new InventoryToInventoryTransfer(null, null, null, null, 0, 0, null).CarriesLiquid);
        }

        [Fact]
        public void Ordinary_goods_go_either_way()
        {
            Assert.True(Crate(carriesLiquid: false).Accepts(0));
            Assert.True(Crate(carriesLiquid: true).Accepts(0));
        }

        /// <summary>Loose water against a liquid-only tank, the way a locomotive's is.</summary>
        private static TestDevice Tank(bool carriesLiquid)
        {
            InventoryGeneric target = new InventoryGeneric(2, "signalslink-testtank", null,
                (id, inv) => new ItemSlotLiquidOnly(inv, 50f));

            return Device(TestStacks.Inventory(2, Portion()), target, carriesLiquid);
        }

        /// <summary>Firewood against ordinary slots: the branch this change must not touch.</summary>
        private static TestDevice Crate(bool carriesLiquid)
        {
            return Device(TestStacks.Inventory(2, TestStacks.Item("game:firewood", 4)),
                TestStacks.Inventory(2), carriesLiquid);
        }

        private static TestDevice Device(IInventory source, IInventory target, bool carriesLiquid)
        {
            PaperConditionsEvaluator evaluator = new PaperConditionsEvaluator();
            evaluator.SetConditionsText("*\n");

            return new TestDevice(source, target, evaluator) { CarriesLiquid = carriesLiquid };
        }

        private static ItemStack Portion()
        {
            ItemLiquidPortion water = new ItemLiquidPortion
            {
                Code = new AssetLocation("game:waterportion"),
                MatterState = EnumMatterState.Liquid,
                Attributes = new JsonObject(
                    JToken.Parse("{ \"waterTightContainerProps\": { \"containable\": true, \"itemsPerLitre\": 100 } }"))
            };

            ItemStack stack = new ItemStack(water, 500);
            stack.Attributes ??= new TreeAttribute();

            return stack;
        }

        /// <summary>
        /// Vintage Story keeps its own ItemLiquidPortion internal, which is why the production
        /// check compares the collectible's simple type NAME. A stand-in of that name takes exactly
        /// the same branch - and if that check is ever tightened to a real type, these tests are
        /// meant to fail and say so.
        /// </summary>
        private sealed class ItemLiquidPortion : Item
        {
        }

        private sealed class TestDevice : InventoryToInventoryTransfer
        {
            private readonly IInventory source;
            private readonly PaperConditionsEvaluator evaluator;

            public TestDevice(IInventory source, IInventory target, PaperConditionsEvaluator evaluator)
                : base(null, source, target, null, 0, 0, evaluator)
            {
                this.source = source;
                this.evaluator = evaluator;
            }

            public bool Accepts(int slotIndex)
            {
                IReadOnlyList<ConditionBlock> blocks = evaluator.GetBlocks();
                return CanTransferSelection(source[slotIndex], blocks[0].Directives);
            }
        }
    }
}
