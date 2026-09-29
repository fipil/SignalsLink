using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using SignalsLink.src.signals.behaviours;
using SignalsLink.src.signals.chunkanchor;
using SignalsLink.src.signals.entitysensor;
using SignalsLink.src.signals.igniter;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace SignalsLink.Tests;

// Guard against Signals changing what BlockConnection.OnBlockInteractStart returns. A block that
// is charged with a temporal gear must claim the click itself on the client; if it falls through
// to the base class and that returns false, the gear starts its own "set spawn" action instead.
// Seen on the entity sensor after Signals 0.3.x stopped returning true by default.
public class GearClickTests
{
    // JSON block class -> C# class, plus the block entity the click handler needs, if any.
    private static readonly Dictionary<string, (Type block, Func<BlockEntity> entity)> Charged = new()
    {
        ["EntitySensor"] = (typeof(EntitySensor), () => null),
        ["Igniter"] = (typeof(BlockIgniter), () => null),
        ["ChunkAnchor"] = (typeof(BlockChunkAnchor), () => new BEChunkAnchor()),
    };

    public static IEnumerable<object[]> ChargedClasses() => Charged.Keys.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(ChargedClasses))]
    public void A_gear_click_is_claimed_on_the_client(string jsonClass)
    {
        var (blockType, entity) = Charged[jsonClass];
        Block block = (Block)Activator.CreateInstance(blockType);
        block.Code = new AssetLocation("signalslink", jsonClass.ToLowerInvariant());
        var charge = new BlockBehaviorTemporalCharge(block);
        block.BlockBehaviors = new BlockBehavior[] { charge };
        block.CollectibleBehaviors = new CollectibleBehavior[] { charge }; // GetBehavior<T> looks here

        var world = DispatchProxy.Create<IWorldAccessor, ClientWorldProxy>();
        var accessor = DispatchProxy.Create<IBlockAccessor, EntityAccessorProxy>();
        ((ClientWorldProxy)(object)world).Accessor = accessor;
        ((EntityAccessorProxy)(object)accessor).Entity = entity();

        IPlayer player = PlayerFake.Holding(new ItemStack(new Item { Code = new AssetLocation("game", "gear-temporal") }));

        var selection = new BlockSelection
        {
            Position = new BlockPos(0, 0, 0, 0),
            Face = BlockFacing.UP,
            HitPosition = new Vec3d(0.5, 1, 0.5),
        };

        Assert.True(block.OnBlockInteractStart(world, player, selection));
    }

    // A new gear-charged block must be added to the table above, so it gets the guard too.
    [Fact]
    public void Every_gear_charged_block_is_covered()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SignalsLink.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        string blocktypes = Path.Combine(dir.FullName, "SignalsLink", "assets", "signalslink", "blocktypes");

        var charged = Directory.GetFiles(blocktypes, "*.json")
            .Where(f => File.ReadAllText(f).Contains("BlockBehaviorTemporalCharge"))
            .Select(f => (string)JObject.Parse(File.ReadAllText(f))["class"])
            .OrderBy(c => c)
            .ToArray();

        Assert.Equal(Charged.Keys.OrderBy(c => c).ToArray(), charged);
    }

    public class ClientWorldProxy : DispatchProxy
    {
        public IBlockAccessor Accessor;
        protected override object Invoke(MethodInfo method, object[] args) => method.Name switch
        {
            "get_Side" => EnumAppSide.Client,
            "get_BlockAccessor" => Accessor,
            _ => throw new NotSupportedException(method.Name)
        };
    }

    public class EntityAccessorProxy : DispatchProxy
    {
        public BlockEntity Entity;
        protected override object Invoke(MethodInfo method, object[] args) =>
            method.Name == "GetBlockEntity" ? Entity : throw new NotSupportedException(method.Name);
    }

}
