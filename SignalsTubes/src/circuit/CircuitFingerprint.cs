using System.Security.Cryptography;
using System.Text;

namespace SignalsTubes.src.circuit;

/// <summary>
/// Identity of the wiring, not of its layout: the same circuit gives the same hash however it was
/// built, numbered or walked. Node labels are refined Weisfeiler-Lehman style until they carry
/// the whole neighbourhood, then everything is written in sorted form and hashed.
/// Delay register contents and toggle memory are not part of the identity.
/// </summary>
public static class CircuitFingerprint
{
    public static string Compute(CircuitProgram program)
    {
        var p = CircuitSimplifier.Simplify(program);
        int n = p.NodeCount;
        var labels = new string[n];
        for (int i = 0; i < n; i++) labels[i] = "";
        foreach (var pin in p.Pins)
            if (pin.Node >= 0) labels[pin.Node] += $"pin{pin.Index}:{pin.Role};";
        // Parameter pins name the component, so they attach to its node labels instead.
        var pinOfComponent = new Dictionary<int, string>();
        foreach (var pin in p.Pins)
            if (pin.Component >= 0) pinOfComponent[pin.Component] = $"pin{pin.Index}:{pin.Role}";
        for (int i = 0; i < n; i++) labels[i] = Hash(labels[i]);

        for (int round = 0; round < n; round++)
        {
            var next = new string[n];
            var parts = new List<string>[n];
            for (int i = 0; i < n; i++) parts[i] = new List<string>();
            foreach (var l in p.Links)
            {
                parts[l.A].Add($"L{l.Att}/{l.RevAtt}>{labels[l.B]}");
                parts[l.B].Add($"L{l.RevAtt}/{l.Att}>{labels[l.A]}");
            }
            for (int c = 0; c < p.Components.Count; c++)
            {
                var comp = p.Components[c];
                string head = ComponentHead(comp, c, pinOfComponent);
                string ports = string.Join(",", comp.Nodes.Select(x => labels[x]));
                for (int port = 0; port < comp.Nodes.Length; port++)
                    parts[comp.Nodes[port]].Add($"C{head}@{port}[{ports}]");
                if (comp.ParamNode >= 0) parts[comp.ParamNode].Add($"C{head}@param[{ports}]");
            }
            bool changed = false;
            for (int i = 0; i < n; i++)
            {
                parts[i].Sort(StringComparer.Ordinal);
                next[i] = Hash(labels[i] + "|" + string.Join(";", parts[i]));
                changed |= next[i] != labels[i];
            }
            labels = next;
            if (!changed) break;
        }

        var lines = new List<string>();
        foreach (var l in p.Links)
        {
            string ab = $"L {labels[l.A]} {l.Att} {labels[l.B]} {l.RevAtt}";
            string ba = $"L {labels[l.B]} {l.RevAtt} {labels[l.A]} {l.Att}";
            lines.Add(string.CompareOrdinal(ab, ba) <= 0 ? ab : ba);
        }
        for (int c = 0; c < p.Components.Count; c++)
        {
            var comp = p.Components[c];
            lines.Add($"C {ComponentHead(comp, c, pinOfComponent)} {string.Join(" ", comp.Nodes.Select(x => labels[x]))}{(comp.ParamNode >= 0 ? " <" + labels[comp.ParamNode] : "")}");
        }
        foreach (var pin in p.Pins)
            if (pin.Node >= 0) lines.Add($"P {pin.Index} {pin.Role} {labels[pin.Node]}");
        lines.Sort(StringComparer.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines))));
    }

    private static string ComponentHead(Component c, int index, Dictionary<int, string> pinOfComponent)
    {
        // A switch or delay pulled out to a pin is defined by the pin, not by its imprinted setting.
        bool exposed = pinOfComponent.TryGetValue(index, out string pin);
        string param = exposed ? pin : c.ParamNode >= 0 ? "node" : c.Kind == ComponentKind.Toggle ? "-" : c.Param.ToString();
        return c.Kind == ComponentKind.Tube ? $"Tube:{c.Ref}" : $"{c.Kind}:{param}";
    }

    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..16];
}
