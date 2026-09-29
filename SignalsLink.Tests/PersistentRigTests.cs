using System.Reflection;
using System.Text.Json;
using SignalsLink.Testing;
using SignalsLink.src.signals.link;
using signals.src;
using signals.src.hangingwires;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using static SignalsLink.Tests.AnchorLifecycleTests;

namespace SignalsLink.Tests;

public class PersistentRigTests
{
    [Fact]
    public void Ground_layout_has_no_platform_and_uses_both_special_chests()
    {
        var layout = new GroundLayout(new ArenaLocation(10,20,30,0,2));
        Assert.Equal(20,layout.Target.Y); Assert.Equal(20,layout.Switch.Y);
        Assert.All(layout.Ground(),p=>Assert.Equal(19,p.Y));
        Assert.All(layout.Ground(),p=>Assert.False(layout.Contains(p)));
        Assert.Equal("signalslink:bottomlesschest-east",layout.Code(layout.Source));
        Assert.Equal("signalslink:recyclerchest-east",layout.Code(layout.Target));
        Assert.Equal(6,layout.Cells().Count(p=>layout.Code(p)!=null));
    }
    [Fact]
    public void Enabled_state_counters_and_identity_survive_manifest_roundtrip()
    {
        var rig = new RigManifest { Id="chute-basic-7", Location=new ArenaLocation(10,20,30,0,2), Enabled=false,
            Initialized=true, Cycles=123, Failures=2, State="stopped", LastError="example" };
        var loaded=JsonSerializer.Deserialize<RigManifest>(JsonSerializer.Serialize(rig));
        Assert.Equal(rig.Id,loaded.Id); Assert.False(loaded.Enabled); Assert.True(loaded.Initialized);
        Assert.Equal(123,loaded.Cycles); Assert.Equal(2,loaded.Failures); Assert.Equal(rig.Location,loaded.Location);
    }
    [Fact]
    public void New_rig_refuses_an_existing_matching_chest_without_writes()
    {
        var f=new Fixture(); f.Place(f.Layout.Target,"signalslink:recyclerchest-east");
        Assert.Contains("occupied",f.Rig.Preflight()); Assert.Equal(0,f.Writes);
    }
    [Fact]
    public void New_rig_requires_ground_and_loaded_chunks()
    {
        var f=new Fixture { Ground=false };
        Assert.Contains("solid ground",f.Rig.Preflight());
        f.Ground=true; f.Loaded=false; Assert.Contains("not loaded",f.Rig.Preflight()); Assert.Equal(0,f.Writes);
    }
    [Fact]
    public void Explicit_cleanup_removes_only_known_devices_and_never_natural_ground()
    {
        var f=new Fixture(); f.Place(f.Layout.Target,"signalslink:recyclerchest-east");
        Assert.True(f.Rig.Cleanup()); Assert.Equal(1,f.Writes); Assert.True(f.Ground);
        Assert.All(f.Removed,p=>Assert.True(f.Layout.Contains(p)));
    }
    [Fact]
    public void Cleanup_preserves_modified_device_and_foreign_connections()
    {
        var f=new Fixture(); f.Place(f.Layout.Target,"game:anvil-iron");
        f.Place(f.Layout.Output,"signals:connection-down");
        f.Wires.data.connections.Add(new WireConnection(new signals.src.signalNetwork.NodePos(f.Layout.Output,0),
            new signals.src.signalNetwork.NodePos(f.Layout.At(30,0,0),0)));
        Assert.False(f.Rig.Cleanup()); Assert.Equal(0,f.Writes); Assert.Equal(2,f.Blocks.Count);
    }
    [Fact]
    public void Stopped_rig_does_not_build_or_remove_any_blocks()
    {
        var f=new Fixture(); f.Rig.Manifest.Enabled=false; f.Rig.Tick(.1);
        Assert.Equal(0,f.Writes); Assert.Equal("upgrade-required",f.Rig.Manifest.State); Assert.False(f.Rig.Manifest.Enabled);
    }
    [Fact]
    public void Current_layout_separates_normal_test_chests_from_supply_and_sink()
    {
        var layout=new GroundLayout(new ArenaLocation(0,10,0,0,3));
        Assert.Equal("game:chest-east",layout.Code(layout.Source));
        Assert.Equal("game:chest-east",layout.Code(layout.Target));
        Assert.Equal(layout.Supply.DownCopy(),layout.Filler);
        Assert.Equal(layout.Filler.DownCopy(),layout.Source);
        Assert.Equal(layout.Source.DownCopy(),layout.Chute);
        Assert.Equal(layout.Chute.DownCopy(),layout.Target);
        Assert.Equal(layout.Target.DownCopy(),layout.Drain);
        Assert.Equal(layout.Drain.DownCopy(),layout.Recycler);
        Assert.Equal(10,layout.Recycler.Y); Assert.Equal(7,layout.Height);
        Assert.Equal(12,layout.Devices.Distinct().Count());
        Assert.All(layout.Devices,p=>Assert.True(layout.Contains(p)));
    }
    [Fact]
    public void Upgrade_accepts_owned_legacy_blocks_but_rejects_foreign_or_expanded_obstructions()
    {
        var f=new Fixture(); f.Place(f.Layout.Target,"signalslink:recyclerchest-east");
        var replacement=new PersistentChuteRig(f.Api,new RigManifest{Id="chute-basic-1",Location=f.Layout.Location with {Version=3}},(_,_)=>{});
        // The fixture intentionally has no live signal network; reaching that check proves space was accepted.
        Assert.Contains("Signals network",replacement.Preflight(f.Rig));
        f.Place(f.Layout.Target,"game:anvil-iron"); Assert.Contains("occupied",replacement.Preflight(f.Rig));
        f.Place(f.Layout.Target,"signalslink:recyclerchest-east");
        f.Place(replacement.Layout.Supply,"game:rock-granite"); Assert.Contains("occupied",replacement.Preflight(f.Rig));
        Assert.Equal(0,f.Writes);
    }
    private sealed class Fixture
    {
        public ICoreServerAPI Api;
        public GroundLayout Layout;
        public PersistentChuteRig Rig;
        public HangingWiresMod Wires=new();
        public LinkNetworkMod Links=new();
        public bool Loaded=true,Ground=true;
        public int Writes;
        public List<BlockPos> Removed=new();
        public Dictionary<BlockPos,Block> Blocks=new();
        public Fixture()
        {
            Layout=new GroundLayout(new ArenaLocation(0,10,0,0,2));
            var chunk=Proxy.Make<IWorldChunk>((m,a)=>Proxy.Unhandled);
            object Handle(MethodInfo m,object[] a)
            {
                switch(m.Name)
                {
                    case "GetChunkAtBlockPos": return Loaded?chunk:null;
                    case "GetBlockEntity": return null;
                    case "GetBlock":
                        var p=(BlockPos)a[0];
                        if(a.Length>1 && (int)a[1]==BlockLayersAccess.Fluid) return new Block{Code=new AssetLocation("game:air")};
                        if(p.Y==9 && Ground) return new Block{BlockId=1,Code=new AssetLocation("game:rock-granite"),SideSolid=new SmallBoolArray(63)};
                        return Blocks.TryGetValue(p,out var b)?b:new Block{Code=new AssetLocation("game:air")};
                    case "SetBlock": Assert.Equal(0,(int)a[0]); Writes++; Removed.Add(((BlockPos)a[1]).Copy()); Blocks.Remove((BlockPos)a[1]); return null;
                    case "GetEntitiesAround": return Array.Empty<Entity>();
                    case "GetModSystem":
                        var t=m.GetGenericArguments()[0];
                        if(t==typeof(HangingWiresMod)) return Wires;
                        if(t==typeof(SignalNetworkMod)) return new SignalNetworkMod();
                        if(t==typeof(LinkNetworkMod)) return Links;
                        return null;
                    default:return Proxy.Unhandled;
                }
            }
            var api=Api=Proxy.Make<ICoreServerAPI>(Handle);
            Rig=new PersistentChuteRig(api,new RigManifest{Id="chute-basic-1",Location=Layout.Location},(_,_)=>{});
        }
        public void Place(BlockPos p,string code)=>Blocks[p.Copy()]=new Block{BlockId=1,Code=new AssetLocation(code)};
    }
}
