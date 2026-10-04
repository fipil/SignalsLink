using System.Reflection;
using SignalsMachines.src.craftingmachine;
using SignalsTubes.src.programtube;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SignalsMachines.Tests;

public class ProgramTubeTests
{
    private static readonly ItemProgramTube Tube = new() { Code = new AssetLocation("signalstubes:programtube"), MaxStackSize = 1 };
    private static ItemStack Program(string id = "sequence")
    {
        var stack = new ItemStack(Tube);
        stack.Attributes.SetString("programId", id);
        var program = new TreeAttribute();
        program.SetInt("delay", 12);
        program.SetString("name", "Test");
        stack.Attributes["program"] = program;
        return stack;
    }

    [Fact]
    public void InsertSaveLoadRemovePreservesProgramWithoutDuplicatingStack()
    {
        var hand = new DummySlot { Itemstack = Program() };
        string fingerprint = TubeVisuals.Fingerprint(hand.Itemstack);
        ItemStack returned = null;
        var inventory = Fake<IPlayerInventoryManager>((m, a) => m.Name switch {
            "get_ActiveHotbarSlot" => hand,
            "TryGiveItemstack" => Give((ItemStack)a[0]),
            _ => null
        });
        bool Give(ItemStack stack) { returned = stack; return true; }
        var player = PlayerFake.WithInventory(inventory);
        var world = World(EnumAppSide.Server);
        var api = MakeApi(world, EnumAppSide.Server);
        var machine = new TestMachine();
        machine.Initialize(api);
        Assert.True(machine.Interact(player));
        Assert.True(hand.Empty);
        Assert.True(machine.HasTube);
        var saved = new TreeAttribute();
        machine.ToTreeAttributes(saved);
        // Exercise the same binary representation used by world saves and packets.
        using var stream = new MemoryStream();
        saved.ToBytes(new BinaryWriter(stream));
        stream.Position = 0;
        var loaded = new TreeAttribute();
        loaded.FromBytes(new BinaryReader(stream));
        var restored = new TestMachine();
        restored.FromTreeAttributes(loaded, world);
        restored.Initialize(api);
        Assert.True(restored.Interact(player));
        Assert.False(restored.HasTube);
        Assert.Equal(fingerprint, TubeVisuals.Fingerprint(returned));
        Assert.Equal(1, returned.StackSize);
        Assert.False(restored.Interact(player));
    }

    [Fact]
    public void ClientInteractionDoesNotTransferItems()
    {
        var hand = new DummySlot { Itemstack = Program() };
        var inventory = Fake<IPlayerInventoryManager>((m, a) => m.Name == "get_ActiveHotbarSlot" ? hand : null);
        var player = PlayerFake.WithInventory(inventory);
        var world = World(EnumAppSide.Client);
        var api = MakeApi(world, EnumAppSide.Client);
        var machine = new TestMachine(); machine.Initialize(api);
        Assert.True(machine.Interact(player));
        Assert.False(hand.Empty);
        Assert.False(machine.HasTube);
    }

    [Fact]
    public void UpperProxyCannotOperateSocket()
    {
        var block = new BlockCraftingMachine();
        var accessor = Fake<IBlockAccessor>((m, a) => null);   // no machine entity below
        var world = Fake<IWorldAccessor>((m, a) => m.Name == "get_BlockAccessor" ? accessor : null);
        var selection = new BlockSelection { Position = new BlockPos(1, 3, 3), SelectionBoxIndex = BlockCraftingMachine.SocketBox };
        Assert.False(block.MBOnBlockInteractStart(world, null, selection, new Vec3i(0, -1, 0)));
    }

    [Theory]
    [InlineData(EnumAppSide.Client, true)]
    [InlineData(EnumAppSide.Server, true)]
    [InlineData(EnumAppSide.Server, false)]
    public void PreviouslyPlacedMachineCanAcquireSocketStateWithoutClientMutation(EnumAppSide side, bool allowed)
    {
        var hand = new DummySlot { Itemstack = Program() };
        var inventory = Fake<IPlayerInventoryManager>((m, a) => m.Name == "get_ActiveHotbarSlot" ? hand : null);
        var player = PlayerFake.WithInventory(inventory);
        var claims = Fake<ILandClaimAPI>((m, a) => m.Name == "TryAccess" && allowed);
        TestMachine entity = null;
        ICoreAPI api = null;
        int spawned = 0, clicks = 0;
        var accessor = Fake<IBlockAccessor>((m, a) => {
            if (m.Name == "GetBlockEntity") return entity;
            if (m.Name == "SpawnBlockEntity") {
                Assert.Equal("CraftingMachine", a[0]);
                spawned++;
                entity = new TestMachine(); entity.Initialize(api);
            }
            return null;
        });
        var world = Fake<IWorldAccessor>((m, a) => {
            if (m.Name == "PlaySoundAt") clicks++;
            return m.Name switch { "get_Side" => side, "get_BlockAccessor" => accessor, "get_Claims" => claims, _ => null };
        });
        api = MakeApi(world, side);
        var block = new BlockCraftingMachine { EntityClass = "CraftingMachine" };
        bool result = block.OnBlockInteractStart(world, player, new BlockSelection { Position = new BlockPos(1,2,3), SelectionBoxIndex = BlockCraftingMachine.SocketBox });
        Assert.Equal(allowed, result);
        bool transfer = allowed && side == EnumAppSide.Server;
        Assert.Equal(transfer ? 1 : 0, spawned);
        Assert.Equal(transfer ? 1 : 0, clicks);
        Assert.Equal(transfer, hand.Empty);
        Assert.Equal(transfer, entity?.HasTube == true);
    }

    // Enough of the game API for a container block entity to initialize: events, class registry and
    // a mod loader that knows no systems.
    private static ICoreAPI MakeApi(IWorldAccessor world, EnumAppSide side)
    {
        var events = Fake<IEventAPI>((m, a) => null);
        var classes = Fake<IClassRegistryAPI>((m, a) => null);
        var mods = Fake<IModLoader>((m, a) => null);
        return Fake<ICoreAPI>((m, a) => m.Name switch { "get_World" => world, "get_Side" => side, "get_Event" => events, "get_ClassRegistry" => classes, "get_ModLoader" => mods, _ => null });
    }

    private static IWorldAccessor World(EnumAppSide side) => Fake<IWorldAccessor>((m, a) => m.Name switch { "get_Side" => side, "GetItem" => Tube, _ => null });
    private static T Fake<T>(System.Func<MethodInfo, object[], object> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, Proxy>();
        ((Proxy)(object)proxy).Call = call;
        return proxy;
    }
    public class Proxy : DispatchProxy
    {
        public System.Func<MethodInfo, object[], object> Call;
        protected override object Invoke(MethodInfo method, object[] args) => Call(method, args) ?? (method.ReturnType.IsValueType && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null);
    }
    private class DummySlot : ItemSlot
    {
        public DummySlot() : base(null) { }
        public override void MarkDirty() { }
    }
    private class TestMachine : BECraftingMachine
    {
        public TestMachine() { Block = new Block { Code = new AssetLocation("signalsmachines:craftingmachine-north") }; Pos = new BlockPos(1, 2, 3); }
        public override void MarkDirty(bool redrawOnClient = false, IPlayer skipPlayer = null) { }
    }
}



