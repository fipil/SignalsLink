using System.Reflection;
using Newtonsoft.Json.Linq;
using SignalsTubes.src.imprint;
using SignalsTubes.Tests.fidelity;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SignalsTubes.Tests;

public class ImprinterRefreshTests
{
    private sealed class Player : Vintagestory.Server.ServerPlayer, IServerPlayer, IPlayer
    {
        public Player() : base(null, null) { }
        protected override void Init() { }
        public new EntityPlayer Entity { get; } = new EntityPlayer();
    }
    private static FieldInfo Field(string name) => typeof(BEImprinter).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);

    [Fact]
    public void IdleImprinterSkipsReadsAndReturningOperatorRefreshesNetwork()
    {
        var rig = new SignalsRig();
        var socket = rig.Socket(SignalsRig.At(0));
        var player = new Player();
        bool online = true;
        int blockReads = 0;
        var accessor = Fake.Of<IBlockAccessor>((m, a) => {
            if (m.Name == "GetBlock") blockReads++;
            return m.Invoke(rig.World.BlockAccessor, a);
        });
        var world = Fake.Of<IWorldAccessor>((m, a) => m.Name switch {
            "PlayerByUid" => online ? player : null,
            "get_BlockAccessor" => accessor,
            _ => m.Invoke(rig.World, a)
        });
        var registry = new ImprinterHighlights();
        var loader = Fake.Of<IModLoader>((m, a) => m.Name == "GetModSystem" && m.IsGenericMethod && m.GetGenericArguments()[0] == typeof(ImprinterHighlights)
            ? registry : m.Invoke(rig.Api.ModLoader, a));
        var api = Fake.Of<ICoreAPI>((m, a) => m.Name switch {
            "get_World" => world, "get_ModLoader" => loader, _ => m.Invoke(rig.Api, a)
        });
        var be = new BEImprinter { Api = api, Pos = SignalsRig.At(2), Block = new BlockImprinter() };
        Field("socket").SetValue(be, socket);
        Field("operatorUid").SetValue(be, "player");
        var draft = new JObject();
        Field("draft").SetValue(be, draft);
        void Tick() => typeof(BEImprinter).GetMethod("ServerTick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(be, new object[] { 1f });
        player.Entity.Pos.X = 100;
        Tick();
        Assert.Equal(0, blockReads);
        Assert.Same(draft, Field("draft").GetValue(be));
        player.Entity.Pos.X = 2;
        player.Entity.Pos.Dimension = 1;
        Tick();
        Assert.Same(draft, Field("draft").GetValue(be));
        player.Entity.Pos.Dimension = 0;
        online = false;
        Tick();
        Assert.Same(draft, Field("draft").GetValue(be));
        online = true;
        Tick();
        Assert.Null(Field("draft").GetValue(be)); // refreshed fingerprint invalidates the stale draft
        Assert.Equal(socket, be.Socket); // idle checks did not disconnect the plug
    }
}
