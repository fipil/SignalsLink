using Vintagestory.API.Common;

namespace SignalsTubes.src.imprint;

/// <summary>Distinct highlight layers for each loaded imprinter, scoped to the server session.</summary>
public sealed class ImprinterHighlights : ModSystem
{
    private int nextSlot = 7311;
    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

    public int Allocate()
    {
        int first = nextSlot;
        nextSlot = checked(nextSlot + 3);
        return first;
    }
}
