using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsMachines.src.craftingmachine;

/// <summary>
/// Everything the machine does for the senses, on the client, driven by the state the server syncs:
/// the crafting hum with its pitch following the strength, lightning to the cells, temporal smog,
/// the leak through an open door, steam when a door moves, the overload bang, and the crystal's light.
/// </summary>
public sealed class MachineEffects : IPointLight
{
    public const float HumBasePitch = 0.8f, HumPitchPerLevel = 0.055f;   // 1 -> 0.86, 9 -> 1.3
    public const float HumIdleVolume = 0.25f, HumWorkVolume = 0.7f;

    private readonly ICoreClientAPI capi;
    private readonly BECraftingMachine be;
    private readonly BlockPos pos;
    private readonly Random random = new();
    private ILoadedSound hum, spin;
    private float pitch = HumBasePitch, volume = HumIdleVolume, zapIn, smogIn, steamLeft, steamIn, lightLevel;
    private byte lastState;
    private int lastOverload;
    private bool wasWestOpen, wasEastOpen, lit;
    private readonly Vec3f lightColor = new();

    public Vec3f Color => lightColor;
    public Vec3d Pos { get; }

    private static readonly Vec3d CrystalTip = new(.5, 25.3 / 16, .5);       // lowered crystal, bottom
    private static readonly Vec3d ChamberCentre = new(.5, 23.5 / 16, .5);

    public MachineEffects(ICoreClientAPI capi, BECraftingMachine be)
    {
        this.capi = capi;
        this.be = be;
        pos = be.Pos;
        Pos = pos.ToVec3d().Add(ChamberCentre);
        lastState = be.State;
        lastOverload = be.OverloadSerial;
        wasWestOpen = be.DoorOpen(4);
        wasEastOpen = be.DoorOpen(5);
    }

    public void Tick(float dt)
    {
        var inputs = be.Inputs;
        bool crafting = be.State == MachineProcess.Crafting;
        Hum(inputs.Crystal > 0 && inputs.Strength > 0, crafting, inputs.Strength, dt);
        Spin(inputs.Clutch > 0 && be.PlateSpeed > PlateDrive.Still);
        if (crafting) { Zaps(dt); Smog(dt); }
        if (!be.Cloud.Empty) CloudMotes(dt);
        Smoke(dt);
        if (!be.Inventory[BECraftingMachine.ProductSlot].Empty) Aura(dt);
        Doors(dt);
        if (be.OverloadSerial != lastOverload) { lastOverload = be.OverloadSerial; Overload(); }
        Light(inputs.Strength, dt);
        lastState = be.State;
    }

    // ---- hum

    // the crystal hums as soon as it is down and powered; pitch follows the strength,
    // the volume swells while the work runs and settles again when it ends or blows
    private void Hum(bool on, bool crafting, byte strength, float dt)
    {
        bool fresh = hum == null;
        hum = Loop(hum, on, "crafting", HumIdleVolume);
        if (hum == null) return;
        float target = HumBasePitch + HumPitchPerLevel * Math.Min(strength, (byte)9);
        pitch += (target - pitch) * Math.Min(1, dt * 3);   // glides, never jumps
        hum.SetPitch(pitch);
        float wantVolume = crafting ? HumWorkVolume : HumIdleVolume;
        if (fresh) volume = HumIdleVolume;
        volume += (wantVolume - volume) * Math.Min(1, dt * 1.5f);
        hum.SetVolume(volume);
    }

    // the gears turn with the plate, under the hum
    private void Spin(bool on) => spin = Loop(spin, on, "craftingspin", .35f);

    private ILoadedSound Loop(ILoadedSound sound, bool on, string name, float volume)
    {
        if (on && sound == null)
        {
            sound = capi.World.LoadSound(new SoundParams
            {
                Location = new AssetLocation("signalsmachines:sounds/" + name + ".ogg"),
                Position = Pos.ToVec3f(), ShouldLoop = true, DisposeOnFinish = false, Range = 16, Volume = volume
            });
            sound?.Start();
        }
        else if (!on && sound != null) { sound.Stop(); sound.Dispose(); sound = null; }
        return sound;
    }

    // ---- lightning to the occupied cells

