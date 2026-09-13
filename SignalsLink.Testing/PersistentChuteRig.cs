using signals.src;
using signals.src.hangingwires;
using signals.src.signalNetwork;
using SignalsLink.src.signals.link;
using SignalsLink.src.signals.managedchute;
using SignalsLink.src.signals.testchest;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SignalsLink.Testing;

public sealed class GroundLayout
{
    public ArenaLocation Location { get; }
    public GroundLayout(ArenaLocation location) { Location = location; }
    public BlockPos At(int x, int y, int z) => Location.Origin.AddCopy(x, y, z);
    public BlockPos Power => At(0,0,2);
    public BlockPos Switch => At(2,0,2);
    public bool Legacy => Location.Version < 3;
    public int Height => Legacy ? 4 : 7;
    public BlockPos Supply => At(4,6,2);
    public BlockPos Filler => At(4,5,2);
    public BlockPos Drain => At(4,1,2);
    public BlockPos Recycler => At(4,0,2);
    public BlockPos FillSwitch => At(2,0,0);
    public BlockPos DrainSwitch => At(6,0,0);
    public BlockPos Target => At(4,Legacy ? 0 : 2,2);
    public BlockPos Chute => At(4,Legacy ? 1 : 3,2);
    public BlockPos Source => At(4,Legacy ? 2 : 4,2);
    public BlockPos Output => At(6,0,2);
    public IEnumerable<BlockPos> Cells()
    { for (int y=0;y<Height;y++) for(int x=0;x<9;x++) for(int z=0;z<5;z++) yield return At(x,y,z); }
    public IEnumerable<BlockPos> Ground()
    { for(int x=0;x<9;x++) for(int z=0;z<5;z++) yield return At(x,-1,z); }
    public bool Contains(BlockPos p) => p != null && p.dimension == Location.Dimension
        && p.X >= Location.X && p.X < Location.X+9 && p.Y >= Location.Y && p.Y < Location.Y+Height && p.Z >= Location.Z && p.Z < Location.Z+5;
    public IEnumerable<BlockPos> Devices => Legacy ? new[] { Power, Switch, Target, Chute, Source, Output }
        : new[] { Power, Switch, FillSwitch, DrainSwitch, Recycler, Drain, Target, Chute, Source, Filler, Supply, Output };
    public bool IsSwitch(BlockPos p) => p.Equals(Switch) || !Legacy && (p.Equals(FillSwitch) || p.Equals(DrainSwitch));
    public string Code(BlockPos p) => p.Equals(Power) ? "signals:blocksource"
        : IsSwitch(p) ? "signals:knifeswitch-north-down-off"
        : p.Equals(Target) ? (Legacy ? "signalslink:recyclerchest-east" : "game:chest-east")
        : p.Equals(Source) ? (Legacy ? "signalslink:bottomlesschest-east" : "game:chest-east")
        : p.Equals(Chute) || !Legacy && (p.Equals(Filler) || p.Equals(Drain)) ? "signalslink:managedchute-north-up"
        : p.Equals(Output) ? "signals:connection-down"
        : !Legacy && p.Equals(Supply) ? "signalslink:bottomlesschest-east"
        : !Legacy && p.Equals(Recycler) ? "signalslink:recyclerchest-east" : null;
    public bool Owns(BlockPos p, string code) => Contains(p) && code != null
        && (Code(p) == code || IsSwitch(p) && code == "signals:knifeswitch-north-down-on");

}

public sealed class RigManifest
{
    public string Id { get; set; }
    public ArenaLocation Location { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Initialized { get; set; }
    public long Cycles { get; set; }
    public long Failures { get; set; }
    public string State { get; set; } = "starting";
    public string LastError { get; set; } = "";
}

/// <summary>Persistent real blocks; checks only observe stock/network and operate the physical switch.</summary>
public sealed class PersistentChuteRig
{
    public const string Paper = "game:firewood\namount 8\n\nin target\ngame:firewood 8+\noutput 9";
    public const string FillPaper = "game:firewood\nin target\ngame:firewood 0\namount 8";
    public const string DrainPaper = "game:firewood\namount 8";
    private readonly ICoreServerAPI api;
    private readonly HangingWiresMod wires;
    private readonly SignalNetworkMod signals;
    private readonly Action<string,string> record;
    private readonly WireConnection[] connections;
    private readonly Dictionary<string,Block> assets = new();
    private readonly ControlledChuteCycle cycle = new();
    private bool attached, paused;

