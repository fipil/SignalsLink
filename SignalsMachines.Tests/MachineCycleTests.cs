using System.Reflection;
using SignalsMachines.src.craftingmachine;
using signals.src.signalNetwork;
using SignalsTubes.Tests.fidelity;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using static SignalsTubes.Tests.fidelity.SignalsRig;

namespace SignalsMachines.Tests;

/// <summary>A whole cycle on the real Signals network: wires drive the machine, a recipe is made, an overload burns.</summary>
public class MachineCycleTests
{
    private static readonly Item Stick = new() { Code = new AssetLocation("game:stick"), MaxStackSize = 64, ItemId = 11 };
    private static readonly Item Flint = new() { Code = new AssetLocation("game:flint"), MaxStackSize = 64, ItemId = 12 };
    private static readonly Item Dust = new() { Code = new AssetLocation("signalsmachines:burntdust"), MaxStackSize = 64, ItemId = 13 };

    private static CraftingRecipeIngredient Ingredient(Item item, int quantity = 1) => new()
    {
        Type = EnumItemClass.Item, Code = item.Code, Quantity = quantity, ResolvedItemStack = new ItemStack(item, quantity), MatchingType = EnumRecipeMatchType.Exact
    };

    private static (SignalsRig rig, BECraftingMachine machine, BlockPos clutch, BlockPos crystal, BlockPos strength) Setup()
    {
        var rig = new SignalsRig();
        var field = typeof(CollectibleObject).GetField("api", BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var item in new[] { Stick, Flint, Dust }) field.SetValue(item, rig.Api);
        rig.Items[Dust.Code.ToString()] = Dust;
        rig.Recipes.Add(new GridRecipe { Width = 2, Height = 1, Enabled = true, ResolvedIngredients = new[] { Ingredient(Flint), Ingredient(Stick) }, Output = Ingredient(Flint, 2), Name = new AssetLocation("test:arrow") });
        var block = new BlockCraftingMachine();
        block.VariantStrict["side"] = "north";
        var machine = new BECraftingMachine { Block = block, SpeedSource = () => 0.5f };
        rig.Place(At(0), block, "signalsmachines:craftingmachine-north", machine, e => new BEBehaviorSignalConnector(e), SourceNodes(BECraftingMachine.PinCount));
        var clutch = rig.SourceBlock(At(1), 15); rig.Wire(clutch, 0, At(0), 1);
        var crystal = rig.SourceBlock(At(2), 15); rig.Wire(crystal, 0, At(0), 2);
        var strength = rig.SourceBlock(At(3), 15); rig.Wire(strength, 0, At(0), 3);
        rig.Drive(clutch, 0, 0); rig.Drive(crystal, 0, 0); rig.Drive(strength, 0, 0);
        return (rig, machine, clutch, crystal, strength);
    }

    private static void Seconds(SignalsRig rig, BECraftingMachine machine, float seconds)
    {
        for (float t = 0; t < seconds - 1e-4f; t += 0.1f) { rig.Tick(); machine.OnMechanicsTick(0.1f); }
    }

    [Fact]
    public void TheOperatorSequenceMakesTheProductAndSignalsItsState()
    {
        var (rig, machine, clutch, crystal, strength) = Setup();
        machine.Inventory[0].Itemstack = new ItemStack(Flint, 3); machine.Inventory[0].MarkDirty();
        machine.Inventory[1].Itemstack = new ItemStack(Stick, 2); machine.Inventory[1].MarkDirty();
        Seconds(rig, machine, 0.3f);
        Assert.NotNull(machine.Recipe);
        Assert.Equal(MachineProcess.Ready, machine.State);

        rig.Drive(clutch, 0, 15); Seconds(rig, machine, 3f);          // plate up to speed
        rig.Drive(crystal, 0, 15); Seconds(rig, machine, 0.3f);
        rig.Drive(strength, 0, 2); Seconds(rig, machine, 0.3f);        // two cells
        Assert.Equal(MachineProcess.Crafting, machine.State);
        Seconds(rig, machine, 5.5f);                                   // 3 + 2 s, a little slack for the signal steps
        Assert.Equal(Flint, machine.Inventory[BECraftingMachine.ProductSlot].Itemstack?.Item);
        Assert.Equal(4, machine.Inventory[BECraftingMachine.ProductSlot].StackSize);   // 2 cycles x 2
        Assert.Equal(1, machine.Inventory[0].StackSize);
        Assert.True(machine.Inventory[1].Empty);
        Assert.Equal(MachineProcess.Done, machine.State);
        // taking the product frees the machine for the next batch
        machine.Inventory[BECraftingMachine.ProductSlot].Itemstack = null; machine.Inventory[BECraftingMachine.ProductSlot].MarkDirty();
        Seconds(rig, machine, 0.3f);
        Assert.NotEqual(MachineProcess.Done, machine.State);
    }

    [Fact]
    public void SlammingTheStrengthBurnsTheBatchToDust()
    {
        var (rig, machine, clutch, crystal, strength) = Setup();
        machine.Inventory[0].Itemstack = new ItemStack(Flint, 3); machine.Inventory[0].MarkDirty();
        machine.Inventory[1].Itemstack = new ItemStack(Stick, 2); machine.Inventory[1].MarkDirty();
        rig.Drive(clutch, 0, 15); Seconds(rig, machine, 3f);
        rig.Drive(crystal, 0, 15); Seconds(rig, machine, 0.3f);
        rig.Drive(strength, 0, 9); Seconds(rig, machine, 0.3f);        // 0 -> 9 in one step
        Assert.Equal(MachineProcess.Overloaded, machine.State);
        Assert.Equal(Dust, machine.Inventory[0].Itemstack?.Item);
        Assert.Equal(1, machine.Inventory[0].StackSize);
        Assert.Equal(Dust, machine.Inventory[1].Itemstack?.Item);
        Assert.True(machine.Inventory[2].Empty);
    }
}
