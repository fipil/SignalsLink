using SignalsLink.src.signals.bridges.iw;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.Tests;

/// <summary>
/// What the chopper bridge demands of Immersive Woodworking before it touches anything, checked
/// against look-alike types: the whole surface, a chopper whose head is out of reach, and one
/// whose hopper gate changed shape.
/// </summary>
public class IwSurfaceTests
{
    [Fact]
    public void The_known_chopper_feeds_logs_and_takes_heads()
    {
        IwSurface surface = IwSurface.Probe(typeof(WholeChopper), typeof(ChopperPart));

        Assert.True(surface.CanFeedLogs);
        Assert.True(surface.CanFitHead);
        Assert.Equal("", surface.Complaint);
        Assert.NotNull(surface.UpdateRenderer);
    }

    [Fact]
    public void The_fingerprint_is_the_one_written_against()
    {
        // Immersive Woodworking 1.3.6 and 1.3.14; a change here means KnownGood needs a look.
        Assert.Equal("30091c0ebced", IwSurface.Probe(typeof(WholeChopper), typeof(ChopperPart)).Surface);
    }

    [Fact]
    public void A_head_out_of_reach_still_leaves_the_logs()
    {
        IwSurface surface = IwSurface.Probe(typeof(HeadlessChopper), typeof(ChopperPart));

        Assert.True(surface.CanFeedLogs);
        Assert.False(surface.CanFitHead);
        Assert.NotEqual(IwSurface.Probe(typeof(WholeChopper), typeof(ChopperPart)).Surface, surface.Surface);
    }

    [Fact]
    public void A_changed_hopper_gate_stands_the_bridge_down()
    {
        IwSurface surface = IwSurface.Probe(typeof(OldChopper), typeof(ChopperPart));

        Assert.False(surface.CanFeedLogs);
        Assert.False(surface.CanFitHead);
        Assert.Contains("GetAutoPushIntoSlot", surface.Complaint);
    }

    [Fact]
    public void A_part_without_a_principal_stands_the_bridge_down()
    {
        IwSurface surface = IwSurface.Probe(typeof(WholeChopper), typeof(LostPart));
        Assert.False(surface.CanFeedLogs);
        Assert.Contains("Principal", surface.Complaint);
    }

    [Fact]
    public void Missing_types_stand_the_bridge_down()
    {
        Assert.False(IwSurface.Probe(null, typeof(ChopperPart)).CanFeedLogs);
        Assert.False(IwSurface.Probe(typeof(WholeChopper), null).CanFeedLogs);
    }

    [Fact]
    public void The_fingerprint_does_not_depend_on_the_order_found()
    {
        Assert.Equal(IwSurface.Fingerprint("A:int", "B:int"), IwSurface.Fingerprint("B:int", "A:int"));
        Assert.NotEqual(IwSurface.Fingerprint("A:int"), IwSurface.Fingerprint("A:long"));
    }

    // ---------------------------------------------------------------- look-alikes

    class InventoryChopper : InventoryGeneric
    {
        public InventoryChopper() : base(1, "test-chopper", null) { }
    }

    class ChopperPart
    {
        public BlockPos Principal { get; set; }
    }

    class LostPart
    {
        public Vec3i Principal { get; set; }
    }

    class WholeChopper
    {
        private ItemStack headStack = null;
        public ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot, BlockPos dockCell) => null;
        public InventoryChopper Inventory => null;
        public ItemStack HeadStack => headStack;
        internal bool HasArm => true;
        public void UpdateRenderer() { }
    }

    class HeadlessChopper
    {
        public ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot, BlockPos dockCell) => null;
        public InventoryChopper Inventory => null;
    }

    class OldChopper
    {
        public ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot) => null;
        public InventoryChopper Inventory => null;
    }
}
