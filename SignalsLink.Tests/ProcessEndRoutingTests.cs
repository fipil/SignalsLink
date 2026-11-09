using System.Collections.Generic;
using System.Reflection;
using SignalsLink.src.signals.managedchute.transporting;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;
using Proxy = SignalsLink.Tests.AnchorLifecycleTests.Proxy;

namespace SignalsLink.Tests
{
    /// <summary>
    /// Which transfer the factory builds around a boiler, a condenser and a fruit press. The
    /// fault these guard against is the plain inventory route reaching one of them: a bucket in
    /// the condenser's water slot or in the press's mash slot, firewood in the boiler's liquid.
    /// </summary>
    public class ProcessEndRoutingTests
    {
        static readonly BlockPos ChestPos = new BlockPos(0, 0, 0, 0);
        static readonly BlockPos OtherChestPos = new BlockPos(9, 0, 0, 0);
        static readonly BlockPos CondenserPos = new BlockPos(1, 0, 0, 0);
        static readonly BlockPos BoilerPos = new BlockPos(2, 0, 0, 0);
        static readonly BlockPos PressPos = new BlockPos(3, 0, 0, 0);
        static readonly BlockPos PressTopPos = new BlockPos(3, 1, 0, 0);
        static readonly BlockPos AirPos = new BlockPos(4, 0, 0, 0);

        [Fact]
        public void A_chest_hands_a_bucket_to_the_condenser_stand_only()
        {
            Assert.IsType<InventoryToBucketSlotTransfer>(Route(ChestPos, CondenserPos));
        }

        [Fact]
        public void The_condenser_stand_hands_to_a_chest_through_the_slot_view_with_a_redraw()
        {
            var wrapped = Assert.IsType<EndRefreshTransfer>(Route(CondenserPos, ChestPos));
            Assert.IsType<InventoryToInventoryTransfer>(wrapped.Inner);
        }

        [Fact]
        public void The_world_cannot_hand_anything_to_a_stand()
        {
            Assert.Null(Route(AirPos, CondenserPos));
            Assert.Null(Route(AirPos, BoilerPos));
            Assert.Null(Route(AirPos, PressTopPos));
        }

        [Fact]
        public void A_boiler_is_always_a_firepit_to_build()
        {
            Assert.IsType<InventoryToFirepitTransfer>(Route(ChestPos, BoilerPos));
        }

        [Fact]
        public void The_press_top_is_the_mash_both_ways()
        {
            Assert.IsType<InventoryToPressMashTransfer>(Route(ChestPos, PressTopPos));
            Assert.IsType<PressMashToInventoryTransfer>(Route(PressTopPos, ChestPos));
            Assert.Null(Route(PressTopPos, AirPos));
        }

        [Fact]
        public void The_press_bottom_is_a_bucket_stand()
        {
            Assert.IsType<InventoryToBucketSlotTransfer>(Route(ChestPos, PressPos));
            Assert.IsType<EndRefreshTransfer>(Route(PressPos, ChestPos));
        }

        [Fact]
        public void Two_chests_keep_the_transfer_they_always_had()
        {
            Assert.IsType<InventoryToInventoryTransfer>(Route(ChestPos, OtherChestPos));
            Assert.IsType<WorldToInventoryTransfer>(Route(AirPos, ChestPos));
        }

        [Fact]
        public void The_press_top_resolves_to_the_press_entity()
        {
            IBlockAccessor ba = World().BlockAccessor;
            Assert.Same(ba.GetBlockEntity(PressPos), AutomationAccess.EntityAt(ba, PressTopPos));
        }

        [Fact]
        public void The_context_offers_the_press_to_actions_from_either_end()
        {
            ICoreAPI api = Api();
            BlockEntity press = api.World.BlockAccessor.GetBlockEntity(PressPos);

            var asTarget = new Dictionary<string, object> { ["targetBlockPos"] = PressTopPos };
            ConditionContext.Complete(api, asTarget);
            Assert.Same(press, asTarget["targetBlockEntity"]);

            var asSource = new Dictionary<string, object> { ["sourceBlockPos"] = PressPos, ["targetBlockPos"] = ChestPos };
            ConditionContext.Complete(api, asSource);
            Assert.Same(press, asSource["sourceBlockEntity"]);
            Assert.False(asSource.ContainsKey("targetBlockEntity"));
        }

        // ---------------------------------------------------------------- plumbing

        static IItemTransfer Route(BlockPos from, BlockPos to)
            => ItemTransferFactory.CreateTransfer(Api(), from, to, 0, 0, new PaperConditionsEvaluator());

        static ICoreAPI Api()
        {
            IWorldAccessor world = World();
            return Proxy.Make<ICoreAPI>((m, a) => m.Name == "get_World" ? world : Proxy.Unhandled);
        }

        static IWorldAccessor World()
        {
            var entities = new Dictionary<BlockPos, BlockEntity>
            {
                [ChestPos] = new Chest(),
                [OtherChestPos] = new Chest(),
                [CondenserPos] = new BlockEntityCondenser(),
                [BoilerPos] = new BlockEntityBoiler(),
                [PressPos] = new BlockEntityFruitPress(),
            };

            var accessor = Proxy.Make<IBlockAccessor>((m, a) =>
            {
                if (m.Name == "GetBlockEntity" && a.Length == 1 && a[0] is BlockPos pos)
                {
                    return entities.TryGetValue(pos, out BlockEntity be) ? be : null;
                }
                if (m.Name == "GetBlock" && a.Length >= 1 && a[0] is BlockPos blockPos)
                {
                    return blockPos.Equals(PressTopPos) ? new BlockFruitPressTop() : new Block();
                }
                return Proxy.Unhandled;
            });

            return Proxy.Make<IWorldAccessor>((m, a) => m.Name == "get_BlockAccessor" ? accessor : Proxy.Unhandled);
        }

        class Chest : BlockEntity, IBlockEntityContainer
        {
            private readonly InventoryGeneric inv = new InventoryGeneric(4, "test-chest", null);
            public IInventory Inventory => inv;
            public string InventoryClassName => "chest";
            public void DropContents(Vec3d atPos) { }
            public void CheckInventoryClearedMidTick() { }
        }
    }
}
