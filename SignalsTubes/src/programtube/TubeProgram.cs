using Newtonsoft.Json.Linq;
using SignalsTubes.src.circuit;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SignalsTubes.src.programtube;

public enum TubeProgramError { None = 0, Cycle = 1, ExpandedSize = 2, Depth = 3 }

/// <summary>Public part of a tube's pin: what clients may know without the program.</summary>
public sealed record PublicPin(int Index, PinRole Role, string Name);

/// <summary>
/// What a tube stack carries. Imprinted tubes hold a program id (program in <see cref="ProgramStore"/>)
/// plus the public part; creative samples still embed their program as JSON and are read directly.
/// </summary>
public static class TubeProgram
{
    public const string IdKey = "programId", NameKey = "programName", DescriptionKey = "programDescription";
    public const string ErrorKey = "programError";
    public static TubeProgramError Error(ItemStack stack) => (TubeProgramError)(stack?.Attributes.GetInt(ErrorKey) ?? 0);
    public static bool HasError(ItemStack stack) => Error(stack) != TubeProgramError.None;
    public static void SetError(ItemStack stack, TubeProgramError error)
    {
        if (stack == null) return;
        if (error != TubeProgramError.None) stack.Attributes.SetInt(ErrorKey, (int)error);
        else stack.Attributes.RemoveAttribute(ErrorKey);
    }
    public const string AuthorKey = "author", AuthorNameKey = "authorName", LockCopyKey = "lockCopy", LockViewKey = "lockView";
    private const string PinsKey = "pins", FingerprintKey = "fingerprint", SolderedKey = "soldered";

    private sealed record Embedded(CircuitProgram Program, string Fingerprint, IReadOnlyList<PublicPin> Pins);
    private static readonly Dictionary<string, Embedded> embeddedCache = new();
    private static readonly string[] RoleNames = { "in", "out", "switch", "delay" };

    public static bool IsBlank(ItemStack stack) =>
        !stack.Attributes.HasAttribute(IdKey) && !stack.Attributes.HasAttribute(ProgramCodec.AttributeKey);

    /// <summary>The program; server only for imprinted tubes, anywhere for embedded ones. Shared instance.</summary>
    public static CircuitProgram Get(ItemStack stack, ICoreAPI api)
    {
        if (stack == null) return null;
        string id = stack.Attributes.GetString(IdKey);
        if (id != null) return ProgramStore.Of(api)?.Get(id);
        return LookupEmbedded(stack)?.Program;
    }

    public static string Fingerprint(ItemStack stack) =>
        stack?.Attributes.GetString(FingerprintKey) ?? LookupEmbedded(stack)?.Fingerprint;

    private const int PinCacheCapacity = 256;
    private static readonly Dictionary<string, IReadOnlyList<PublicPin>> pinCache = new(StringComparer.Ordinal);
    private static readonly Queue<string> pinCacheOrder = new();

    // Raw JSON is the key: replacing synced attributes, renaming or erasing needs no invalidation hook.
    // Only immutable raw data is shared, never ItemStacks, worlds or translated names.
    public static IReadOnlyList<PublicPin> Pins(ItemStack stack)
    {
        string json = stack?.Attributes.GetString(PinsKey);
        if (json != null)
        {
            lock (pinCache)
            {
                if (pinCache.TryGetValue(json, out var cached)) return cached;
                IReadOnlyList<PublicPin> pins;
                try { pins = ParsePins(JArray.Parse(json)).AsReadOnly(); }
                catch (Exception) { pins = Array.Empty<PublicPin>(); }
                if (pinCache.Count >= PinCacheCapacity) pinCache.Remove(pinCacheOrder.Dequeue());
                pinCache.Add(json, pins);
                pinCacheOrder.Enqueue(json);
                return pins;
            }
        }
        return LookupEmbedded(stack)?.Pins ?? Array.Empty<PublicPin>();
    }

    /// <summary>Bit i set = pin i present.</summary>
    public static int PinMask(ItemStack stack) => Pins(stack).Aggregate(0, (m, p) => m | 1 << p.Index);

    public static int PinMaskOf(CircuitProgram p) => p.Pins.Aggregate(0, (m, pin) => m | 1 << pin.Index);

    public static string Author(ItemStack stack) => stack?.Attributes.GetString(AuthorKey);
    public static string AuthorName(ItemStack stack) => stack?.Attributes.GetString(AuthorNameKey);
    public static bool LockCopy(ItemStack stack) => stack?.Attributes.GetBool(LockCopyKey) == true;
    public static bool LockView(ItemStack stack) => stack?.Attributes.GetBool(LockViewKey) == true;
    public static bool IsAuthor(ItemStack stack, string playerUid) => Author(stack) == null || Author(stack) == playerUid;

