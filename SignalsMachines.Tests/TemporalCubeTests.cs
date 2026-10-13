using System.Reflection;
using SignalsMachines.src.parts;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SignalsMachines.Tests;

public class TemporalCubeTests
{
    [Fact]
    public void FinalGrindingHitKeepsCubeBuffableUntilWheelFinishes()
    {
        var (cube, slot, world, callbacks, crystal) = Setup();
        for (int i = 1; i < ItemTemporalCube.GrindHits; i++)
        {
            cube.DamageItem(world, null, slot);
            Assert.Equal(i, ItemTemporalCube.Progress(slot.Itemstack));
            Assert.Empty(callbacks);
        }
        cube.DamageItem(world, null, slot);
        Assert.Same(cube, slot.Itemstack.Collectible);
        // Vanilla does this after DamageItem, including on the final grinding hit.
        var buffable = slot.Itemstack.Collectible.GetBehavior<CollectibleBehaviorBuffable>();
        Assert.Empty(buffable.GetItemBuffs(slot.Itemstack));
        buffable.AddBuff(slot.Itemstack, new AppliedCollectibleBuff
        {
            Code = "sharpened", Multiplier = 1.01f, StatCode = "critchance"
        }, EnumBuffAddType.AddOnDuplicate);
        Assert.Single(callbacks)(0);
        Assert.Same(crystal, slot.Itemstack.Collectible);
        Assert.False(slot.Itemstack.Attributes.HasAttribute("buffs"));
        Assert.Equal(1, slot.Itemstack.StackSize);
    }

    [Fact]
    public void DeferredCompletionDoesNotOverwriteAChangedSlot()
    {
        var (cube, slot, world, callbacks, _) = Setup();
        slot.Itemstack.Attributes.SetInt(ItemTemporalCube.ProgressKey, ItemTemporalCube.GrindHits - 1);
        cube.DamageItem(world, null, slot);
        var replacement = new ItemStack(cube);
        slot.Itemstack = replacement;
        Assert.Single(callbacks)(0);
        Assert.Same(replacement, slot.Itemstack);
        Assert.Equal(0, ItemTemporalCube.Progress(replacement));
    }

    private static (ItemTemporalCube, DummySlot, IWorldAccessor, List<Action<float>>, Item) Setup()
    {
        var cube = new ItemTemporalCube { Code = new AssetLocation("signalsmachines:temporalcube") };
        cube.CollectibleBehaviors = new CollectibleBehavior[] { new CollectibleBehaviorBuffable(cube) };
        var crystal = new Item { Code = ItemTemporalCube.Crystal };
        var callbacks = new List<Action<float>>();
        var world = DispatchProxy.Create<IWorldAccessor, ProgramTubeTests.Proxy>();
        ((ProgramTubeTests.Proxy)(object)world).Call = (method, args) =>
        {
            if (method.Name == "get_Side") return EnumAppSide.Server;
            if (method.Name == "GetItem") return crystal;
            if (method.Name == "RegisterCallback") { callbacks.Add((Action<float>)args[0]); return 1L; }
            return null;
        };
        return (cube, new DummySlot { Itemstack = new ItemStack(cube) }, world, callbacks, crystal);
    }

    private class DummySlot : ItemSlot
    {
        public DummySlot() : base(null) { }
        public override void MarkDirty() { }
    }
}
