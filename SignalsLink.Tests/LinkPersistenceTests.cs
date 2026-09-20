using System.Reflection;
using signals.src.signalNetwork;
using SignalsLink.src.signals.link;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using static SignalsLink.Tests.AnchorLifecycleTests;

namespace SignalsLink.Tests;

public class LinkPersistenceTests
{
    [Theory]
    [InlineData(LinkKind.Sleeve)]
    [InlineData(LinkKind.Hose)]
    public void Exit_before_first_autosave_preserves_new_connection(byte kind)
    {
        var host = new Host();
        var mod = host.Start();
        var link = Connection(kind);
        mod.data.connections.Add(link);
        // Real VS shutdown: dispose mods, then invoke the final world save.
        mod.Dispose();
        host.Raise("GameWorldSave");
        var restored = host.Start();
        Assert.Equal(kind, Assert.Single(restored.data.connections).kind);
        Assert.Contains(link, restored.data.connections);
        restored.Dispose();
    }

    [Fact]
    public void Removal_since_autosave_does_not_reappear_after_exit()
    {
        var host = new Host(); var mod = host.Start(); var link = Connection(LinkKind.Sleeve);
        mod.data.connections.Add(link); host.Raise("GameWorldSave");
        Assert.True(mod.TryToRemoveConnection(link.pos1, link.pos2));
        mod.Dispose(); host.Raise("GameWorldSave");
        var restored = host.Start(); Assert.Empty(restored.data.connections); restored.Dispose();
    }

    [Fact]
    public void Disposal_before_world_load_does_not_overwrite_existing_save()
    {
        var host = new Host(); host.Data[LinkNetworkMod.SaveKey] = new byte[] { 1, 2, 3 };
        var mod = host.Start(load: false); mod.Dispose();
        Assert.Equal(new byte[] { 1, 2, 3 }, host.Data[LinkNetworkMod.SaveKey]);
    }

    static LinkConnection Connection(byte kind) => new(new NodePos(new BlockPos(10, 20, 30), 0), new NodePos(new BlockPos(15, 20, 30), 1), kind);
    sealed class Host
    {
        public readonly Dictionary<string, byte[]> Data = new();
        readonly Dictionary<string, Delegate> events = new();
        readonly ICoreServerAPI api;
        public Host()
        {
            api = Proxy.Make<ICoreServerAPI>((m, a) =>
            {
                if (m.Name.StartsWith("add_")) { string name = m.Name[4..]; events.TryGetValue(name, out var previous); events[name] = Delegate.Combine(previous, (Delegate)a[0]); return null; }
                if (m.Name.StartsWith("remove_")) { string name = m.Name[7..]; events.TryGetValue(name, out var previous); events[name] = Delegate.Remove(previous, (Delegate)a[0]); return null; }
                if (m.Name == "GetData") return Data.GetValueOrDefault((string)a[0]);
                if (m.Name == "StoreData") { Data[(string)a[0]] = (byte[])a[1]; return null; }
                return Proxy.Unhandled;
            });
        }
        public LinkNetworkMod Start(bool load = true) { var mod = new LinkNetworkMod(); mod.StartServerSide(api); if (load) Raise("SaveGameLoaded"); return mod; }
        public void Raise(string name) { if (events.TryGetValue(name, out var callback)) callback?.DynamicInvoke(); }
    }
}
