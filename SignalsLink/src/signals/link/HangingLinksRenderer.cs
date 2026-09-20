using System.Diagnostics;
using signals.src.signalNetwork;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
namespace SignalsLink.src.signals.link;

/// <summary>Incremental, bounded mesh construction; small spatial batches and allocation-free sway.</summary>
public class HangingLinksRenderer : IRenderer
{
    public double RenderOrder=>0.5;
    public int RenderRange=>100;
    protected virtual Vintagestory.API.Common.EntityPlayer Player=>capi.World?.Player?.Entity;
    private const int MaxWobblers=8, BatchSize=16;
    private readonly ICoreClientAPI capi;
    private readonly LinkNetworkMod mod;
    private readonly LinkSpatialIndex spatial=new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(int X,int Y,int Z),bool> changedChunks=new();
    private readonly LinkRouteCache routes=new();
    private readonly Dictionary<LinkConnection,LinkConnection> known=new();
    private readonly Dictionary<LinkConnection,Visual> visuals=new();
    private readonly Queue<LinkConnection> work=new();
    private readonly HashSet<LinkConnection> queued=new();
    private readonly Dictionary<(LinkSpatialIndex.Cell,byte),List<Batch>> groups=new();
    private readonly HashSet<Batch> dirtyBatches=new();
    private readonly Queue<Batch> batchWork=new();
    private readonly LinkedList<LinkConnection> resident=new();
    private readonly Dictionary<LinkConnection,LinkedListNode<LinkConnection>> residentNodes=new();
    private void Dirty(Batch batch) { if(dirtyBatches.Add(batch)) batchWork.Enqueue(batch); }
    private readonly List<Visual> wobblers=new();
    private readonly List<Batch> visible=new();
    private readonly int[] textures=new int[LinkKind.Count];
    private readonly Matrixf matrix=new();
    private bool visibilityDirty=true, disposed;
    private double visibilityTime;
    private Vec3d lastCamera=new(double.MaxValue,0,0);
    private int lastDimension=int.MinValue;
    private sealed class Visual
    {
        public LinkConnection Con;
        public int Block1,Block2,Offset;
        public MeshData Mesh;
        public float[] Weights;
        public Vec3d Origin,Center;
        public Vec3f Sway;
        public double Radius;
        public float Time=-1;
        public Batch Batch;
    }
    private sealed class Batch
    {
        public LinkSpatialIndex.Cell Cell;
        public byte Kind;
        public readonly List<Visual> Items=new();
        public Vec3d Origin;
        public MeshRef Gpu;
        public MeshData Cpu, Positions;
        public float[] Rest;
        public bool Upload;
    }
    public HangingLinksRenderer(ICoreClientAPI capi,LinkNetworkMod mod)
    {
        this.capi=capi; this.mod=mod;
        capi.Event.RegisterRenderer(this,EnumRenderStage.Opaque,"signalslinklinks");
    }
    private void Enqueue(LinkConnection c) { if(queued.Add(c)) work.Enqueue(c); }
    public void RequestFullRebuild()=>RequestIncrementalRebuild(mod.data);
    public void RequestIncrementalRebuild(LinkNetworkData data)
    {
        foreach(var c in known.Values.ToArray()) if(!data.connections.Contains(c)) Remove(c);
        foreach(var c in data.connections) Add(c);
    }
    public void ApplyDelta(LinkNetworkDelta delta)
    {
        foreach(var c in delta.Removed) Remove(c);
        foreach(var c in delta.Added) Add(c);
    }
    private void Add(LinkConnection c)
    {
        if(known.TryGetValue(c,out var old) && old.kind!=c.kind) Remove(old);
        if(!known.ContainsKey(c)) { known[c]=c; spatial.Add(c); routes.Add(c); }
        Enqueue(c);
    }
    private void RemoveVisual(LinkConnection c)
    {
        if(!visuals.Remove(c,out var v)) return;
        wobblers.Remove(v); v.Batch.Items.Remove(v); Dirty(v.Batch);
        if(residentNodes.Remove(c,out var node)) resident.Remove(node); visibilityDirty=true;
    }
    private void Remove(LinkConnection c)
    {
        if(known.Remove(c,out var old)) { spatial.Remove(old); routes.Remove(old); }
        RemoveVisual(c);
    }
    public void RequestChunkRebuild(Vec3i coord,IWorldChunk chunk,EnumChunkDirtyReason reason)
    {
        if(disposed) return;
        changedChunks.AddOrUpdate((coord.X,coord.Y,coord.Z),reason!=EnumChunkDirtyReason.MarkedDirty,
            (_,loaded)=>loaded || reason!=EnumChunkDirtyReason.MarkedDirty);
    }
    public void OnClientTick(float dt)
    {
        if(disposed || Player==null) return;
        int chunks=0;
        foreach(var change in changedChunks)
        {
            if(!changedChunks.TryRemove(change.Key,out bool loaded)) continue;
            var key=change.Key;
            // Chunk events use InternalY: the dimension is encoded in Y, independent of
            // where the player currently stands (events can arrive during a dimension switch).
            const int dimensionHeight = BlockPos.DimensionBoundary / 32;
            var cell=new LinkSpatialIndex.Cell(key.X,key.Y % dimensionHeight,key.Z,key.Y / dimensionHeight);
            bool touched=false;
            foreach(var c in spatial.In(cell)) { Enqueue(c); touched=true; }
            if(touched && loaded) routes.InvalidateRoutes();
            if(++chunks>=32) break;
        }
        long started=Stopwatch.GetTimestamp();
        // Limit both number of links and elapsed construction time. Never rebuild the world in one tick.
        for(int n=0;n<8 && work.TryDequeue(out var c);n++)
        {
            queued.Remove(c);
            if(known.TryGetValue(c,out var actual)) Build(actual);
            if(Stopwatch.GetElapsedTime(started).TotalMilliseconds>=2) break;
        }
        for(int i=0,count=Math.Min(4,resident.Count);i<count && resident.First!=null;i++)
        {
            var node=resident.First; resident.RemoveFirst(); resident.AddLast(node);
            var c=node.Value;
            if(capi.World.BlockAccessor.GetChunkAtBlockPos(c.pos1.blockPos)==null || capi.World.BlockAccessor.GetChunkAtBlockPos(c.pos2.blockPos)==null)
            { RemoveVisual(c); routes.InvalidateRoutes(); }
        }
    }
    private void Build(LinkConnection c)
    {
        var ba=capi.World.BlockAccessor;
        if(ba.GetChunkAtBlockPos(c.pos1.blockPos)==null || ba.GetChunkAtBlockPos(c.pos2.blockPos)==null)
        { RemoveVisual(c); return; }
        var b1=ba.GetBlock(c.pos1.blockPos); var b2=ba.GetBlock(c.pos2.blockPos);
        if(b1 is not ILinkAnchor a1 || b2 is not ILinkAnchor a2) { RemoveVisual(c); return; }
        if(visuals.TryGetValue(c,out var old) && old.Block1==b1.Id && old.Block2==b2.Id) return;
        RemoveVisual(c); routes.InvalidateRoutes();
        var p1=a1.GetLinkAnchorPosInBlock(c.pos1); var p2=a2.GetLinkAnchorPosInBlock(c.pos2);
        var origin=new Vec3d(c.pos1.blockPos.X+p1.X,c.pos1.blockPos.Y+p1.Y,c.pos1.blockPos.Z+p1.Z);
        var end=new Vec3f(c.pos2.blockPos.X-c.pos1.blockPos.X+p2.X-p1.X,c.pos2.blockPos.Y-c.pos1.blockPos.Y+p2.Y-p1.Y,c.pos2.blockPos.Z-c.pos1.blockPos.Z+p2.Z-p1.Z);
        var weights=new List<float>();
        var mesh=LinkMesh.MakeLinkMesh(new Vec3f(),end,GetAnchorExitDirection(a1,c.pos1),GetAnchorExitDirection(a2,c.pos2),null,0,LinkProfile.For(c.kind),weights);
        mesh.SetMode(EnumDrawMode.Triangles);
        var sway=new Vec3f(end.X,0,end.Z);
        if(sway.X==0 && sway.Z==0) sway.X=1; else sway.Normalize();
        var v=new Visual { Con=c,Block1=b1.Id,Block2=b2.Id,Origin=origin,Center=origin.AddCopy(end.X/2,end.Y/2,end.Z/2),
            Radius=Math.Sqrt(end.X*end.X+end.Y*end.Y+end.Z*end.Z)/2+1,Mesh=mesh,Weights=weights.ToArray(),Sway=sway };
        var cell=LinkSpatialIndex.At(c.pos1.blockPos); var key=(cell,c.kind);
        if(!groups.TryGetValue(key,out var batches)) groups[key]=batches=new();
        var batch=batches.Find(b=>b.Items.Count<BatchSize);
        if(batch==null) { batch=new Batch { Cell=cell,Kind=c.kind,Origin=new Vec3d(cell.X*32,cell.Y*32,cell.Z*32) }; batches.Add(batch); }
        batch.Items.Add(v); v.Batch=batch; visuals[c]=v; Dirty(batch); residentNodes[c]=resident.AddLast(c); visibilityDirty=true;
    }
    private void BuildBatch(Batch b)
    {
        b.Gpu?.Dispose(); b.Gpu=null;
        if(b.Items.Count==0) return;
        var mesh=new MeshData(b.Items.Sum(v=>v.Mesh.VerticesCount),b.Items.Sum(v=>v.Mesh.IndicesCount));
        mesh.SetMode(EnumDrawMode.Triangles);
        foreach(var v in b.Items)
        {
            v.Offset=mesh.VerticesCount;
            var copy=v.Mesh.Clone(); copy.Translate((float)(v.Origin.X-b.Origin.X),(float)(v.Origin.Y-b.Origin.Y),(float)(v.Origin.Z-b.Origin.Z));
            mesh.AddMeshData(copy);
        }
        b.Upload=false; b.Cpu=mesh; b.Rest=(float[])mesh.xyz.Clone();
        b.Positions=new MeshData(false) { xyz=mesh.xyz,VerticesCount=mesh.VerticesCount };
        b.Gpu=capi.Render.UploadMesh(mesh);
        foreach(var v in b.Items) if(v.Time>=0) Sway(v);
    }
    private bool IsVisible(Visual v,Vec3d cam)
    {
        if(v.Con.pos1.blockPos.dimension!=Player.Pos.Dimension) return false;
        double dx=v.Center.X-cam.X,dy=v.Center.Y-cam.Y,dz=v.Center.Z-cam.Z,range=RenderRange+v.Radius;
        return dx*dx+dy*dy+dz*dz<=range*range && capi.Render.DefaultFrustumCuller.SphereInFrustum(v.Center.X,v.Center.Y,v.Center.Z,v.Radius);
    }
    public void TriggerWobble(NodePos anchor,NodePos other=null)
    {
        if(disposed || anchor==null || wobblers.Count>=MaxWobblers || Player==null) return;
        var route=routes.Get(anchor,other,p=>(capi.World.BlockAccessor.GetBlock(p.blockPos) as ILinkAnchor)?.GetLinkAnchors(capi.World,p.blockPos));
        Visual selected=null; int seen=0; var cam=Player.CameraPos;
        // Reservoir sampling: no temporary list and exactly one segment per pulse.
        foreach(var c in route) if(visuals.TryGetValue(c,out var v) && v.Time<0 && v.Batch.Gpu!=null && IsVisible(v,cam))
            if(Random.Shared.Next(++seen)==0) selected=v;
        if(selected!=null) { selected.Time=0; wobblers.Add(selected); }
    }
    private static void Sway(Visual v)
    {
        var b=v.Batch; if(b.Rest==null) return;
        float amount=v.Time<0 ? 0 : .12f*(float)Math.Sin(v.Time/.6f*Math.PI*2)*(1-v.Time/1.2f);
        for(int i=0;i<v.Mesh.VerticesCount;i++)
        {
            int index=(v.Offset+i)*3; float weight=v.Weights[i]*amount;
            b.Cpu.xyz[index]=b.Rest[index]+v.Sway.X*weight;
            b.Cpu.xyz[index+2]=b.Rest[index+2]+v.Sway.Z*weight;
        }
        b.Upload=true;
    }
    public void OnRenderFrame(float dt,EnumRenderStage stage)
    {
        if(disposed || stage!=EnumRenderStage.Opaque || Player==null) return;
        long started=Stopwatch.GetTimestamp();
        for(int count=0;count<8 && batchWork.TryDequeue(out var batch);count++)
        {
            dirtyBatches.Remove(batch); BuildBatch(batch);
            if(batch.Items.Count==0) { var key=(batch.Cell,batch.Kind); groups[key].Remove(batch); if(groups[key].Count==0) groups.Remove(key); }
            if(Stopwatch.GetElapsedTime(started).TotalMilliseconds>=2) break;
        }
        var cam=Player.CameraPos;
        for(int i=wobblers.Count-1;i>=0;i--)
        {
            var v=wobblers[i]; v.Time+=dt;
            if(v.Time>=1.2f || !IsVisible(v,cam)) { v.Time=-1; wobblers.RemoveAt(i); }
            if(!dirtyBatches.Contains(v.Batch)) Sway(v);
        }
        visibilityTime+=dt;
        int dimension=Player.Pos.Dimension;
        if(lastDimension!=dimension)
        {
            lastDimension=dimension; routes.InvalidateRoutes();
            foreach(var c in known.Values) Enqueue(c);
            visibilityDirty=true;
        }
        if(visibilityDirty || visibilityTime>=.25 || cam.SquareDistanceTo(lastCamera)>64)
        {
            visible.Clear(); visibilityTime=0; visibilityDirty=false; lastCamera.Set(cam);
            foreach(var list in groups.Values) foreach(var b in list)
            {
                if(b.Cell.Dimension!=dimension) continue;
                double dx=b.Origin.X+16-cam.X,dy=b.Origin.Y+16-cam.Y,dz=b.Origin.Z+16-cam.Z;
                if(dx*dx+dy*dy+dz*dz<160*160) visible.Add(b);
            }
        }
        if(visible.Count==0) return;
        var r=capi.Render; r.GLEnableDepthTest(); r.GlEnableCullFace();
        var shader=r.PreparedStandardShader(0,0,0); shader.Use();
        shader.ProjectionMatrix=r.CurrentProjectionMatrix; shader.ViewMatrix=r.CameraMatrixOriginf;
        for(byte kind=0;kind<LinkKind.Count;kind++)
        {
            bool bound=false;
            foreach(var b in visible)
            {
                if(b.Kind!=kind || b.Gpu==null || dirtyBatches.Contains(b)) continue;
                if(!r.DefaultFrustumCuller.SphereInFrustum(b.Origin.X+16,b.Origin.Y+16,b.Origin.Z+16,40)) continue;
                if(!bound) { if(textures[kind]<=0) textures[kind]=r.GetOrLoadTexture(LinkProfile.For(kind).Texture); r.BindTexture2d(textures[kind]); bound=true; }
                if(b.Upload) { r.UpdateMesh(b.Gpu,b.Positions); b.Upload=false; }
                shader.ModelMatrix=matrix.Identity().Translate(b.Origin.X-cam.X,b.Origin.Y-cam.Y,b.Origin.Z-cam.Z).Values;
                r.RenderMesh(b.Gpu);
            }
        }
        shader.Stop();
    }
        private static Vec3f GetAnchorExitDirection(ILinkAnchor owner, NodePos anchor)
        {
            if (owner is BlockLinkEndpointBase endpoint)
            {
                string sideCode = endpoint.Variant?["side"];
                BlockFacing hostFace = sideCode != null ? BlockFacing.FromCode(sideCode) : null;
                if (hostFace == BlockFacing.DOWN && endpoint.FloorMountExitsSideways)
                {
                    string orientationCode = endpoint.Variant?["orientation"];
                    BlockFacing orientation = orientationCode != null ? BlockFacing.FromCode(orientationCode) : null;
                    if (orientation != null)
                    {
                        Vec3i outward = orientation.Opposite.Normali;
                        return new Vec3f(outward.X, outward.Y, outward.Z);
                    }
                }
                if (hostFace != null)
                {
                    Vec3i outward = hostFace.Opposite.Normali;
                    return new Vec3f(outward.X, outward.Y, outward.Z);
                }
            }

            Vec3f position = owner.GetLinkAnchorPosInBlock(anchor);
            float offsetX = position.X - 0.5f;
            float offsetY = position.Y - 0.5f;
            float offsetZ = position.Z - 0.5f;
            float absX = Math.Abs(offsetX);
            float absY = Math.Abs(offsetY);
            float absZ = Math.Abs(offsetZ);

            if (absX >= absY && absX >= absZ) return new Vec3f(offsetX >= 0f ? 1f : -1f, 0f, 0f);
            if (absY >= absZ) return new Vec3f(0f, offsetY >= 0f ? 1f : -1f, 0f);
            return new Vec3f(0f, 0f, offsetZ >= 0f ? 1f : -1f);
        }


    public void Dispose()
    {
        if(disposed) return; disposed=true;
        capi.Event.UnregisterRenderer(this,EnumRenderStage.Opaque);
        foreach(var list in groups.Values) foreach(var b in list) b.Gpu?.Dispose();
        groups.Clear(); visuals.Clear(); known.Clear(); work.Clear(); queued.Clear(); dirtyBatches.Clear(); wobblers.Clear(); visible.Clear(); routes.Clear(); spatial.Clear(); batchWork.Clear(); resident.Clear(); residentNodes.Clear(); changedChunks.Clear();
    }
}
