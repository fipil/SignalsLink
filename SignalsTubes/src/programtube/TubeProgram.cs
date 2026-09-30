using SignalsTubes.src.circuit;
using Vintagestory.API.Common;

namespace SignalsTubes.src.programtube;

/// <summary>The program carried by a tube stack, parsed once per distinct JSON. A blank tube has none.</summary>
public static class TubeProgram
{
    private sealed record Entry(CircuitProgram Program, string Fingerprint, int PinMask);
    private static readonly Dictionary<string, Entry> cache = new();

    public static bool IsBlank(ItemStack stack) => !stack.Attributes.HasAttribute(ProgramCodec.AttributeKey);

    public static void Set(ItemStack stack, CircuitProgram program) =>
        stack.Attributes.SetString(ProgramCodec.AttributeKey, ProgramCodec.ToJson(program));

    /// <summary>Shared, read-only instance; a simulator copies what it mutates.</summary>
    public static CircuitProgram Get(ItemStack stack) => Lookup(stack)?.Program;

    public static string Fingerprint(ItemStack stack) => Lookup(stack)?.Fingerprint;

    /// <summary>Bit i set = pin i present.</summary>
    public static int PinMask(ItemStack stack) => Lookup(stack)?.PinMask ?? 0;

    public static int PinMaskOf(CircuitProgram p) => p.Pins.Aggregate(0, (m, pin) => m | 1 << pin.Index);

    private static Entry Lookup(ItemStack stack)
    {
        string json = stack?.Attributes.GetString(ProgramCodec.AttributeKey);
        if (json == null) return null;
        lock (cache)
        {
            if (cache.TryGetValue(json, out var entry)) return entry;
            if (cache.Count >= 256) cache.Clear();
            try
            {
                var program = ProgramCodec.FromJson(json);
                entry = new Entry(program, CircuitFingerprint.Compute(program), PinMaskOf(program));
            }
            catch (FormatException)
            {
                entry = null;  // a broken program renders and behaves as a blank tube
            }
            cache[json] = entry;
            return entry;
        }
    }
}
