using System.Reflection;
using SignalsLink.src.signals.managedchute.transporting;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>
    /// Devices reaching a multiblock part find the controller's inventory, and a block entity that
    /// shuts some of its faces keeps chutes out on those faces only.
    /// </summary>
    public class AutomationAccessTests
    {
        private sealed class Part : Block, IMultiblockOffset
        {
            public BlockPos Control;
            public BlockPos GetControlBlockPos(BlockPos pos) => Control;
        }

        private sealed class PlainEntity : BlockEntity { }

        private sealed class DoorKeeper : BlockEntity, ISidedAutomation
        {
            public BlockFacing Open;
            public int OpenY = int.MinValue;   // only this block of the structure has the door; any when unset
            public bool AllowsAutomation(BlockPos touched, BlockFacing face) => face == Open && (OpenY == int.MinValue || touched.Y == OpenY);
        }

        private static IBlockAccessor World(Dictionary<BlockPos, BlockEntity> entities, Dictionary<BlockPos, Block> blocks)
        {
            var proxy = DispatchProxy.Create<IBlockAccessor, Accessor>();
            ((Accessor)(object)proxy).Entities = entities;
            ((Accessor)(object)proxy).Blocks = blocks;
            return proxy;
        }

        public class Accessor : DispatchProxy
        {
            public Dictionary<BlockPos, BlockEntity> Entities;
            public Dictionary<BlockPos, Block> Blocks;
            protected override object Invoke(MethodInfo method, object[] args) => method.Name switch
            {
                "GetBlockEntity" when args[0] is BlockPos p => Entities.TryGetValue(p, out var be) ? be : null,
                "GetBlock" when args[0] is BlockPos p => Blocks.TryGetValue(p, out var b) ? b : new Block(),
                _ => method.ReturnType.IsValueType && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null
            };
        }

        [Fact]
        public void A_multiblock_part_answers_with_the_controllers_entity()
        {
            var control = new BlockPos(10, 5, 10);
            var part = new BlockPos(10, 6, 10);
            var keeper = new DoorKeeper { Open = BlockFacing.EAST };
            var ba = World(new() { [control] = keeper }, new() { [part] = new Part { Control = control } });

            Assert.Equal(control, AutomationAccess.ControlPos(ba, part));
            Assert.Same(keeper, AutomationAccess.EntityAt(ba, part));
            Assert.Equal(control, AutomationAccess.ControlPos(ba, control));   // the controller itself stays put
            Assert.Null(AutomationAccess.EntityAt(ba, new BlockPos(0, 0, 0)));  // plain air: nothing, no exception
        }

        [Fact]
        public void A_sided_entity_lets_a_device_in_through_its_open_face_only()
        {
            var control = new BlockPos(10, 5, 10);
            var part = new BlockPos(10, 6, 10);
            var keeper = new DoorKeeper { Open = BlockFacing.EAST, OpenY = part.Y };
            var ba = World(new() { [control] = keeper }, new() { [part] = new Part { Control = control } });

            Assert.True(AutomationAccess.Allows(ba, part, part.EastCopy()));     // chute east of the upper part
            Assert.False(AutomationAccess.Allows(ba, part, part.WestCopy()));
            Assert.False(AutomationAccess.Allows(ba, control, control.EastCopy()));   // the lower block has no door, whatever the face
        }

        [Fact]
        public void Ordinary_containers_are_open_from_every_side()
        {
            var pos = new BlockPos(1, 1, 1);
            var ba = World(new() { [pos] = new PlainEntity() }, new());
            foreach (var face in BlockFacing.ALLFACES)
                Assert.True(AutomationAccess.Allows(ba, pos, pos.AddCopy(face)));
        }

        [Fact]
        public void The_face_asked_about_is_the_one_looking_at_the_device()
        {
            var pos = new BlockPos(5, 5, 5);
            Assert.Equal(BlockFacing.UP, AutomationAccess.FaceTowards(pos, pos.UpCopy()));
            Assert.Equal(BlockFacing.NORTH, AutomationAccess.FaceTowards(pos, pos.NorthCopy()));
            Assert.Equal(BlockFacing.WEST, AutomationAccess.FaceTowards(pos, pos.WestCopy()));
        }
    }
}
