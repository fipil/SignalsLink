using System.Globalization;
using System.Text;

namespace SignalsLink.Testing;

public sealed class RigReport : IDisposable
{
    private readonly StreamWriter writer;
    public string Path { get; }
    public RigReport(string directory, string rig, string world, string versions)
    {
        Directory.CreateDirectory(directory);
        Path = System.IO.Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N")[..8] + ".txt");
        writer = new StreamWriter(Path, false, new UTF8Encoding(false)) { AutoFlush = true };
        try
        {
            Write("FORMAT", "signalslink-rig-v1");
            Write("RIG", rig);
            Write("WORLD", world);
            Write("VERSIONS", versions);
        }
        catch { writer.Dispose(); throw; }
    }
    public void Write(string kind, string message) => writer.WriteLine(kind + " | " + OneLine(message));
    public static string OneLine(string message) => (message ?? "").Replace("\r", "\\r").Replace("\n", "\\n");
    public void Dispose() => writer.Dispose();
}
