using Newtonsoft.Json.Linq;
using SignalsTubes.src.circuit;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SignalsTubes.src.programtube;

/// <summary>
/// A tube as one self-contained JSON: its item, texts, locks, program, every program it references
/// (transitively, by id) and the tubes soldered into it, the same way down. Import rebuilds all of it
/// under new program ids, with the importer as the author of every tube inside.
/// </summary>
public static class TubeTransfer
{
    public const int Format = 1;

    public static JObject Export(ItemStack stack, ICoreAPI api)
    {
        var o = new JObject
        {
            ["format"] = Format,
            ["item"] = stack.Collectible.Code.ToString(),
            ["name"] = stack.Attributes.GetString(TubeProgram.NameKey),
            ["description"] = stack.Attributes.GetString(TubeProgram.DescriptionKey),
            ["authorName"] = TubeProgram.AuthorName(stack),
            ["lockCopy"] = TubeProgram.LockCopy(stack),
            ["lockView"] = TubeProgram.LockView(stack)
        };
        var program = TubeProgram.Get(stack, api);
        if (program != null)
        {
            o["program"] = JObject.Parse(ProgramCodec.ToJson(program));
            var referenced = new JObject();
            Collect(program, api, referenced);
            if (referenced.Count > 0) o["programs"] = referenced;
        }
        var soldered = TubeProgram.Soldered(stack, api.World);
        if (soldered.Count > 0) o["soldered"] = new JArray(soldered.Select(s => Export(s, api)));
        return o;
    }

    // every program the given one points at, and theirs, by id
    private static void Collect(CircuitProgram program, ICoreAPI api, JObject into)
    {
        foreach (string id in References(program))
        {
            if (id == null || into.ContainsKey(id)) continue;
            var nested = ProgramStore.Of(api)?.Get(id);
            if (nested == null) continue;
            into[id] = JObject.Parse(ProgramCodec.ToJson(nested));
            Collect(nested, api, into);
        }
    }

    private static IEnumerable<string> References(CircuitProgram p) =>
        p.Components.Where(c => c.Ref != null).Select(c => c.Ref).Concat(p.Groups.Where(g => g.Ref != null).Select(g => g.Ref)).Distinct();

    /// <summary>Builds the tube; null when the item is unknown. Throws FormatException on a broken file.</summary>
    public static ItemStack Import(JObject o, ICoreServerAPI sapi, string authorUid, string authorName)
    {
        if ((int?)o["format"] != Format) throw new FormatException("unsupported format " + o["format"]);
        var item = sapi.World.GetItem(new AssetLocation((string)o["item"] ?? "signalstubes:programtube-fire"));
        if (item == null) return null;
        var stack = new ItemStack(item);
        if (o["program"] is JObject programJson)
        {
            var store = ProgramStore.Of(sapi);
            // referenced programs first, deepest first, each under the id the store gives it here
            var exported = o["programs"] as JObject ?? new JObject();
            var newIds = new Dictionary<string, string>();
            foreach (var id in exported.Properties().Select(p => p.Name)) ImportReferenced(id, exported, store, newIds, new HashSet<string>());
            var program = Remapped(ProgramCodec.FromJson(Newtonsoft.Json.JsonConvert.SerializeObject(programJson, Newtonsoft.Json.Formatting.None)), newIds);
            TubeProgram.Set(stack, program, store, authorUid, authorName, (bool?)o["lockCopy"] == true || program.HasReferences, (bool?)o["lockView"] == true);
        }
        if ((string)o["name"] is { Length: > 0 } name) stack.Attributes.SetString(TubeProgram.NameKey, name);
        if ((string)o["description"] is { Length: > 0 } description) stack.Attributes.SetString(TubeProgram.DescriptionKey, description);
        if (o["soldered"] is JArray soldered)
            TubeProgram.SetSoldered(stack, soldered.OfType<JObject>().Select(s => Import(s, sapi, authorUid, authorName)).Where(s => s != null));
        return stack;
    }

    private static string ImportReferenced(string id, JObject exported, ProgramStore store, Dictionary<string, string> newIds, HashSet<string> visiting)
    {
        if (newIds.TryGetValue(id, out string known)) return known;
        if (exported[id] is not JObject json) return id;   // not in the file: keep the id and hope the server has it
        if (!visiting.Add(id)) throw new FormatException("program " + id + " references itself");
        var program = ProgramCodec.FromJson(Newtonsoft.Json.JsonConvert.SerializeObject(json, Newtonsoft.Json.Formatting.None));
        foreach (string inner in References(program)) ImportReferenced(inner, exported, store, newIds, visiting);
        string newId = store.Put(Remapped(program, newIds));
        newIds[id] = newId;
        return newId;
    }

    private static CircuitProgram Remapped(CircuitProgram program, Dictionary<string, string> newIds)
    {
        foreach (var c in program.Components) if (c.Ref != null && newIds.TryGetValue(c.Ref, out string id)) c.Ref = id;
        foreach (var g in program.Groups) if (g.Ref != null && newIds.TryGetValue(g.Ref, out string id)) g.Ref = id;
        return program;
    }
}
