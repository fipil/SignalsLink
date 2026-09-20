using System.Diagnostics;
using System.Text.Json;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SignalsLink.Testing;

public sealed class TestingMod : ModSystem
{
    private const string ManifestKey="signalslink-testing-rigs-v2";
    private ICoreServerAPI api;
    private readonly List<PersistentChuteRig> rigs=new();
    private readonly Queue<(int X,int Z)> toLoad=new();
    private readonly HashSet<(int X,int Z)> requested=new();
    private RigReport report;
    private long listener, callbacks;
    private double summarySeconds, totalMs, maxMs;
    private bool shuttingDown, logFailed, manifestReadable = true;
    private DateTime reportDay;
    public override bool ShouldLoad(EnumAppSide side)=>side==EnumAppSide.Server;
    public override void StartServerSide(ICoreServerAPI api)
    {
        this.api=api;
        api.ChatCommands.Create("sltest").WithDescription("Persistent rigs: add/run, start/stop [id|all], status/results, upgrade, clean id.")
            .RequiresPrivilege(Privilege.controlserver).RequiresPlayer()
            .WithArgs(api.ChatCommands.Parsers.OptionalWord("action"),api.ChatCommands.Parsers.OptionalWord("rig"))
            .HandleWith(Command);
        api.Event.SaveGameLoaded+=Restore;
        api.Event.GameWorldSave+=Store;
        api.Event.ServerRunPhase(EnumServerRunPhase.RunGame,()=>listener=api.Event.RegisterGameTickListener(Tick,100));
    }
    private void Restore()
    {
        byte[] data=api.WorldManager.SaveGame.GetData(ManifestKey);
        if(data==null) return;
        try
        {
            var saved = JsonSerializer.Deserialize<List<RigManifest>>(data) ?? new();
            if (saved.Any(m => m.Location == null || m.Location.Dimension != 0 || m.Location.Version is not (2 or 3)
                || string.IsNullOrEmpty(m.Id)) || saved.Select(m => m.Id).Distinct().Count() != saved.Count)
                throw new InvalidOperationException("Invalid rig manifest; existing saved data preserved.");
            foreach(var manifest in saved)
            {
                var rig=new PersistentChuteRig(api,manifest,Record); rigs.Add(rig);
                if(manifest.Enabled) QueueColumns(rig);
            }
            Record("RESTORE",rigs.Count+" persistent rigs; blocks and inventories retained");
        }
        catch(Exception ex) { manifestReadable=false; api.Logger.Error("[SignalsLink.Testing] Cannot restore rigs: "+ex); }
    }
    private void Store()
    {
        if (manifestReadable) api.WorldManager.SaveGame.StoreData(ManifestKey,JsonSerializer.SerializeToUtf8Bytes(rigs.Select(r=>r.Manifest).ToList()));
    }
    private void QueueColumns(PersistentChuteRig rig)
    {
        int size=api.WorldManager.ChunkSize;
        foreach(var p in new[]{rig.Layout.At(0,0,0),rig.Layout.At(8,0,0),rig.Layout.At(0,0,4),rig.Layout.At(8,0,4)})
        {
            var c=((int)Math.Floor((double)p.X/size),(int)Math.Floor((double)p.Z/size));
            if(requested.Add(c)) toLoad.Enqueue(c);
        }
    }
    private TextCommandResult Command(TextCommandCallingArgs args)
    {
        var player=args.Caller.Player;
        if(player?.WorldData.CurrentGameMode!=EnumGameMode.Creative) return TextCommandResult.Error("Creative mode required.");
        string action=((string)args[0]??"status").ToLowerInvariant(), id=(string)args[1];
        try
        {
            if (!manifestReadable) return TextCommandResult.Error("Saved rig manifest could not be read; see server log. Saved data was preserved.");
            if(action=="list") return TextCommandResult.Success("chute-basic: metered supply -> ordinary source chest -> tested chute -> ordinary target chest -> controlled drain -> recycler. /sltest add chute-basic creates another rig nearby.");
            if(action is "status" or "results")
                return TextCommandResult.Success(rigs.Count==0?"No persistent rigs. /sltest add chute-basic":
                    string.Join("\n",rigs.Where(r=>id==null||id=="all"||r.Manifest.Id==id).Select(r=>r.Loaded?r.Snapshot():r.Manifest.Id+" | "+(r.Manifest.Enabled?"loading":"stopped")+" | "+r.Layout.Location.Origin))
                    +"\nHarness avg/max ms: "+(callbacks>0?totalMs/callbacks:0).ToString("F3")+" / "+maxMs.ToString("F3")
                    +". Reports: "+api.GetOrCreateDataPath("signalslink-testing"));
            if(action=="upgrade") return Upgrade();
            if(action=="add" || action=="run" && (rigs.Count==0||id=="chute-basic")) return Add(player,id);
            if(action is "run" or "start" or "stop" or "cancel")
            {
                var selected=rigs.Where(r=>id==null||id=="all"||r.Manifest.Id==id).ToList();
                if(selected.Count==0) return TextCommandResult.Error("Unknown rig. /sltest status");
                bool enabled=action is "run" or "start";
                if(enabled && selected.Any(r=>r.Layout.Legacy)) return TextCommandResult.Error("Legacy rig requires /sltest upgrade.");
                foreach(var rig in selected)
                {
                    rig.Manifest.Enabled=enabled;
                    if(enabled) QueueColumns(rig); else rig.Pause();
                }
                Store(); return TextCommandResult.Success((enabled?"Started ":"Stopped ")+selected.Count+" rig(s). Assemblies retained.");
            }
            if(action=="clean")
            {
                var rig=rigs.FirstOrDefault(r=>r.Manifest.Id==id);
                if(rig==null) return TextCommandResult.Error("Specify one rig ID: /sltest clean chute-basic-1");
                if(rig.Manifest.Enabled) return TextCommandResult.Error("Stop this rig first: /sltest stop "+id);
                if(!rig.Cleanup()) return TextCommandResult.Error("Cleanup incomplete: unloaded or modified blocks were preserved.");
                rigs.Remove(rig); Store(); Record("CLEAN",id); return TextCommandResult.Success("Removed "+id+"; natural ground preserved.");
            }
            return TextCommandResult.Error("Use /sltest add chute-basic | start/stop [id|all] | status/results | upgrade | clean id");
        }
        catch(Exception ex) { Record("ERROR",ex.ToString()); return TextCommandResult.Error(ex.Message); }
    }
    private TextCommandResult Upgrade()
    {
        if(rigs.Count==0) return TextCommandResult.Error("No registered rigs to upgrade.");
        var replacements=rigs.Select(r=>new PersistentChuteRig(api,new RigManifest {
            Id=r.Manifest.Id, Location=r.Manifest.Location with { Version=3 }, Enabled=true },Record)).ToList();
        if(replacements.Any(r=>!r.Loaded))
        {
            foreach(var rig in replacements) QueueColumns(rig);
            return TextCommandResult.Success("Loading registered rig areas. Repeat /sltest upgrade in a moment. No blocks changed.");
        }
        for(int i=0;i<rigs.Count;i++)
        {
            string problem=replacements[i].Preflight(rigs[i]);
            if(problem!=null) return TextCommandResult.Error(rigs[i].Manifest.Id+": "+problem+". Nothing changed; load/clear the area and retry /sltest upgrade.");
            if(replacements.Where((_,j)=>j!=i).Any(r=>replacements[i].Layout.Cells().Any(r.Layout.Contains)))
                return TextCommandResult.Error("Expanded rig areas overlap. Nothing changed.");
        }
        EnsureReport();
        foreach(var rig in rigs) { rig.Manifest.Enabled=false; rig.Pause(); }
        Store();
        foreach(var rig in rigs)
            if(!rig.Cleanup()) return TextCommandResult.Error("Cleanup incomplete for "+rig.Manifest.Id+". All rigs stopped; inspect before retrying.");
        rigs.Clear(); rigs.AddRange(replacements);
        // Persist stopped manifests first: interrupted construction must never run unattended.
        foreach(var rig in rigs) { rig.Manifest.Enabled=false; rig.Manifest.State="building"; }
        Store();
        foreach(var rig in rigs) rig.Build();
        foreach(var rig in rigs) { rig.Manifest.Enabled=true; QueueColumns(rig); }
        Store(); Record("UPGRADE",rigs.Count+" rigs rebuilt as version 3; inventories and counters reset");
        return TextCommandResult.Success("Rebuilt "+rigs.Count+" rigs at their saved locations. IDs retained; inventory/counters reset; all running.");
    }
    private TextCommandResult Add(IPlayer player,string name)
    {
        if(name!=null&&name!="chute-basic") return TextCommandResult.Error("Unknown rig type: "+name);
        var playerPos=player.Entity.Pos.AsBlockPos;
        if(playerPos.dimension!=0) return TextCommandResult.Error("Create load-test rigs in the main dimension.");
        // Find actual ground near the player, not a platform at the player's flying height.
        int x=playerPos.X+4,z=playerPos.Z,y=playerPos.Y;
        var ba=api.World.BlockAccessor;
        bool found=false;
        for(int candidate=Math.Min(ba.MapSizeY-7,y+4);candidate>=Math.Max(1,y-64);candidate--)
        {
            var below=new BlockPos(x,candidate-1,z);
            if(ba.GetChunkAtBlockPos(below)==null) continue;
            if(ba.GetBlock(below).SideSolid[BlockFacing.UP.Index]==true && ba.GetBlock(new BlockPos(x,candidate,z)).Id==0)
            { y=candidate; found=true; break; }
        }
        if(!found) return TextCommandResult.Error("No loaded ground nearby. Stand near a level, clear 9 x 5 area.");
        var location=new ArenaLocation(x,y,z,0,3);
        var layout=new GroundLayout(location);
        if(rigs.Any(r=>layout.Cells().Any(r.Layout.Contains))) return TextCommandResult.Error("Another rig occupies this area. Move further away to add another.");
        int n=1; while(rigs.Any(r=>r.Manifest.Id=="chute-basic-"+n)) n++;
        var manifest=new RigManifest{Id="chute-basic-"+n,Location=location};
        var rig=new PersistentChuteRig(api,manifest,Record);
        string problem=rig.Preflight(); if(problem!=null) return TextCommandResult.Error(problem+". No blocks changed.");
        EnsureReport(); // Open report before world edits, so an inaccessible log directory cannot strand a new rig.
        rigs.Add(rig); Store();
        try { rig.Build(); QueueColumns(rig); }
        catch { manifest.Enabled=false; manifest.State="failed"; manifest.LastError="incomplete construction"; Store(); throw; }
        return TextCommandResult.Success("Created "+manifest.Id+" at "+location.Origin+". Runs repeatedly and resumes after reload. /sltest status");
    }
    private void Tick(float dt)
    {
        if(shuttingDown) return;
        long started=Stopwatch.GetTimestamp();
        if(toLoad.TryDequeue(out var column)) api.WorldManager.LoadChunkColumn(column.X,column.Z,true);
        foreach(var rig in rigs)
        {
            try { rig.Tick(dt); }
            catch(Exception ex)
            {
                string message=ex.GetType().Name+": "+ex.Message;
                if(rig.Manifest.LastError!=message)
                { rig.Manifest.LastError=message; rig.Manifest.State="failed"; rig.Manifest.Failures++; Record("ERROR",rig.Manifest.Id+" | "+ex); }
            }
        }
        double ms=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        callbacks++; totalMs+=ms; maxMs=Math.Max(maxMs,ms); summarySeconds+=Math.Max(0,dt);
        if(summarySeconds>=60)
        {
            summarySeconds=0;
            foreach(var rig in rigs.Where(r=>r.Manifest.Enabled&&r.Loaded)) Record("SUMMARY",rig.Snapshot());
            Record("TIMING",FormattableString.Invariant($"harnessCallbacks={callbacks};avgMs={totalMs/callbacks:F3};maxMs={maxMs:F3}"));
            Store();
        }
    }
    private void EnsureReport()
    {
        if(report!=null && reportDay == DateTime.UtcNow.Date) return;
        report?.Dispose(); report=null;
        reportDay=DateTime.UtcNow.Date;
        report=new RigReport(api.GetOrCreateDataPath("signalslink-testing"),"continuous",api.WorldManager.SaveGame.SavegameIdentifier,
            "testing="+Mod.Info.Version+";signalslink="+api.ModLoader.GetMod("signalslink")?.Info.Version);
    }
    private void Record(string kind,string text)
    {
        if(logFailed) return;
        try { EnsureReport(); report.Write(kind, DateTime.UtcNow.ToString("O") + " | " + text); }
        catch(Exception ex) { logFailed=true; api.Logger.Error("[SignalsLink.Testing] Report unavailable; rigs remain active: "+ex.Message); }
    }
    public override void Dispose()
    {
        shuttingDown=true;
        if(api!=null)
        {
            api.Event.SaveGameLoaded-=Restore; api.Event.GameWorldSave-=Store;
            if(listener!=0) api.Event.UnregisterGameTickListener(listener);
        }
        // World save already persists enabled state and BE inventories. Never toggle switches or dismantle on shutdown.
        report?.Dispose(); base.Dispose();
    }
}