    private void Zaps(float dt)
    {
        zapIn -= dt;
        if (zapIn > 0) return;
        zapIn = 0.3f + (float)random.NextDouble() * 0.6f;
        var cells = Enumerable.Range(0, BECraftingMachine.GridSlots).Where(i => !be.Inventory[i].Empty).ToList();
        if (cells.Count == 0) return;
        int cell = cells[random.Next(cells.Count)];
        var target = pos.ToVec3d().Add(
            PlateRenderer.FirstCell + PlateRenderer.CellPitch * (cell % BECraftingMachine.GridSize),
            PlateRenderer.PlateTop + 0.02,
            PlateRenderer.FirstCell + PlateRenderer.CellPitch * (cell / BECraftingMachine.GridSize));
        // the plate turns: the cell's world position follows the picture's angle
        target = Turn(target, be.PlateAngle);
        capi.World.PlaySoundAt(new AssetLocation("signalsmachines:sounds/zap*"), pos.X + .5, pos.Y + 1.5, pos.Z + .5, null, false, 14, .5f);
        var from = pos.ToVec3d().Add(CrystalTip);
        var p = new SimpleParticleProperties(1, 1, ColorUtil.ToRgba(255, 170, 255, 250), from, from, new Vec3f(), new Vec3f(), 0.12f, 0, 0.15f, 0.3f, EnumParticleModel.Quad)
        {
            WithTerrainCollision = false, SelfPropelled = true, GravityEffect = 0, VertexFlags = 200
        };
        p.OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -255);
        int steps = 7;
        for (int i = 0; i <= steps; i++)
        {
            double t = (double)i / steps;
            var at = new Vec3d(from.X + (target.X - from.X) * t, from.Y + (target.Y - from.Y) * t, from.Z + (target.Z - from.Z) * t);
            at.Add((random.NextDouble() - .5) * .06, (random.NextDouble() - .5) * .06, (random.NextDouble() - .5) * .06);
            p.MinPos = at; p.AddPos = new Vec3d();
            capi.World.SpawnParticles(p);
        }
    }

    private Vec3d Turn(Vec3d world, float angle)
    {
        double cx = pos.X + .5, cz = pos.Z + .5;
        double dx = world.X - cx, dz = world.Z - cz;
        double c = Math.Cos(angle), s = Math.Sin(angle);
        return new Vec3d(cx + dx * c + dz * s, world.Y, cz - dx * s + dz * c);
    }

    // ---- temporal smog: slow, large wisps filling the chamber while crafting

    private void Smog(float dt)
    {
        smogIn -= dt;
        if (smogIn > 0) return;
        smogIn = 0.08f;
        var centre = pos.ToVec3d().Add(ChamberCentre);
        var p = new SimpleParticleProperties(3, 6, ColorUtil.ToRgba(140, 90, 220, 210),
            centre.AddCopy(-.3, -.2, -.3), centre.AddCopy(.3, .25, .3), new Vec3f(-.35f, -.1f, -.35f), new Vec3f(.35f, .1f, .35f),
            2.2f, 0, 0.2f, 0.45f, EnumParticleModel.Quad)
        {
            WithTerrainCollision = false, SelfPropelled = true, GravityEffect = 0, VertexFlags = 120,
            LifeLength = 2.2f, addLifeLength = 1.8f          // each dies somewhere else
        };
        p.OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.QUADRATIC, -255);
        p.SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, 0.3f);
        capi.World.SpawnParticles(p);
    }

    // ---- the leak cloud: in every block it has flowed into, motes in the temporal hue surface near the top now and
    //      then (the stronger the block, the more often), sink at a steady crawl and die at the floor - so the ones
    //      two metres up live twice as long as the ones a metre up

    private float motesIn;
    public const float MoteSinkSpeed = 0.1f, MoteDriftSpeed = 0.15f, MoteSecondsPerMetre = 10f;
    private static readonly int TemporalMote = ColorUtil.ToRgba(150, 90, 230, 210);

    private void CloudMotes(float dt)
    {
        motesIn -= dt;
        if (motesIn > 0) return;
        motesIn = 0.1f;   // five rounds a second
        var accessor = capi.World.BlockAccessor;
        var cells = be.Cloud.Cells;
        foreach (var (cell, data) in cells)
        {
            float intensity = data.Intensity;
            if (intensity < 0.02f) continue;
            // a block with cloud below it is passed through by the motes from above: it adds only a few of its own
            bool stacked = cells.ContainsKey(cell.DownCopy());
            double chance = (0.2 + intensity * 0.5) * (stacked ? 0.35 : 1);
            if (random.NextDouble() > chance) continue;
            // the floor under this block: down through passable blocks, at most three
            int drop = 0;
            while (drop < 3 && Passable(accessor, cell.X, cell.Y - 1 - drop, cell.Z)) drop++;
            double startY = 0.55 + random.NextDouble() * 0.35;
            float height = (float)(drop + startY) - 0.08f;     // dies just above the floor, nothing piles up there
            float life = height / MoteSinkSpeed;
            var at = cell.ToVec3d().Add(0.15 + random.NextDouble() * 0.7, startY, 0.15 + random.NextDouble() * 0.7);
            // drifts down the slope the cloud runs along here (water-like), and sinks; a falling column just drops
            var flow = data.Flow;
            float drift = flow.Y < 0 ? 0 : MoteDriftSpeed, sink = flow.Y < 0 ? MoteSinkSpeed * 2 : MoteSinkSpeed;
            if (flow.Y < 0) life = height / sink;
            var p = new SimpleParticleProperties(1, 1, TemporalMote, at, at.AddCopy(.001, .001, .001),
                new Vec3f(flow.X * drift - .02f, -sink, flow.Z * drift - .02f), new Vec3f(flow.X * drift + .02f, -sink, flow.Z * drift + .02f),
                life, 0, 0.2f + 0.3f * intensity, 0.4f + 0.4f * intensity, EnumParticleModel.Quad)
            {
                WithTerrainCollision = false, SelfPropelled = true, GravityEffect = 0, VertexFlags = (byte)(40 + 100 * intensity)
            };
            // surfaces from nothing and dissolves again at the end (the game's one fade-in-and-out curve), swelling on the way down
            p.OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.CLAMPEDPOSITIVESINUS, (float)Math.PI);
            p.SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, 0.7f);
            capi.World.SpawnParticles(p);
        }
    }

    private static bool Passable(IBlockAccessor accessor, int x, int y, int z)
    {
        var pos = new BlockPos(x, y, z, 0);
        var block = accessor.GetBlock(pos);
        if (block == null || block.Id == 0) return true;
        var boxes = block.GetCollisionBoxes(accessor, pos);
        return boxes == null || boxes.Length == 0;
    }

    // ---- smoke after an overload: the chamber fills with dense grey smoke that thins over a minute behind shut
    //      doors, or rolls out of an open door into the block before it and clears in seconds

    private float smokeLeft, smokeIn;
    public const float SmokeSeconds = 60f, SmokeVentFactor = 10f;

    private void Smoke(float dt)
    {
        if (smokeLeft <= 0) return;
        bool west = be.DoorOpen(4), east = be.DoorOpen(5);
        smokeLeft -= dt * (west || east ? SmokeVentFactor : 1);
        smokeIn -= dt;
        if (smokeIn > 0) return;
        smokeIn = 0.12f;
        float density = Math.Clamp(smokeLeft / SmokeSeconds, 0, 1);
        var centre = pos.ToVec3d().Add(ChamberCentre);
        var inside = new SimpleParticleProperties(1, 1 + (int)(density * 6), ColorUtil.ToRgba(200, 70, 70, 72),
            centre.AddCopy(-.38, -.3, -.38), centre.AddCopy(.38, .35, .38), new Vec3f(-.05f, 0, -.05f), new Vec3f(.05f, .04f, .05f),
            1.5f, 0, 0.35f, 0.7f, EnumParticleModel.Quad) { WithTerrainCollision = false, SelfPropelled = true, GravityEffect = 0, addLifeLength = 1f };
        inside.OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -160);
        inside.SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, 0.3f);
        capi.World.SpawnParticles(inside);
        foreach (int pin in new[] { 4, 5 })
        {
            if (!be.DoorOpen(pin)) continue;
            var side = ((BlockCraftingMachine)be.Block).DoorSide(pin);
            var mouth = pos.ToVec3d().Add(.5 + side.Normali.X, 1.5, .5 + side.Normali.Z);
            var outside = new SimpleParticleProperties(2, 2 + (int)(density * 6), ColorUtil.ToRgba(180, 80, 80, 82),
                mouth.AddCopy(-.4, -.4, -.4), mouth.AddCopy(.4, .4, .4),
                new Vec3f(side.Normali.X * .3f - .2f, .1f, side.Normali.Z * .3f - .2f), new Vec3f(side.Normali.X * .8f + .2f, .5f, side.Normali.Z * .8f + .2f),
                2.5f, -0.01f, 0.4f, 0.9f, EnumParticleModel.Quad) { WithTerrainCollision = true, SelfPropelled = true, addLifeLength = 1.5f };
            outside.OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -200);
            outside.SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, 0.8f);
            capi.World.SpawnParticles(outside);
        }
    }

    // ---- a finished product waits: temporal motes drift slowly about the chamber

    private float auraIn;

    private void Aura(float dt)
    {
        auraIn -= dt;
        if (auraIn > 0) return;
        auraIn = 0.35f;
        var centre = pos.ToVec3d().Add(ChamberCentre);
        var p = new SimpleParticleProperties(1, 1, ColorUtil.ToRgba(160, 120, 240, 230),
            centre.AddCopy(-.3, -.15, -.3), centre.AddCopy(.3, .2, .3), new Vec3f(-.08f, -.03f, -.08f), new Vec3f(.08f, .06f, .08f),
            2.5f, 0, 0.04f, 0.09f, EnumParticleModel.Quad)
        {
            WithTerrainCollision = false, SelfPropelled = true, GravityEffect = 0, VertexFlags = 160, addLifeLength = 1.5f
        };
        p.OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.QUADRATIC, -255);
        capi.World.SpawnParticles(p);
    }

    // ---- doors: steam when one starts to move, the slide sounds

    private void Doors(float dt)
    {
        bool west = be.DoorOpen(4), east = be.DoorOpen(5);
        if (west != wasWestOpen) { DoorSound(4, west); wasWestOpen = west; }
        if (east != wasEastOpen) { DoorSound(5, east); wasEastOpen = east; }
        if (steamLeft <= 0) return;
        steamLeft -= dt;
        steamIn -= dt;
        if (steamIn > 0) return;
        steamIn = 0.08f;
        foreach (int pin in new[] { 4, 5 })
        {
            if (steamDoor != pin) continue;
            var side = ((BlockCraftingMachine)be.Block).DoorSide(pin);
            var at = pos.ToVec3d().Add(.5 + side.Normali.X * .45, 1.35, .5 + side.Normali.Z * .45);
            var p = new SimpleParticleProperties(2, 4, ColorUtil.ToRgba(130, 230, 230, 230), at.AddCopy(-.2, -.3, -.2), at.AddCopy(.2, .3, .2),
                new Vec3f(side.Normali.X * .6f - .2f, .2f, side.Normali.Z * .6f - .2f), new Vec3f(side.Normali.X * 1.2f + .2f, .8f, side.Normali.Z * 1.2f + .2f),
                1.2f, -0.05f, 0.2f, 0.5f, EnumParticleModel.Quad) { WithTerrainCollision = false, SelfPropelled = true };
            p.OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -200);
            p.SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, 0.6f);
            capi.World.SpawnParticles(p);
        }
    }

    private int steamDoor;

    private void DoorSound(int pin, bool opening)
    {
        var side = ((BlockCraftingMachine)be.Block).DoorSide(pin);
        double x = pos.X + .5 + side.Normali.X * .5, y = pos.Y + 1.4, z = pos.Z + .5 + side.Normali.Z * .5;
        if (opening)
        {
            // steam as the door pushes out, the slide sound once it starts to drop
            capi.World.PlaySoundAt(new AssetLocation("signalsmachines:sounds/doorsteam.ogg"), x, y, z, null, false, 14, .6f);
            capi.Event.RegisterCallback(_ => capi.World.PlaySoundAt(new AssetLocation("signalsmachines:sounds/dooropen.ogg"), x, y, z, null, false, 14, .7f), (int)(DoorMotion.PushSeconds * 1000));
            steamDoor = pin; steamLeft = 2f;
        }
        else capi.World.PlaySoundAt(new AssetLocation("signalsmachines:sounds/doorclose.ogg"), x, y, z, null, false, 14, .7f);   // 2.25 s slide, then the latch
    }

    // ---- overload: bang, flash, smoke

    private void Overload()
    {
        capi.World.PlaySoundAt(new AssetLocation("signalsmachines:sounds/overload.ogg"), pos.X + .5, pos.Y + 1.5, pos.Z + .5, null, false, 24, 1f);
        smokeLeft = SmokeSeconds;
        var centre = pos.ToVec3d().Add(ChamberCentre);
        var flash = new SimpleParticleProperties(40, 60, ColorUtil.ToRgba(255, 200, 255, 255), centre, centre,
            new Vec3f(-2, -1, -2), new Vec3f(2, 2, 2), 0.4f, 0, 0.2f, 0.6f, EnumParticleModel.Quad) { WithTerrainCollision = false, SelfPropelled = true, VertexFlags = 255 };
        flash.OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -255);
        capi.World.SpawnParticles(flash);
        var smoke = new SimpleParticleProperties(20, 30, ColorUtil.ToRgba(180, 40, 40, 40), centre.AddCopy(-.3, -.3, -.3), centre.AddCopy(.3, .3, .3),
            new Vec3f(-.3f, .3f, -.3f), new Vec3f(.3f, 1f, .3f), 3f, -0.03f, 0.3f, 0.8f, EnumParticleModel.Quad) { WithTerrainCollision = false, SelfPropelled = true };
        smoke.OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -150);
        smoke.SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, 1f);
        capi.World.SpawnParticles(smoke);
    }

    // ---- light: temporal hue, intensity by strength

    private void Light(byte strength, float dt)
    {
        float target = Math.Min(strength, (byte)9) / 9f;
        lightLevel += (target - lightLevel) * Math.Min(1, dt * 4);
        lightColor.Set(0.25f * lightLevel, 0.9f * lightLevel, 0.85f * lightLevel);
        bool want = lightLevel > 0.02f;
        if (want && !lit) { capi.Render.AddPointLight(this); lit = true; }
        else if (!want && lit) { capi.Render.RemovePointLight(this); lit = false; }
    }

    public void Dispose()
    {
        hum?.Stop(); hum?.Dispose(); hum = null;
        spin?.Stop(); spin?.Dispose(); spin = null;
        if (lit) capi.Render.RemovePointLight(this);
    }
}
