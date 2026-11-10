using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SignalsLink.src.signals.managedchute.transporting;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Proxy = SignalsLink.Tests.AnchorLifecycleTests.Proxy;

namespace SignalsLink.Tests;

/// <summary>
/// Ends another mod registers: the factory and the resolver read and write them through their
/// view, the device tells the provider where it stands, and a broken provider costs nothing but
/// its own ends. With no registry at all, nothing here is ever consulted.
/// </summary>
public class TransferEndRegistryTests
{
    static readonly BlockPos ChestPos = new BlockPos(0, 0, 0, 0);
    static readonly BlockPos OtherChestPos = new BlockPos(9, 0, 0, 0);
    static readonly BlockPos MachinePos = new BlockPos(1, 0, 0, 0);
    static readonly BlockPos AirPos = new BlockPos(4, 0, 0, 0);
    static readonly BlockPos DevicePos = new BlockPos(1, 0, 1, 0);

    static PaperConditionsEvaluator Eval(string paper = "*") { var e = new PaperConditionsEvaluator(); e.SetConditionsText(paper); return e; }
    static ItemStackMoveOperation Op(int n = 1) => new(null, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, n);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Removing_a_bucket_notifies_both_ends_only_after_a_move(bool blocked)
    {
        var condenser = new CountingCondenser();
        var bucket = new BlockLiquidContainerTopOpened { Code = new AssetLocation("game:woodbucket") };
        condenser.Inventory[1].Itemstack = new ItemStack(bucket);
        var target = new FakeEnd(MachinePos);
        if (blocked) target.Inventory[0].Itemstack = TestStacks.Item("game:stone");

        var api = Api(Registry(new FakeProvider(target)), Chest(), condenser);
        var transfer = ItemTransferFactory.CreateTransfer(api, ChestPos, MachinePos, 0, 1, Eval(""));
        var result = transfer.TryMove(Op());

        Assert.Equal(!blocked, result.Success);
        Assert.Equal(!blocked, condenser.Inventory[1].Empty);
        Assert.Equal(blocked ? 0 : 1, condenser.Redraws);
        Assert.Equal(blocked ? 0 : 1, target.ChangedCount);
        if (!blocked) Assert.Same(bucket, target.Inventory[0].Itemstack.Collectible);
    }

    sealed class CountingCondenser : BlockEntityCondenser
    {
        public override InventoryBase Inventory { get; } = new QuietInventory(2);
        public int Redraws;
        public override void MarkDirty(bool redrawOnClient = false, IPlayer skipPlayer = null)
        {
            if (redrawOnClient) Redraws++;
        }
    }

    [Fact]
    public void A_chest_hands_to_a_registered_end_through_its_view()
    {
        var end = new FakeEnd(MachinePos);
        var registry = Registry(new FakeProvider(end));
        var chest = Chest(TestStacks.Item("game:log-oak", 4));

        IItemTransfer transfer = ItemTransferFactory.CreateTransfer(Api(registry, chest), ChestPos, MachinePos, 0, 0, Eval(), null, DevicePos);

        var wrapped = Assert.IsType<EndRefreshTransfer>(transfer);
        Assert.IsType<InventoryToInventoryTransfer>(wrapped.Inner);
        Assert.Equal(1, transfer.TryMove(Op()).MovedAmount);
        Assert.Equal(1, end.Inventory[0].StackSize);
        Assert.Equal(3, chest[0].StackSize);
        Assert.Equal(1, end.ChangedCount);
    }