    /// <summary>Writes an imprinted program: stored on the server, public part on the stack.</summary>
    public static void Set(ItemStack stack, CircuitProgram program, ProgramStore store, string authorUid, string authorName, bool lockCopy, bool lockView)
    {
        var a = stack.Attributes;
        SetError(stack, TubeProgramError.None);
        a.RemoveAttribute(ProgramCodec.AttributeKey);
        a.SetString(IdKey, store.Put(program));
        a.SetString(FingerprintKey, CircuitFingerprint.Compute(program));
        a.SetString(PinsKey, Newtonsoft.Json.JsonConvert.SerializeObject(new JArray(program.Pins.OrderBy(p => p.Index).Select(p =>
        {
            var o = new JObject { ["i"] = p.Index, ["r"] = RoleNames[(int)p.Role] };
            if (!string.IsNullOrEmpty(p.Name)) o["name"] = p.Name;
            return o;
        })), Newtonsoft.Json.Formatting.None));
        if (authorUid != null) a.SetString(AuthorKey, authorUid); else a.RemoveAttribute(AuthorKey);
        if (authorName != null) a.SetString(AuthorNameKey, authorName); else a.RemoveAttribute(AuthorNameKey);
        a.SetBool(LockCopyKey, lockCopy);
        a.SetBool(LockViewKey, lockView);
    }

    /// <summary>Back to a blank tube. The stored program stays; copies may still point at it.</summary>
    public static void Clear(ItemStack stack)
    {
        SetError(stack, TubeProgramError.None);
        foreach (var key in new[] { IdKey, NameKey, DescriptionKey, AuthorKey, AuthorNameKey, LockCopyKey, LockViewKey, PinsKey, FingerprintKey, SolderedKey, ProgramCodec.AttributeKey })
            stack.Attributes.RemoveAttribute(key);
    }

    /// <summary>Tubes soldered into this one; they come back out when it is erased.</summary>
    public static List<ItemStack> Soldered(ItemStack stack, IWorldAccessor world)
    {
        var list = new List<ItemStack>();
        var tree = stack?.Attributes.GetTreeAttribute(SolderedKey);
        if (tree == null) return list;
        for (int i = 0; i < tree.GetInt("n"); i++)
        {
            var item = tree.GetItemstack("s" + i);
            if (item == null) continue;
            if (world != null) item.ResolveBlockOrItem(world);
            list.Add(item);
        }
        return list;
    }

    public static void SetSoldered(ItemStack stack, IEnumerable<ItemStack> items)
    {
        var tree = new Vintagestory.API.Datastructures.TreeAttribute();
        int n = 0;
        foreach (var item in items) tree.SetItemstack("s" + n++, item);
        tree.SetInt("n", n);
        if (n == 0) stack.Attributes.RemoveAttribute(SolderedKey); else stack.Attributes[SolderedKey] = tree;
    }

    private static List<PublicPin> ParsePins(JArray arr) => arr.Select(t => new PublicPin((int)t["i"],
        (PinRole)Math.Max(0, Array.IndexOf(RoleNames, (string)t["r"])), (string)t["name"])).ToList();

    private static Embedded LookupEmbedded(ItemStack stack)
    {
        string json = stack?.Attributes.GetString(ProgramCodec.AttributeKey);
        if (json == null) return null;
        lock (embeddedCache)
        {
            if (embeddedCache.TryGetValue(json, out var entry)) return entry;
            if (embeddedCache.Count >= 256) embeddedCache.Clear();
            try
            {
                var program = ProgramCodec.FromJson(json);
                entry = new Embedded(program, CircuitFingerprint.Compute(program),
                    program.Pins.OrderBy(p => p.Index).Select(p => new PublicPin(p.Index, p.Role, p.Name)).ToList().AsReadOnly());
            }
            catch (FormatException)
            {
                entry = null;  // a broken program renders and behaves as a blank tube
            }
            embeddedCache[json] = entry;
            return entry;
        }
    }

    /// <summary>Player text shown in VTML tooltips: '<' would otherwise start a tag.</summary>
    public static string Vtml(string text) => text?.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>A pin name given by a machine, stored as lang keys so every viewer reads it in their own language.</summary>
    public const string LangPrefix = "lang:";
    public static string LangRef(string key, params string[] argKeys) => LangPrefix + string.Join("|", argKeys.Prepend(key));
    public static bool IsLangRef(string name) => name != null && name.StartsWith(LangPrefix);

    /// <summary>A stored name as text: lang refs translated (null langCode = this side's language), others as they are.</summary>
    public static string Resolve(string name, string langCode = null)
    {
        if (!IsLangRef(name)) return name;
        string Get(string key, object[] args) => langCode == null ? Lang.Get(key, args) : Lang.GetL(langCode, key, args);
        var keys = name[LangPrefix.Length..].Split('|');
        return Get(keys[0], keys.Skip(1).Select(k => (object)Get(k, Array.Empty<object>())).ToArray());
    }

    /// <summary>The name of a soldered tube left unnamed, in the given language.</summary>
    public static string DefaultTubeName(string langCode = null) => langCode == null
        ? Lang.GetMatching("signalstubes:item-programtube-fire")
        : Lang.GetMatchingL(langCode, "signalstubes:item-programtube-fire");
}
