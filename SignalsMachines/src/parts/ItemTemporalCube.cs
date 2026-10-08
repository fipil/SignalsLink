using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace SignalsMachines.src.parts;

/// <summary>
/// A temporal gear hammered into a cube; the grinding wheel grinds it into the machine's crystal.
/// The wheel grinds anything with the vanilla Sharpenable + Buffable behaviours: it calls DamageItem (max(1, 0.3 % of the
/// max durability), 5 % of its 100 ms ticks at full speed) and adds a "sharpened" buff until that buff is full.
/// Here every damage call is one step of the work, and the buff is wiped so the wheel never stops early.
/// </summary>
public class ItemTemporalCube : Item
{
    /// <summary>Damage calls until the cube is ground: about 30 s of grinding at full wheel speed.</summary>
    public const int GrindHits = 15;
    /// <summary>How long after the last interact step the wheel keeps its unstable field (ms).</summary>
    public const long GrindHoldMs = 1000;
    public const string ProgressKey = "grindProgress";
    public static readonly AssetLocation Crystal = new("signalsmachines:groundcrystal");

    public static int Progress(ItemStack stack) => stack?.Attributes.GetInt(ProgressKey) ?? 0;

    public override void DamageItem(IWorldAccessor world, Entity byEntity, ItemSlot itemSlot, int amount = 1, bool destroyOnZeroDurability = true)
    {
        var stack = itemSlot.Itemstack;
        if (stack == null) return;
        stack.Attributes.RemoveAttribute("buffs");
        int done = Progress(stack) + 1;
        if (done >= GrindHits)
        {
            var crystal = world.GetItem(Crystal);
            if (crystal == null) { world.Logger.Error("[signalsmachines] item {0} missing, the cube stays a cube", Crystal); return; }
            itemSlot.Itemstack = new ItemStack(crystal);
        }
        else stack.Attributes.SetInt(ProgressKey, done);
        itemSlot.MarkDirty();

        if (world.Side != EnumAppSide.Server) return;
        var wheel = (byEntity as EntityPlayer)?.Player?.CurrentBlockSelection?.Position;
        if (wheel != null) Sparks(world, wheel.ToVec3d().Add(.5, .6, .5), done >= GrindHits);
    }

    // Runs on both sides every frame while the button is held: the field around the wheel must exist on the client too,
    // or the player's gear does not spin while the server drains them.
    public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
    {
        bool result = base.OnHeldInteractStep(secondsUsed, slot, byEntity, blockSel, entitySel);
        if (blockSel != null && byEntity is EntityPlayer player
            && byEntity.World.BlockAccessor.GetBlockEntity(blockSel.Position) is Vintagestory.GameContent.BlockEntityGrindingWheel wheel
            && wheel.PlayersGrinding.ContainsKey(player.PlayerUID))
            byEntity.World.Api.ModLoader.GetModSystem<SignalsMachinesMod>()?.Grinding(blockSel.Position, byEntity.World.ElapsedMilliseconds + GrindHoldMs);
        return result;
    }

    // temporal motes off the wheel; a burst when the crystal is done
    private static void Sparks(IWorldAccessor world, Vec3d at, bool burst)
    {
        var p = new SimpleParticleProperties(burst ? 25 : 3, burst ? 40 : 6, ColorUtil.ToRgba(150, 90, 230, 210),
            at.AddCopy(-.3, -.3, -.3), at.AddCopy(.3, .3, .3), new Vec3f(-.6f, .2f, -.6f), new Vec3f(.6f, 1.2f, .6f),
            burst ? 1.5f : .8f, 0, .1f, .25f, EnumParticleModel.Quad);
        p.VertexFlags = 200;
        p.OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -200);
        world.SpawnParticles(p);
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        // the wheel's "sharpened" buff means nothing on a cube; keep it out of the tooltip between two hits
        inSlot.Itemstack?.Attributes.RemoveAttribute("buffs");
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        int done = Progress(inSlot.Itemstack);
        if (done > 0) dsc.AppendLine(Lang.Get("signalsmachines:grind-progress", 100 * done / GrindHits));
    }
}