    [Fact]
    public void A_registered_end_hands_to_a_chest_and_is_told_afterwards()
    {
        var end = new FakeEnd(MachinePos);
        end.Inventory[0].Itemstack = TestStacks.Item("game:firewood", 5);
        var registry = Registry(new FakeProvider(end));
        var chest = Chest();

        IItemTransfer transfer = ItemTransferFactory.CreateTransfer(Api(registry, chest), MachinePos, ChestPos, 0, 0, Eval(), null, DevicePos);

        var wrapped = Assert.IsType<EndRefreshTransfer>(transfer);
        Assert.IsType<InventoryToInventoryTransfer>(wrapped.Inner);
        Assert.Equal(1, transfer.TryMove(Op()).MovedAmount);
        Assert.Equal(1, chest[0].StackSize);
        Assert.Equal(1, end.ChangedCount);
    }

    [Fact]
    public void The_world_cannot_hand_anything_to_a_registered_end()
    {
        var registry = Registry(new FakeProvider(new FakeEnd(MachinePos)));
        Assert.Null(ItemTransferFactory.CreateTransfer(Api(registry, Chest()), AirPos, MachinePos, 0, 0, Eval(), null, DevicePos));
    }

    [Fact]
    public void The_provider_learns_where_the_device_stands()
    {
        var provider = new FakeProvider(new FakeEnd(MachinePos));
        var registry = Registry(provider);

        ItemTransferFactory.CreateTransfer(Api(registry, Chest()), ChestPos, MachinePos, 0, 0, Eval(), null, DevicePos);

        Assert.Equal(DevicePos, provider.LastDevicePos);
    }

    [Fact]
    public void The_resolver_reads_a_registered_end_through_its_view()
    {
        var end = new FakeEnd(MachinePos);
        var registry = Registry(new FakeProvider(end));

        Assert.Same(end.Inventory, TargetInventoryResolver.Resolve(Api(registry, Chest()), MachinePos, DevicePos));
        Assert.Same(end.Inventory, TargetInventoryResolver.Resolve(Api(registry, Chest()), MachinePos));
    }

    [Fact]
    public void Other_blocks_keep_the_transfer_they_always_had()
    {
        var registry = Registry(new FakeProvider(new FakeEnd(MachinePos)));
        var chest = Chest(TestStacks.Item("game:firewood", 5));

        Assert.IsType<InventoryToInventoryTransfer>(ItemTransferFactory.CreateTransfer(Api(registry, chest), ChestPos, OtherChestPos, 0, 0, Eval(), null, DevicePos));
        Assert.IsType<WorldToInventoryTransfer>(ItemTransferFactory.CreateTransfer(Api(registry, chest), AirPos, ChestPos, 0, 0, Eval(), null, DevicePos));
    }

    [Fact]
    public void A_broken_provider_is_ignored_and_the_rest_still_works()
    {
        var end = new FakeEnd(MachinePos);
        var registry = Registry(new ThrowingProvider(), new FakeProvider(end));
        var chest = Chest(TestStacks.Item("game:firewood", 5));

        Assert.IsType<EndRefreshTransfer>(ItemTransferFactory.CreateTransfer(Api(registry, chest), ChestPos, MachinePos, 0, 0, Eval(), null, DevicePos));
        Assert.IsType<InventoryToInventoryTransfer>(ItemTransferFactory.CreateTransfer(Api(registry, chest), ChestPos, OtherChestPos, 0, 0, Eval(), null, DevicePos));
    }

    [Fact]
    public void Without_a_registry_nothing_changes()
    {
        var chest = Chest(TestStacks.Item("game:firewood", 5));
        Assert.IsType<InventoryToInventoryTransfer>(ItemTransferFactory.CreateTransfer(Api(null, chest), ChestPos, OtherChestPos, 0, 0, Eval(), null, DevicePos));
        Assert.Null(ItemTransferFactory.CreateTransfer(Api(null, chest), ChestPos, MachinePos, 0, 0, Eval(), null, DevicePos));
    }

    [Fact]
    public void A_provider_is_registered_once_and_null_not_at_all()
    {
        var registry = new TransferEndRegistry();
        var provider = new FakeProvider(new FakeEnd(MachinePos));
        registry.Register(provider);
        registry.Register(provider);
        registry.Register(null);
        Assert.Single(registry.Providers);
    }

