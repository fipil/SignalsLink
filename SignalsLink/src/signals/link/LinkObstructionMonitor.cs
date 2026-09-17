using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
namespace SignalsLink.src.signals.link;
/// <summary>Local placement checks and bounded rotating sweep. No global collision-path rebuild.</summary>
public sealed class LinkObstructionMonitor : IDisposable
{
    private readonly ICoreServerAPI sapi;
    private readonly LinkNetworkMod mod;
    private readonly LinkSpatialIndex spatial=new();
    private readonly LinkedList<LinkConnection> sweep=new();
    private readonly Dictionary<LinkConnection,LinkedListNode<LinkConnection>> nodes=new();
    private readonly Queue<LinkConnection> pending=new();
    private readonly HashSet<LinkConnection> queued=new();
    private readonly long listener;
    public LinkObstructionMonitor(ICoreServerAPI sapi,LinkNetworkMod mod)
    {
        this.sapi=sapi; this.mod=mod;
        mod.ConnectionChanged+=Changed; mod.NetworkReset+=Reset;
        sapi.Event.DidPlaceBlock+=Placed;
        listener=sapi.Event.RegisterGameTickListener(Tick,250);
        Reset();
    }
    private void Reset()
    {
        spatial.Clear(); sweep.Clear(); nodes.Clear(); pending.Clear(); queued.Clear();
        foreach(var con in mod.data.connections) Changed(con,true);
    }
    private void Changed(LinkConnection con,bool added)
    {
        if(nodes.Remove(con,out var node)) { sweep.Remove(node); spatial.Remove(con); }
        if(added && con.kind==LinkKind.Sleeve) { nodes[con]=sweep.AddLast(con); spatial.Add(con); }
    }
    private void Placed(IServerPlayer player,int oldId,BlockSelection selection,ItemStack stack)
    {
        if(selection?.Position==null) return;
        foreach(var con in spatial.In(LinkSpatialIndex.At(selection.Position))) if(queued.Add(con)) pending.Enqueue(con);
    }
    private void Tick(float dt)
    {
        for(int i=0;i<4 && pending.TryDequeue(out var con);i++) { queued.Remove(con); Check(con); }
        int count=Math.Min(2,sweep.Count);
        for(int i=0;i<count && sweep.First!=null;i++)
        {
            var node=sweep.First; sweep.RemoveFirst(); sweep.AddLast(node); Check(node.Value);
        }
    }
    private void Check(LinkConnection con)
    {
        if(!nodes.ContainsKey(con)) return;
        if(LinkPathChecker.Check(sapi.World,con,out var blocked)==LinkPathResult.Blocked) mod.BreakLinkAt(con,blocked);
    }
    public void Dispose()
    {
        sapi.Event.DidPlaceBlock-=Placed; sapi.Event.UnregisterGameTickListener(listener);
        mod.ConnectionChanged-=Changed; mod.NetworkReset-=Reset;
    }
}
