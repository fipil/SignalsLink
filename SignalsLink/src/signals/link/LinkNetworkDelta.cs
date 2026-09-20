using ProtoBuf;
namespace SignalsLink.src.signals.link;
[ProtoContract(ImplicitFields=ImplicitFields.AllPublic)]
public sealed class LinkNetworkDelta
{
    public long Before;
    public long After;
    public List<LinkConnection> Added=new();
    public List<LinkConnection> Removed=new();
    public bool Apply(LinkNetworkData data)
    {
        if(data.Revision!=Before || After!=Before+1) return false;
        foreach(var c in Removed) data.connections.Remove(c);
        foreach(var c in Added) { data.connections.Remove(c); data.connections.Add(c); }
        data.Revision=After; return true;
    }
}
[ProtoContract] public sealed class LinkSnapshotRequest { }
