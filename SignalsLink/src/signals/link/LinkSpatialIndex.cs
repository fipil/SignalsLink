using Vintagestory.API.MathTools;
namespace SignalsLink.src.signals.link;
/// <summary>Conservative chunk coverage, independent of loaded blocks.</summary>
public sealed class LinkSpatialIndex
{
    public readonly record struct Cell(int X,int Y,int Z,int Dimension);
    private readonly Dictionary<Cell,HashSet<LinkConnection>> cells=new();
    public static Cell At(BlockPos p)=>new(p.X>>5,p.Y>>5,p.Z>>5,p.dimension);
    public static IEnumerable<Cell> Covered(LinkConnection c)
    {
        var a=c.pos1.blockPos; var b=c.pos2.blockPos;
        for(int x=(Math.Min(a.X,b.X)-1)>>5;x<=(Math.Max(a.X,b.X)+1)>>5;x++)
        for(int y=(Math.Min(a.Y,b.Y)-1)>>5;y<=(Math.Max(a.Y,b.Y)+1)>>5;y++)
        for(int z=(Math.Min(a.Z,b.Z)-1)>>5;z<=(Math.Max(a.Z,b.Z)+1)>>5;z++)
            yield return new Cell(x,y,z,a.dimension);
    }
    public IEnumerable<LinkConnection> In(Cell cell)=>cells.TryGetValue(cell,out var set)?set:Array.Empty<LinkConnection>();
    public void Clear()=>cells.Clear();
    public void Add(LinkConnection con)
    {
        foreach(var cell in Covered(con)) { if(!cells.TryGetValue(cell,out var set)) cells[cell]=set=new(); set.Add(con); }
    }
    public void Remove(LinkConnection con)
    {
        foreach(var cell in Covered(con)) if(cells.TryGetValue(cell,out var set)) { set.Remove(con); if(set.Count==0) cells.Remove(cell); }
    }
}