    public RigManifest Manifest { get; }
    public GroundLayout Layout { get; }
    public PersistentChuteRig(ICoreServerAPI api, RigManifest manifest, Action<string,string> record)
    {
        this.api=api; Manifest=manifest; this.record=record; Layout=new GroundLayout(manifest.Location);
        wires=api.ModLoader.GetModSystem<HangingWiresMod>(); signals=api.ModLoader.GetModSystem<SignalNetworkMod>();
        connections = new[] {
            new WireConnection(new NodePos(Layout.Power,0), new NodePos(Layout.Switch,0)),
            new WireConnection(new NodePos(Layout.Switch,1), new NodePos(Layout.Chute,0)),
            new WireConnection(new NodePos(Layout.Chute,3), new NodePos(Layout.Output,0)) };
        if (!Layout.Legacy) connections = connections.Concat(new[] {
            new WireConnection(new NodePos(Layout.Power,0),new NodePos(Layout.FillSwitch,0)),
            new WireConnection(new NodePos(Layout.FillSwitch,1),new NodePos(Layout.Filler,0)),
            new WireConnection(new NodePos(Layout.Power,0),new NodePos(Layout.DrainSwitch,0)),
            new WireConnection(new NodePos(Layout.DrainSwitch,1),new NodePos(Layout.Drain,0)) }).ToArray();
    }
    public bool Loaded => new[] { Layout.At(0,0,0), Layout.At(8,0,0), Layout.At(0,0,4), Layout.At(8,Layout.Height-1,4) }.All(p => api.World.BlockAccessor.GetChunkAtBlockPos(p) != null);
    private BlockEntityGenericTypedContainer Source => api.World.BlockAccessor.GetBlockEntity(Layout.Source) as BlockEntityGenericTypedContainer;
    private BETestChest Supply => api.World.BlockAccessor.GetBlockEntity(Layout.Supply) as BETestChest;
    private BETestChest Recycler => api.World.BlockAccessor.GetBlockEntity(Layout.Recycler) as BETestChest;
    private BEManagedChute Filler => api.World.BlockAccessor.GetBlockEntity(Layout.Filler) as BEManagedChute;
    private BEManagedChute Drain => api.World.BlockAccessor.GetBlockEntity(Layout.Drain) as BEManagedChute;
    private BlockEntityGenericTypedContainer Target => api.World.BlockAccessor.GetBlockEntity(Layout.Target) as BlockEntityGenericTypedContainer;
    private BEManagedChute Chute => api.World.BlockAccessor.GetBlockEntity(Layout.Chute) as BEManagedChute;
    private ISignalNode Node(BlockPos p, int pin) => signals?.GetDeviceAt(p)?.GetNodeAt(new NodePos(p,pin));
    private int Value(BlockPos p,int pin) => Node(p,pin)?.value ?? -1;
    private bool NetworkReady => Node(Layout.Output,0)?.netId is long id && signals.netManager.networks.ContainsKey(id);
    private bool Ready => Source?.Inventory != null && Target?.Inventory != null && Supply?.Stock != null && Recycler?.Stock != null && Filler?.Api != null && Drain?.Api != null && Chute?.Api != null
        && connections.All(c => Node(c.pos1.blockPos,c.pos1.index) != null && Node(c.pos2.blockPos,c.pos2.index) != null);
    private static bool SameWire(WireConnection a,WireConnection b) => a.pos1==b.pos1 && a.pos2==b.pos2 || a.pos1==b.pos2 && a.pos2==b.pos1;
    public string Preflight(PersistentChuteRig replacing = null)
    {
        if (!Loaded) return "area is not loaded";
        foreach (var p in Layout.Ground())
        {
            var block=api.World.BlockAccessor.GetBlock(p);
            if (block.Id == 0 || block.SideSolid[BlockFacing.UP.Index] != true)
                return "needs level solid ground under the whole 9 x 5 area; unsupported at " + p;
        }
        foreach(var p in Layout.Cells())
            if((api.World.BlockAccessor.GetBlock(p,BlockLayersAccess.Solid).Id!=0 && !(replacing?.Layout.Owns(p,api.World.BlockAccessor.GetBlock(p).Code.ToString()) ?? false)) || api.World.BlockAccessor.GetBlock(p,BlockLayersAccess.Fluid).Id!=0)
                return "occupied area at " + p;
        if (wires == null || signals?.netManager == null) return "Signals network is not ready";
        if(wires.data.connections.Any(w=>(Layout.Contains(w.pos1.blockPos)||Layout.Contains(w.pos2.blockPos)) && !(replacing?.connections.Any(c=>SameWire(c,w)) ?? false))) return "wire crosses the area";
        if(api.ModLoader.GetModSystem<LinkNetworkMod>()?.data.connections.Any(w=>Layout.Contains(w.pos1.blockPos)||Layout.Contains(w.pos2.blockPos))==true) return "link crosses the area";
        if(api.World.GetEntitiesAround(Layout.At(4,Layout.Height/2,2).ToVec3d(),8,Layout.Height,e=>Layout.Contains(e.Pos.AsBlockPos)).Length>0) return "entity inside the area";
        foreach(var p in Layout.Cells())
        {
            string code=Layout.Code(p); if(code==null) continue;
            var block=api.World.GetBlock(new AssetLocation(code));
            if(block==null||block.Id==0) return "missing asset " + code;
            assets[code]=block;
        }
        if(api.World.GetItem(new AssetLocation("game:firewood"))==null) return "missing firewood";
        return null;
    }
    public void Build()
    {
        foreach(var p in Layout.Devices)
        {
            var stack=new ItemStack(assets[Layout.Code(p)]); stack.Attributes.SetString("type","normal-generic");
            api.World.BlockAccessor.SetBlock(stack.Block.Id,p,stack); api.World.BlockAccessor.MarkBlockDirty(p);
        }
        record("BUILD",Manifest.Id+" at "+Layout.Location.Origin+"; natural ground preserved");
    }
    public void SetSwitch(bool on) => SetSwitch(Layout.Switch,on);
    private void SetSwitch(BlockPos pos,bool on)
    {
        var block=api.World.BlockAccessor.GetBlock(pos);
        if(!Layout.Owns(pos,block.Code.ToString())) return;
        if(block.Code.Path.EndsWith(on ? "-on" : "-off",StringComparison.Ordinal)) return;
        block.Activate(api.World,new Caller(),new BlockSelection{Position=pos,Face=BlockFacing.UP});
    }
    public void Pause()
    {
        if(Loaded) { SetSwitch(false); Chute?.ClearBuffer(); if(!Layout.Legacy) { SetSwitch(Layout.FillSwitch,false); SetSwitch(Layout.DrainSwitch,false); Filler?.ClearBuffer(); Drain?.ClearBuffer(); } }
        attached=false; paused=Loaded; Transition("stopped","");
    }
    private void Transition(string state,string error)
    {
        if(Manifest.State==state && Manifest.LastError==error) return;
        bool failed=state=="failed";
        if(failed) Manifest.Failures++;
        record(failed?"FAILURE":state=="running"&&Manifest.State=="failed"?"RECOVERY":"STATE",Manifest.Id+" | "+state+" | "+error);
        Manifest.State=state; Manifest.LastError=error;
    }
    public string Snapshot() => Manifest.Id+" | "+Manifest.State+" | cycles="+Manifest.Cycles+" | failures="+Manifest.Failures
        +" | version="+Layout.Location.Version+" | phase="+cycle.PhaseName+" | at="+Layout.Location.Origin+" | supplied="+(Supply?.Stock?.Supplied??0)+" | received="+(Recycler?.Stock?.Received??0)
        +" | source="+(Source?.Inventory==null ? -1 : Count(Source))+" | target="+(Target?.Inventory==null ? -1 : Count(Target))
        +" | recycled="+(Recycler?.Stock?.Recycled??0)+" | input="+Value(Layout.Chute,0)+" | output="+Value(Layout.Output,0)
        +(Manifest.LastError.Length>0?" | "+Manifest.LastError:"");
    private static int Count(BlockEntityGenericTypedContainer chest) => chest.Inventory
        .Where(s=>!s.Empty && s.Itemstack.Collectible.Code.ToString()=="game:firewood").Sum(s=>s.StackSize);
    private void ApplySwitches()
    {
        SetSwitch(cycle.TestEnabled);
        SetSwitch(Layout.FillSwitch,cycle.FillEnabled);
        SetSwitch(Layout.DrainSwitch,cycle.DrainEnabled);
    }
    public void Tick(double seconds)
    {
        if(Layout.Legacy)
        {
            Manifest.Enabled=false;
            if(!paused && Loaded) Pause();
            Transition("upgrade-required","Legacy rig: /sltest upgrade");
            return;
        }
        if(!Manifest.Enabled) { if(!paused && Loaded) Pause(); return; }
        paused=false;
        if(!Loaded) { attached=false; Transition("waiting","chunks loading"); return; }
        if(!Ready) { attached=false; Transition(Manifest.Initialized ? "failed" : "waiting", "devices initializing or missing"); return; }
        if(Layout.Devices.Any(p=>!Layout.Owns(p,api.World.BlockAccessor.GetBlock(p).Code.ToString())))
        { Transition("failed","rig blocks changed; preserved for inspection"); return; }
        if(!Manifest.Initialized)
        {
            foreach(var c in connections) if(!wires.data.connections.Any(w=>SameWire(w,c))&&!wires.TryToAddConnection(c))
            { Transition("failed","cannot connect rig wire"); return; }
            Chute.ConditionsText=Paper; Filler.ConditionsText=FillPaper; Drain.ConditionsText=DrainPaper;
            var item=api.World.GetItem(new AssetLocation("game:firewood"));
            Supply.Inventory[0].Itemstack=new ItemStack(item,4); Supply.Inventory[0].MarkDirty();
            Supply.Inventory[1].Itemstack=new ItemStack(item,12); Supply.Inventory[1].MarkDirty();
            Manifest.Initialized=true; record("PAPER",Manifest.Id+" | "+Paper);
        }
        if(!attached) { attached=true; cycle.Reset(); ApplySwitches(); }
        if(Chute.ConditionsText!=Paper || Filler.ConditionsText!=FillPaper || Drain.ConditionsText!=DrainPaper)
        { Pause(); Manifest.Enabled=false; Transition("failed","rig paper was changed; stopped for inspection"); return; }
        int source=Count(Source), target=Count(Target);
        if(Supply.Inventory[0].StackSize!=4 || Supply.Inventory[1].StackSize!=12 || Supply.Stock.TemplateCount!=2)
        { Pause(); Manifest.Enabled=false; Transition("failed","supply templates changed; stopped for inspection"); return; }
        int previousPhase=cycle.Phase;
        bool sinkFull=!Recycler.Inventory.Any(s=>s.Empty || s.StackSize<Math.Min(s.MaxSlotStackSize,s.Itemstack.Collectible.MaxStackSize));
        var result=cycle.Observe(seconds,source,target,Value(Layout.Chute,0),NetworkReady?Value(Layout.Output,0):-1,
            Supply.Stock.Supplied,Recycler.Stock.Received,sinkFull);
        if(result.Failure!=null) Transition("failed",result.Failure);
        else if(result.Waiting) Transition("waiting","recycler full; waiting for disposal capacity");
        else if(result.Complete)
        {
            if(!connections.All(c=>wires.data.connections.Any(w=>SameWire(w,c)))) Transition("failed","rig wire missing");
            else { Manifest.Cycles++; Transition("running",""); }
        }
        if(cycle.Phase!=previousPhase || result.Failure!=null) ApplySwitches();
    }

    public bool Cleanup()
    {
        if(!Loaded) return false;
        foreach(var c in connections) wires.TryToRemoveConnection(c.pos1,c.pos2);
        bool complete=true;
        foreach(var p in Layout.Devices.OrderByDescending(p=>p.Y))
        {
            var block=api.World.BlockAccessor.GetBlock(p);
            if(block.Id==0) continue;
            if(!Layout.Owns(p,block.Code.ToString()) || wires.data.connections.Any(w=>w.pos1.blockPos.Equals(p)||w.pos2.blockPos.Equals(p))
                || api.ModLoader.GetModSystem<LinkNetworkMod>()?.data.connections.Any(w=>w.pos1.blockPos.Equals(p)||w.pos2.blockPos.Equals(p))==true)
            { complete=false; continue; }
            if(api.World.BlockAccessor.GetBlockEntity(p) is BETestChest chest) chest.Stock?.Clear();
            api.World.BlockAccessor.SetBlock(0,p); api.World.BlockAccessor.MarkBlockDirty(p);
        }
        return complete;
    }
}
