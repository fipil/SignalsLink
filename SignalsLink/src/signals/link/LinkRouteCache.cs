using signals.src.signalNetwork;
namespace SignalsLink.src.signals.link;
/// <summary>Routes follow the selected first hop; shared intake/valve branches are not crossed.</summary>
public sealed class LinkRouteCache
{
    private readonly Dictionary<NodePos,List<LinkConnection>> adjacent=new();
    private readonly Dictionary<(NodePos,NodePos),List<LinkConnection>> routes=new();
    public void Clear() { adjacent.Clear(); routes.Clear(); }
    public void InvalidateRoutes()=>routes.Clear();
    public void Add(LinkConnection con)
    {
        Remove(con);
        foreach(var a in new[]{con.pos1,con.pos2})
        { if(!adjacent.TryGetValue(a,out var list)) adjacent[a]=list=new(); list.Add(con); }
    }
    public void Remove(LinkConnection con)
    {
        routes.Clear();
        foreach(var a in new[]{con.pos1,con.pos2}) if(adjacent.TryGetValue(a,out var list))
        { list.RemoveAll(c=>c.Equals(con)); if(list.Count==0) adjacent.Remove(a); }
    }
    public IReadOnlyList<LinkConnection> Get(NodePos anchor,NodePos firstHop,Func<NodePos,NodePos[]> anchors)
    {
        if(anchor==null) return Array.Empty<LinkConnection>();
        if(firstHop==null)
        {
            if(!adjacent.TryGetValue(anchor,out var choices) || choices.Count==0) return Array.Empty<LinkConnection>();
            var c=choices[Random.Shared.Next(choices.Count)]; firstHop=c.pos1==anchor?c.pos2:c.pos1;
        }
        var key=(anchor,firstHop);
        if(routes.TryGetValue(key,out var cached)) return cached;
        var result=new List<LinkConnection>(); var visited=new HashSet<NodePos>();
        var from=anchor; var to=firstHop;
        while(visited.Add(from) && visited.Add(to))
        {
            if(!adjacent.TryGetValue(from,out var list)) break;
            var con=list.Find(c=>c.pos1==to || c.pos2==to);
            if(con==null) break;
            result.Add(con);
            var ports=anchors(to);
            if(ports==null || ports.Length!=2) break;
            var through=ports[0]==to?ports[1]:ports[1]==to?ports[0]:null;
            if(through==null || !adjacent.TryGetValue(through,out var next) || next.Count!=1) break;
            from=through; to=next[0].pos1==through?next[0].pos2:next[0].pos1;
        }
        routes[key]=result; return result;
    }
}
