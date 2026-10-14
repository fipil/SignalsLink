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

    [Fact]
    public void UnchangedLayersAreNotResentAndOnlyChangedLayerIsUpdated()
    {
        int sent = 0;
        var layers = new Dictionary<int, List<BlockPos>>();
        var be = Imprinter(Api(EnumAppSide.Server, new ImprinterHighlights(), layers, () => sent++), new BlockPos(1, 2, 3));
        Show(be);
        Assert.Equal(3, sent);
        Show(be);
        Assert.Equal(3, sent);
        var refused = (List<BlockPos>)Field("refused").GetValue(be);
        refused.Add(new BlockPos(9, 2, 3));
        Show(be);
        Assert.Equal(4, sent);
        refused.Reverse();
        Show(be);
        Assert.Equal(4, sent);
        refused.Clear();
        Show(be);
        Assert.Equal(5, sent);
        Clear(be);
        Assert.Equal(7, sent); // only two still-visible layers need clearing
        Clear(be);
        Assert.Equal(7, sent);
        Show(be);
        Assert.Equal(10, sent);
    }

    [Fact]
    public void ReconnectingSameUidReceivesFreshHighlights()
    {
        int sent = 0;
        var player = (ServerPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ServerPlayer));
        var be = Imprinter(Api(EnumAppSide.Server, new ImprinterHighlights(), new(), () => sent++, () => player), new BlockPos(1, 2, 3));
        Show(be);
        player = (ServerPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ServerPlayer));
        Show(be);
        Assert.Equal(6, sent);
        player = null;
        Show(be);
        Assert.Equal(6, sent);
        player = (ServerPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ServerPlayer));
        Show(be);
        Assert.Equal(9, sent);
    }

    private static ICoreAPI Api(EnumAppSide side, ImprinterHighlights registry, Dictionary<int, List<BlockPos>> layers, Action sent = null, Func<ServerPlayer> connected = null)
    {
        // Only the identity/type of this player is used; the fake world captures outgoing packets.
        var player = (ServerPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ServerPlayer));
        var world = Fake.Of<IWorldAccessor>((m, a) =>
        {
            if (m.Name == "PlayerByUid") return connected == null ? player : connected();
            if (m.Name == "HighlightBlocks") { sent?.Invoke(); layers[(int)a[1]] = new((List<BlockPos>)a[2]); return null; }
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
