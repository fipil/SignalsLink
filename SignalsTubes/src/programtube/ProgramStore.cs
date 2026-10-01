using SignalsTubes.src.circuit;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace SignalsTubes.src.programtube;

/// <summary>
/// Programs never leave the server: they live here, keyed by id, saved with the world. A tube item
/// carries only the id and the public part (pins, names, fingerprint, author, locks).
/// </summary>
public class ProgramStore : ModSystem
{
    private const string SaveKey = "signalstubes-programs";
    private ICoreServerAPI sapi;
    private Dictionary<string, string> json = new();
    private readonly Dictionary<string, CircuitProgram> parsed = new();

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        api.Event.SaveGameLoaded += () =>
        {
            json = api.WorldManager.SaveGame.GetData<Dictionary<string, string>>(SaveKey) ?? new Dictionary<string, string>();
            parsed.Clear();
        };
        api.Event.GameWorldSave += () => api.WorldManager.SaveGame.StoreData(SaveKey, json);
    }

    public static ProgramStore Of(ICoreAPI api) => api?.Side == EnumAppSide.Server ? api.ModLoader.GetModSystem<ProgramStore>() : null;

    /// <summary>Shared read-only instance; simulators copy what they mutate.</summary>
    public CircuitProgram Get(string id)
    {
        if (id == null) return null;
        lock (json)
        {
            if (parsed.TryGetValue(id, out var program)) return program;
            if (!json.TryGetValue(id, out string text)) return null;
            try { program = ProgramCodec.FromJson(text); }
            catch (FormatException e) { sapi.Logger.Warning("Signals Tubes: program {0} is unreadable: {1}", id, e.Message); return null; }
            parsed[id] = program;
            return program;
        }
    }

    public string Put(CircuitProgram program)
    {
        string id = Guid.NewGuid().ToString("N");
        lock (json)
        {
            json[id] = ProgramCodec.ToJson(program);
            parsed[id] = program;
        }
        return id;
    }

    public void Remove(string id)
    {
        if (id == null) return;
        lock (json) { json.Remove(id); parsed.Remove(id); }
    }
}
