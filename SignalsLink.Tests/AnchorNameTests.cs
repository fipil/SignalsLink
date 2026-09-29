using System.IO;
using System.Text;
using SignalsLink.src.signals.chunkanchor;
using Vintagestory.API.Datastructures;
using static SignalsLink.Tests.AnchorLifecycleTests;

namespace SignalsLink.Tests;

public class AnchorNameTests
{
    [Fact]
    public void Name_packet_is_saved_with_block_and_can_be_cleared()
    {
        var f = new Fixture(); var be = f.Create(1000);
        be.OnReceivedClientPacket(f.Player, BEChunkAnchor.PacketIdSetName, Encoding.UTF8.GetBytes("  Severní továrna  "));
        var tree = new TreeAttribute(); be.ToTreeAttributes(tree);
        be.SetAnchorName("");
        be.FromTreeAttributes(tree, f.Api.World);
        Assert.Equal("Severní továrna", be.AnchorName);
        Assert.Equal(be.AnchorName, f.Manager.NameAt(f.Pos));
        be.OnReceivedClientPacket(f.Player, BEChunkAnchor.PacketIdSetName, Array.Empty<byte>());
        Assert.Equal("", be.AnchorName);
        Assert.Equal("", f.Manager.NameAt(f.Pos));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Scheduler_keeps_name_across_restart_without_loading_block(bool sleeping)
    {
        var f = new Fixture();
        Call(f.Manager, "Restore");
        f.Manager.SetName(f.Pos, "Lom");
        if (sleeping) f.Manager.Sleep(f.Pos, new[] { 0L }, 26.5, false);
        else f.Manager.SetColumns(f.Pos, new[] { 0L });
        Call(f.Manager, "Store");
        var restored = new Fixture { Saved = f.Saved };
        Call(restored.Manager, "Restore");
        Assert.Null(restored.Entity);
        Assert.Equal("Lom", restored.Manager.NameAt(restored.Pos));
        Assert.Equal(sleeping, restored.Manager.IsAsleep(restored.Pos));
    }

    [Fact]
    public void Older_sleep_schedule_without_names_still_loads()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(-2); writer.Write(1);
            writer.Write(0); writer.Write(1); writer.Write(0);
            writer.Write(26.5); writer.Write(false);
            writer.Write(1); writer.Write(0L);
        }
        var f = new Fixture { Saved = stream.ToArray() };
        Call(f.Manager, "Restore");
        Assert.Equal("", f.Manager.NameAt(f.Pos));
        Assert.Equal(26.5, f.Manager.WakesAt(f.Pos));
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(4.5, "04:30")]
    [InlineData(23.999, "23:59")]
    [InlineData(24, "00:00")]
    [InlineData(1002.25, "18:15")]
    public void Log_clock_uses_time_of_day(double hour, string expected)
        => Assert.Equal(expected, AnchorDisplay.TimeOfDay(hour));

    [Fact]
    public void Duration_does_not_wrap_at_midnight()
        => Assert.Equal("24:30", AnchorDisplay.Duration(24.5));
}
