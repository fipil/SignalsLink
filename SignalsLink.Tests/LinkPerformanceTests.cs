using System.Reflection;
using ProtoBuf;
using signals.src.signalNetwork;
using SignalsLink.src.signals.link;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using static SignalsLink.Tests.AnchorLifecycleTests;
namespace SignalsLink.Tests;

public class LinkPerformanceTests
{
    static NodePos Port(int x,int i=0,int dimension=0)=>new(new BlockPos(x,10,0,dimension),i);
    static LinkConnection Link(int x,int y,byte kind=0)=>new(Port(x),Port(y),kind);
    static object Call(object o,string method,params object[] args)=>o.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,args);
    static void Set(object o,string field,object value)=>o.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(o,value);
    [ProtoContract(ImplicitFields=ImplicitFields.AllPublic)]
    public class OldData { public HashSet<LinkConnection> connections=new(); }
    [Fact]
    public void Coalesced_remove_then_add_preserves_replacement_kind_on_client()
    {
        var packets = new List<LinkNetworkDelta>();
        var mod = new LinkNetworkMod();
        Set(mod, "serverChannel", Proxy.Make<IServerNetworkChannel>((m, a) => {
            if (m.Name == "BroadcastPacket") { packets.Add((LinkNetworkDelta)a[0]); return null; }
            return Proxy.Unhandled;
        }));
        var old = Link(0, 5, LinkKind.Hose);
        var replacement = Link(0, 5, LinkKind.Sleeve);
        var client = new LinkNetworkData(); client.connections.Add(old);
        mod.data.connections.Add(old);
        mod.TryToRemoveConnection(old.pos1, old.pos2);
        mod.data.connections.Add(replacement); Call(mod, "Changed", replacement, true);
        Call(mod, "FlushChanges");
        var wire = SerializerUtil.Deserialize<LinkNetworkDelta>(SerializerUtil.Serialize(Assert.Single(packets)));
        Assert.True(wire.Apply(client));
        Assert.Equal(LinkKind.Sleeve, Assert.Single(client.connections).kind);
    }

    [Fact]
    public void Snapshot_kind_replacement_rebuilds_mesh_and_releases_old_gpu_buffer()
    {
        using var f = new RenderFixture();
        f.Mod.data.connections.Add(Link(0, 5)); f.Renderer.RequestFullRebuild(); f.Drain();
        var old = Assert.Single(f.Meshes);
        f.Mod.data = new LinkNetworkData(); f.Mod.data.connections.Add(Link(0, 5, LinkKind.Sleeve));
        f.Renderer.RequestFullRebuild(); f.Drain();
        Assert.True(old.Disposed); Assert.Single(f.Meshes, m => !m.Disposed);
    }

    [Fact]
    public void Chunk_load_in_another_dimension_restores_a_previously_unloaded_link()
    {
        using var f = new RenderFixture();
        f.Player.Pos.Dimension = 1;
        f.Loaded = false;
        f.Mod.data.connections.Add(new(Port(0, dimension: 1), Port(5, dimension: 1)));
        f.Renderer.RequestFullRebuild(); f.Drain();
        Assert.Empty(f.Meshes);
        f.Loaded = true;
        // The game encodes the dimension in chunk Y, using BlockPos.InternalY / chunk size.
        f.Renderer.RequestChunkRebuild(new Vec3i(0, BlockPos.DimensionBoundary / 32, 0), null, EnumChunkDirtyReason.NewlyLoaded);
        f.Drain();
        Assert.Single(f.Meshes, m => !m.Disposed);
    }

    [Fact]
    public void Existing_world_graph_keeps_protobuf_field_identity()
    {
        var old=new OldData(); old.connections.Add(Link(0,5,LinkKind.Sleeve));
        var loaded=SerializerUtil.Deserialize<LinkNetworkData>(SerializerUtil.Serialize(old));
        Assert.Equal(LinkKind.Sleeve,Assert.Single(loaded.connections).kind); Assert.Equal(0,loaded.Revision);
        loaded.Revision=12;
        var again=SerializerUtil.Deserialize<LinkNetworkData>(SerializerUtil.Serialize(loaded));
        Assert.Equal(12,again.Revision); Assert.Single(again.connections);
    }
    [Fact]
    public void Deltas_replace_kind_and_reject_revision_gaps_without_partial_mutation()
    {
        var data=new LinkNetworkData(); data.connections.Add(Link(0,5));
        var delta=new LinkNetworkDelta {Before=0,After=1,Added=new(){Link(0,5,LinkKind.Sleeve),Link(6,9)}};
        var wire=SerializerUtil.Deserialize<LinkNetworkDelta>(SerializerUtil.Serialize(delta));
        Assert.True(wire.Apply(data)); Assert.Equal(2,data.connections.Count);
        Assert.Equal(LinkKind.Sleeve,data.connections.Single(c=>c.Equals(Link(0,5))).kind);
        Assert.False(new LinkNetworkDelta {Before=2,After=3,Removed=new(){Link(0,5)}}.Apply(data));
        Assert.Equal(2,data.connections.Count);
        Assert.True(new LinkNetworkDelta {Before=1,After=2,Removed=new(){Link(0,5)}}.Apply(data)); Assert.Single(data.connections);
    }
    [Fact]
    public void Server_batches_mutations_into_one_delta_instead_of_world_snapshots()
    {
        var packets=new List<object>();
        var channel=Proxy.Make<IServerNetworkChannel>((m,a)=> { if(m.Name=="BroadcastPacket") { packets.Add(a[0]); return null; } return Proxy.Unhandled; });
        var mod=new LinkNetworkMod(); Set(mod,"serverChannel",channel);
        var a=Link(0,5); var b=Link(10,15); mod.data.connections.UnionWith(new[]{a,b});
        Assert.True(mod.TryToRemoveConnection(a.pos1,a.pos2));
        Assert.True(mod.TryToRemoveConnection(b.pos1,b.pos2)); Assert.Empty(packets);
        Call(mod,"FlushChanges");
        var delta=Assert.IsType<LinkNetworkDelta>(Assert.Single(packets)); Assert.Equal(2,delta.Removed.Count); Assert.Equal(1,delta.After);
        Call(mod,"FlushChanges"); Assert.Single(packets);
    }
    [Fact]
    public void Chunk_index_handles_negative_coordinates_dimensions_and_removal()
    {
        var index=new LinkSpatialIndex(); var c=new LinkConnection(Port(-2,0,1),Port(3,0,1)); index.Add(c);
        Assert.Contains(c,index.In(new(-1,0,0,1))); Assert.Contains(c,index.In(new(0,0,0,1)));
        Assert.Empty(index.In(new(0,0,0,0))); Assert.Empty(index.In(new(50,0,0,1)));
        index.Remove(c); Assert.Empty(index.In(new(-1,0,0,1)));
    }
    [Fact]
    public void Route_cache_follows_couplings_but_stops_at_branched_endpoint_and_invalidates()
    {
        var cache=new LinkRouteCache();
        var a=Port(0); var b=Port(5); var through=Port(5,1); var c=Port(10); var next=Port(10,1); var d=Port(15);
        var first=new LinkConnection(a,b); var middle=new LinkConnection(through,c); var last=new LinkConnection(next,d);
        foreach(var con in new[]{first,middle,last,new LinkConnection(d,Port(20))}) cache.Add(con);
        int reads=0;
        NodePos[] Ports(NodePos p) { reads++; return p.blockPos.X switch {5=>new[]{b,through},10=>new[]{c,next},_=>new[]{p}}; }
        Assert.Equal(new[]{first,middle,last},cache.Get(a,b,Ports));
        int before=reads; Assert.Equal(3,cache.Get(a,b,Ports).Count); Assert.Equal(before,reads);
        cache.Remove(middle); Assert.Single(cache.Get(a,b,Ports));
        cache.Add(middle); Assert.Equal(3,cache.Get(a,b,Ports).Count);
    }
    [Fact]
    public void Route_loops_terminate_without_repeating_segments()
    {
        var cache=new LinkRouteCache(); var a=Port(0);var aa=Port(0,1);var b=Port(5);var bb=Port(5,1);
        cache.Add(new(a,b));cache.Add(new(bb,aa));
        Assert.Equal(2,cache.Get(a,b,p=>p.blockPos.X==0?new[]{a,aa}:new[]{b,bb}).Count);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void Precomputed_sway_weights_cover_vertices_and_keep_endpoints_fixed(byte kind)
    {
        var method=typeof(LinkNetworkMod).Assembly.GetType("SignalsLink.src.signals.link.LinkMesh").GetMethods(BindingFlags.Public|BindingFlags.Static).Single(m=>m.GetParameters().Length==8);
        var weights=new List<float>();
        var mesh=(MeshData)method.Invoke(null,new object[]{new Vec3f(),new Vec3f(8,1,0),new Vec3f(1,0,0),new Vec3f(-1,0,0),null,0f,LinkProfile.For(kind),weights});
        Assert.Equal(mesh.VerticesCount,weights.Count); Assert.Contains(weights,w=>w>.99f);
        int face=weights.Count/4;
        for(int i=0;i<4;i++) { Assert.Equal(0,weights[i*face]); Assert.Equal(0,weights[(i+1)*face-1]); }
        Assert.All(weights,w=>Assert.InRange(w,0,1));
        Assert.All(mesh.xyz.Take(mesh.VerticesCount*3),v=>Assert.True(float.IsFinite(v)));
    }
    [Fact]
    public void Renderer_batches_links_and_does_not_reupload_unchanged_or_unrelated_chunks()
    {
        using var f=new RenderFixture();
        for(int i=0;i<20;i++) f.Mod.data.connections.Add(new(Port(i),Port(i+1)));
        f.Renderer.RequestFullRebuild(); f.Drain();
        Assert.Equal(2,f.Meshes.Count(m=>!m.Disposed)); int before=f.Uploads;
        f.Renderer.RequestChunkRebuild(new Vec3i(100,0,100),null,EnumChunkDirtyReason.NewlyLoaded); f.Drain(); Assert.Equal(before,f.Uploads);
        f.Renderer.RequestChunkRebuild(new Vec3i(0,0,0),null,EnumChunkDirtyReason.NewlyLoaded); f.Drain(); Assert.Equal(before,f.Uploads);
        var c=f.Mod.data.connections.First(); f.Mod.data.connections.Remove(c);
        f.Renderer.ApplyDelta(new LinkNetworkDelta {Removed=new(){c}}); f.Drain(); Assert.Equal(before+1,f.Uploads);
        f.Loaded=false; f.Drain(); Assert.All(f.Meshes,m=>Assert.True(m.Disposed));
        f.Loaded=true;
        f.Renderer.RequestChunkRebuild(new Vec3i(0,0,0),null,EnumChunkDirtyReason.NewlyLoaded);
        f.Drain(); Assert.Equal(2,f.Meshes.Count(m=>!m.Disposed));
    }
    [Fact]
    public void Each_line_is_drawn_with_the_average_light_along_it()
    {
        using var f=new RenderFixture();
        f.Light=(x,y,z)=>x<4 ? new Vec4f(1,1,1,1) : new Vec4f(.2f,.2f,.2f,0);
        f.Mod.data.connections.Add(Link(0,8));    // two of five samples in the light, three in the dark
        f.Renderer.RequestFullRebuild(); f.Drain();
        Assert.NotEmpty(f.Lights);
        Assert.All(f.Lights,light=> { Assert.InRange(light.X,.5f,.55f); Assert.InRange(light.W,.35f,.45f); });
        Assert.NotEmpty(f.Ranges); Assert.All(f.Ranges,range=>Assert.Equal(0,range.start));   // its own draw, from the start of the batch
        Assert.All(f.Uploaded,m=>Assert.All(m.Rgba,b=>Assert.Equal(255,b)));   // no vertex tint
    }
    [Fact]
    public void Sway_updates_only_positions_and_preserves_the_eight_segment_cap()
    {
        using var f=new RenderFixture();
        for(int i=0;i<12;i++) f.Mod.data.connections.Add(new(Port(i),Port(i+1)));
        f.Renderer.RequestFullRebuild(); f.Drain(); int before=f.Uploads;
        foreach(var c in f.Mod.data.connections) f.Renderer.TriggerWobble(c.pos1,c.pos2);
        var list=(System.Collections.ICollection)typeof(HangingLinksRenderer).GetField("wobblers",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(f.Renderer);
        Assert.Equal(8,list.Count);
        f.Renderer.OnRenderFrame(.1f,EnumRenderStage.Opaque);
        Assert.Equal(before,f.Uploads); Assert.True(f.Updates>0);
        Assert.All(f.UpdateData,m=>{ Assert.Null(m.Indices);Assert.Null(m.Uv); Assert.Null(m.Rgba); });
    }
    [Fact]
    public void Missing_delta_requests_one_snapshot_and_recovers()
    {
        int requests=0;
        var mod=new LinkNetworkMod();
        var channel=Proxy.Make<IClientNetworkChannel>((m,a)=> {if(m.Name=="SendPacket") {requests++;return null;}return Proxy.Unhandled;});
        Set(mod,"clientChannel",channel);
        var bad=new LinkNetworkDelta {Before=2,After=3,Added=new(){Link(0,5)}};
        Call(mod,"OnDeltaFromServer",bad); Call(mod,"OnDeltaFromServer",bad);
        Assert.Equal(1,requests); Assert.Empty(mod.data.connections);
        Call(mod,"OnDataFromServer",new LinkNetworkData {Revision=3});
        Call(mod,"OnDeltaFromServer",new LinkNetworkDelta {Before=3,After=4,Added=new(){Link(0,5)}});
        Assert.Single(mod.data.connections); Assert.Equal(4,mod.data.Revision);
    }
    [Fact]
    public void Obstruction_index_never_walks_all_world_paths_and_sweep_has_fixed_budget()
    {
        var mod=new LinkNetworkMod();
        for(int i=0;i<1000;i++) mod.data.connections.Add(Link(i*8,i*8+5,LinkKind.Sleeve));
        int reads=0;
        var anchor=new Anchor {Replaceable=10000};
        var chunk=Proxy.Make<IWorldChunk>((m,a)=>Proxy.Unhandled);
        bool loaded=true;
        var api=Proxy.Make<ICoreServerAPI>((m,a)=> {
            if(m.Name=="GetBlock") {reads++; return anchor;}
            if(m.Name=="GetChunkAtBlockPos") return loaded?chunk:null;
            if(m.Name=="get_Side") return EnumAppSide.Server;
            return Proxy.Unhandled;
        });
        Set(mod,"api",api);
        using var monitor=new LinkObstructionMonitor(api,mod);
        Assert.Equal(0,reads);
        Call(monitor,"Tick",.25f);
        Assert.InRange(reads,1,60); Assert.Equal(1000,mod.data.connections.Count);
        loaded=false; reads=0; Call(monitor,"Tick",.25f);
        Assert.InRange(reads,0,12); Assert.Equal(1000,mod.data.connections.Count);
        var con=mod.data.connections.First(); mod.TryToRemoveConnection(con.pos1,con.pos2);
        Assert.Equal(999,mod.data.connections.Count);
    }
    [Fact]
    public void Pulse_can_choose_middle_segment_but_never_an_unrelated_branch()
    {
        using var f=new RenderFixture();
        f.Anchor.Ports=p=>p.X is 5 or 10 ? new[]{new NodePos(p,0),new NodePos(p,1)} : new[]{new NodePos(p,0)};
        var first=new LinkConnection(Port(0),Port(5));
        var middle=new LinkConnection(Port(5,1),Port(10));
        var last=new LinkConnection(Port(10,1),Port(15));
        var unrelated=new LinkConnection(Port(0),Port(-5));
        f.Mod.data.connections.UnionWith(new[]{first,middle,last,unrelated});
        f.Renderer.RequestFullRebuild();f.Drain();
        // Active segments are not reselected: three pulses must cover exactly this route.
        for(int i=0;i<3;i++) f.Renderer.TriggerWobble(first.pos1,first.pos2);
        var wobblers=(System.Collections.IEnumerable)typeof(HangingLinksRenderer).GetField("wobblers",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(f.Renderer);
        var selected=wobblers.Cast<object>().Select(v=>(LinkConnection)v.GetType().GetField("Con").GetValue(v)).ToHashSet();
        Assert.True(selected.SetEquals(new[]{first,middle,last})); Assert.DoesNotContain(unrelated,selected);
    }
    private sealed class ProbeRenderer : HangingLinksRenderer
    {
        private readonly EntityPlayer player;
        public ProbeRenderer(ICoreClientAPI api,LinkNetworkMod mod,EntityPlayer player):base(api,mod) { this.player=player; }
        protected override EntityPlayer Player=>player;
    }
    private sealed class FakeMesh:MeshRef { public override bool Initialized=>!Disposed; }
    private sealed class Anchor:Block,ILinkAnchor
    {
        public Anchor() { BlockId=1; Code=new AssetLocation("test:anchor"); }
        public byte AcceptedLinkKind=>0;
        public bool AllowsMultipleLinks(NodePos p)=>true;
        public bool CanAttachLink(IWorldAccessor w,NodePos p,NodePos initial=null)=>true;
        public Vec3f GetLinkAnchorPosInBlock(NodePos p)=>new(.5f,.5f,.5f);
        public System.Func<BlockPos,NodePos[]> Ports=p=>new[]{new NodePos(p,0)};
        public NodePos[] GetLinkAnchors(IWorldAccessor w,BlockPos p)=>Ports(p);
        public NodePos GetNodePosForLink(IWorldAccessor w,BlockSelection s,NodePos initial=null)=>new(s.Position,0);
    }
    private sealed class RenderFixture:IDisposable
    {
        public readonly LinkNetworkMod Mod=new();
        public HangingLinksRenderer Renderer;
        public int Uploads,Updates;
        public readonly Anchor Anchor=new();
        public bool Loaded=true;
        public readonly EntityPlayer Player=new();
        public List<FakeMesh> Meshes=new();
        public List<MeshData> UpdateData=new();
        public RenderFixture()
        {
            var entity=Player; var anchor=Anchor;
            var chunk=Proxy.Make<IWorldChunk>((m,a)=>Proxy.Unhandled);
            var shader=Proxy.Make<IStandardShaderProgram>((m,a)=> { if(m.Name=="set_RgbaLightIn") Lights.Add((Vec4f)a[0]); return Proxy.Unhandled; });
            var frustum=new FrustumCulling();
            var api=Proxy.Make<ICoreClientAPI>((m,a)=>m.Name switch {
                "get_Entity"=>entity, "GetBlock"=>anchor, "GetChunkAtBlockPos"=>Loaded?chunk:null,
                "get_DefaultFrustumCuller"=>frustum, "PreparedStandardShader"=>shader,
                "GetOrLoadTexture"=>1, "UploadMesh"=>Upload((MeshData)a[0]), "UpdateMesh"=>Update((MeshData)a[1]),
                "get_AmbientColor"=>new Vec3f(1,1,1), "GetLightRGBs"=>LightAt((int)a[0],(int)a[1],(int)a[2]),
                "RenderMesh" when a.Length==4=>Range(((int[])a[1])[0],((int[])a[2])[0]),
                _=>Proxy.Unhandled });
            Renderer=new ProbeRenderer(api,Mod,entity);
        }
        /// <summary>Uniform light unless a test says otherwise: a lamp at one end, dark at the other.</summary>
        public System.Func<int,int,int,Vec4f> Light=(x,y,z)=>new Vec4f(1,1,1,1);
        private Vec4f LightAt(int x,int y,int z)=>Light(x,y,z);
        public List<MeshData> Uploaded=new();
        public List<Vec4f> Lights=new();
        public List<(int start,int count)> Ranges=new();
        private object Range(int start,int count) { Ranges.Add((start,count)); return null; }
        private object Upload(MeshData data) { Uploads++; Uploaded.Add(data); var mesh=new FakeMesh(); Meshes.Add(mesh); return mesh; }
        private object Update(MeshData mesh) { Updates++; UpdateData.Add(mesh); return null; }
        public void Drain() { for(int i=0;i<50;i++) {Renderer.OnClientTick(.016f);Renderer.OnRenderFrame(.016f,EnumRenderStage.Opaque);} }
        public void Dispose()=>Renderer.Dispose();
    }
}