    // ---------------------------------------------------------------- plumbing

    static TransferEndRegistry Registry(params ITransferEndProvider[] providers)
    {
        var registry = new TransferEndRegistry();
        foreach (var p in providers) registry.Register(p);
        return registry;
    }

    static QuietInventory Chest(params ItemStack[] stacks)
    {
        var inv = new QuietInventory(4);
        for (int i = 0; i < stacks.Length; i++) inv[i].Itemstack = stacks[i];
        return inv;
    }

    /// <summary>Two chests and an air block; the machine position holds no block entity of its own.</summary>
    static ICoreAPI Api(TransferEndRegistry registry, QuietInventory chest, BlockEntity source = null)
    {
        var entities = new Dictionary<BlockPos, BlockEntity>
        {
            [ChestPos] = source ?? new ChestEntity(chest),
            [OtherChestPos] = new ChestEntity(new QuietInventory(4)),
        };

        var accessor = Proxy.Make<IBlockAccessor>((m, a) =>
        {
            if (m.Name == "GetBlockEntity" && a.Length == 1 && a[0] is BlockPos pos) return entities.TryGetValue(pos, out BlockEntity be) ? be : null;
            if (m.Name == "GetBlock" && a.Length >= 1) return new Block();
            return Proxy.Unhandled;
        });
        var world = Proxy.Make<IWorldAccessor>((m, a) => m.Name == "get_BlockAccessor" ? accessor : Proxy.Unhandled);
        var loader = Proxy.Make<IModLoader>((m, a) =>
            m.Name == "GetModSystem" && m.IsGenericMethod && m.GetGenericArguments()[0] == typeof(TransferEndRegistry) ? registry : Proxy.Unhandled);

        return Proxy.Make<ICoreAPI>((m, a) => m.Name == "get_World" ? world : m.Name == "get_ModLoader" ? loader : Proxy.Unhandled);
    }

    sealed class FakeEnd : ITransferEnd
    {
        public FakeEnd(BlockPos pos) { Pos = pos; }
        public BlockPos Pos { get; }
        public QuietInventory Inventory { get; } = new QuietInventory(2);
        public IInventory View => Inventory;
        public int ChangedCount;
        public void Changed(ICoreAPI api) => ChangedCount++;
    }

    sealed class FakeProvider : ITransferEndProvider
    {
        private readonly ITransferEnd end;
        public BlockPos LastDevicePos;
        public FakeProvider(ITransferEnd end) { this.end = end; }
        public ITransferEnd At(ICoreAPI api, BlockPos pos, BlockPos devicePos)
        {
            if (!pos.Equals(end.Pos)) return null;
            LastDevicePos = devicePos;
            return end;
        }
    }

    sealed class ThrowingProvider : ITransferEndProvider
    {
        public ITransferEnd At(ICoreAPI api, BlockPos pos, BlockPos devicePos) => throw new InvalidOperationException("broken on purpose");
    }

    sealed class ChestEntity : BlockEntity, IBlockEntityContainer
    {
        private readonly InventoryGeneric inv;
        public ChestEntity(InventoryGeneric inv) { this.inv = inv; }
        public IInventory Inventory => inv;
        public string InventoryClassName => "chest";
        public void DropContents(Vec3d atPos) { }
        public void CheckInventoryClearedMidTick() { }
    }

    internal class QuietInventory : InventoryGeneric
    {
        public QuietInventory(int n) : base(n, "test-quiet", null, (i, inv) => new QuietSlot(inv)) { }
        public override void DidModifyItemSlot(ItemSlot s, ItemStack extracted = null) { }
    }

    internal class QuietSlot : ItemSlot
    {
        public QuietSlot(InventoryBase inv) : base(inv) { }
        public override void OnItemSlotModified(ItemStack s) { }
        public override void MarkDirty() { }
    }
}
