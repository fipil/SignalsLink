using System.Reflection;
using System.Runtime.CompilerServices;
using SignalsTubes.src.imprint;
using SignalsTubes.Tests.fidelity;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Server;

namespace SignalsTubes.Tests;

public class ImprinterHighlightsTests
{
    [Fact]
    public void AnotherImprintersRefreshAndCleanupCannotEraseExistingHighlights()
    {
        var layers = new Dictionary<int, List<BlockPos>>();
        var registry = new ImprinterHighlights();
        var api = Api(EnumAppSide.Server, registry, layers);
        var first = Imprinter(api, new BlockPos(1, 2, 3));
        var second = Imprinter(api, new BlockPos(5, 2, 3));
        Show(first);
        Assert.Equal(3, layers.Values.Count(p => p.Count == 1));
        Show(second);
        Assert.Equal(6, layers.Values.Count(p => p.Count == 1));
        Clear(second);
        Assert.Equal(3, layers.Values.Count(p => p.Count == 1));
        Assert.All(layers.Values.Where(p => p.Count > 0), p => Assert.Equal(new BlockPos(1, 2, 3), p[0]));
        Show(second);
        Show(first);
        Assert.Equal(6, layers.Values.Count(p => p.Count == 1));
    }

    [Fact]
    public void ClientInstanceCannotSendOrClearServerHighlightsInSameProcess()
    {
        var layers = new Dictionary<int, List<BlockPos>>();
        var registry = new ImprinterHighlights();
        Show(Imprinter(Api(EnumAppSide.Server, registry, layers), new BlockPos(1, 2, 3)));
        var client = Imprinter(Api(EnumAppSide.Client, registry, layers), new BlockPos(1, 2, 3));
        Show(client);
        Clear(client);
        Assert.Equal(3, layers.Count);
        Assert.All(layers.Values, p => Assert.Single(p));
    }

    private static ICoreAPI Api(EnumAppSide side, ImprinterHighlights registry, Dictionary<int, List<BlockPos>> layers)
    {
        // Only the identity/type of this player is used; the fake world captures outgoing packets.
        var player = (ServerPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ServerPlayer));
        var world = Fake.Of<IWorldAccessor>((m, a) =>
        {
            if (m.Name == "PlayerByUid") return player;
            if (m.Name == "HighlightBlocks") { layers[(int)a[1]] = new((List<BlockPos>)a[2]); return null; }
            return Fake.Unhandled;
        });
        var loader = Fake.Of<IModLoader>((m, a) => m.Name == "GetModSystem" ? registry : Fake.Unhandled);
        return Fake.Of<ICoreAPI>((m, a) => m.Name switch
        {
            "get_Side" => side, "get_World" => world, "get_ModLoader" => loader, _ => Fake.Unhandled
        });
    }

    private static BEImprinter Imprinter(ICoreAPI api, BlockPos marked)
    {
        var be = new BEImprinter { Api = api };
        Field("operatorUid").SetValue(be, "player");
        Field("socket").SetValue(be, new BlockPos(0, 0, 0));
        ((List<BlockPos>)Field("refused").GetValue(be)).Add(marked);
        ((HashSet<BlockPos>)Field("exposed").GetValue(be)).Add(marked);
        ((HashSet<BlockPos>)Field("excluded").GetValue(be)).Add(marked);
        return be;
    }
    private static FieldInfo Field(string name) => typeof(BEImprinter).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
    private static void Show(BEImprinter be) => typeof(BEImprinter).GetMethod("ShowHighlights", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(be, null);
    private static void Clear(BEImprinter be) => typeof(BEImprinter).GetMethod("ClearHighlights", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(be, null);
}
