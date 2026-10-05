using Vintagestory.API.MathTools;

namespace SignalsMachines.src.craftingmachine;

/// <summary>
/// The temporal leak as a heavy gas that behaves like the game's finite liquids. A set of passable blocks it has
/// reached, each with a level (its strength) and the way it flows. Fed from the block in front of an open door while
/// the crystal is lit, it falls first (down only, while there is room below), then looks for a drop within
/// <see cref="DrainSearch"/> blocks and flows only towards it, otherwise spreads evenly to the sides, losing one
/// eighth of the source per sideways step (falling is free), never climbing. Its flow in a block is the slope of
/// the levels around it, like water's, which is what the motes drift along. Once the source stops, every cell fades
/// out over <see cref="FadeSeconds"/>. Pure data: the machine steps it on the server and syncs it to clients, which
/// draw it and ask it for the instability where a player stands.
/// </summary>
public sealed class MachineCloud
{
    public const int MaxSteps = 8, DrainSearch = 3;
    public const float FadeSeconds = 60f, SpreadDelay = 1.5f, FallDelay = 0.5f;

    public sealed class Cell
    {
        public int Steps;           // sideways steps from the source (falling costs nothing)
        public float Since;         // seconds the cloud has held this block (sets when it may pour on)
        public float Strength;      // 0..1 as fed: source * (1 - Steps / MaxSteps)
        public float Fade = 1;      // 1 while fed, falls to 0 over FadeSeconds afterwards
        public Vec3i Flow = new();  // the slope: where the cloud runs from here (horizontal signs; Y = -1 while falling)
        public bool FedThisStep;
        public bool Live;           // fed in the previous step; a fading cell is refilled only when the front reaches it again
        public float Intensity => Strength * Fade;
    }

    private readonly Dictionary<BlockPos, Cell> cells = new();

    public IReadOnlyDictionary<BlockPos, Cell> Cells => cells;
    public bool Empty => cells.Count == 0;

    /// <summary>Intensity 0..1 at a world point, 0 outside the cloud.</summary>
    public float At(double x, double y, double z) =>
        cells.TryGetValue(new BlockPos((int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Floor(z), 0), out var c) ? c.Intensity : 0;

    /// <summary>
    /// One step of <paramref name="dt"/> seconds. <paramref name="sources"/> are the blocks in front of open doors
    /// with the source strength 0..1; <paramref name="passable"/> says where the cloud may flow. Returns true when
    /// anything changed (for syncing).
    /// </summary>
    public bool Step(float dt, IEnumerable<(BlockPos pos, float strength)> sources, System.Func<BlockPos, bool> passable)
    {
        bool changed = false;
        foreach (var c in cells.Values) { c.Live = c.FedThisStep; c.FedThisStep = false; c.Since += dt; }

        // A flood from each source through the cells the cloud already holds keeps them fed; from a cell that has
        // held its block long enough, it pours on: down while it can, else towards a drain, else all around.
        var queue = new Queue<BlockPos>();
        var seen = new HashSet<BlockPos>();
        foreach (var (pos, strength) in sources)
        {
            if (strength <= 0 || !passable(pos)) continue;
            changed |= Feed(pos, 0, strength, dt);
            if (seen.Add(pos)) queue.Enqueue(pos.Copy());
        }
        while (queue.Count > 0)
        {
            var pos = queue.Dequeue();
            var cell = cells[pos];
            float source = cell.Strength / (1f - (float)cell.Steps / MaxSteps);
            var below = pos.DownCopy();
            if (passable(below))
            {
                // falling: the column fills before anything spreads
                changed |= Pour(below, cell, cell.Steps, source, FallDelay, queue, seen);
                continue;
            }
            if (cell.Steps >= MaxSteps - 1) continue;
            foreach (var step in SpreadDirections(pos, passable))
                changed |= Pour(pos.AddCopy(step), cell, cell.Steps + 1, source, SpreadDelay, queue, seen);
        }
        // whatever was not fed this step thins out, and cells that are gone are dropped
        foreach (var (pos, cell) in cells.ToList())
        {
            if (cell.FedThisStep) continue;
            cell.Fade -= dt / FadeSeconds;
            changed = true;
            if (cell.Fade <= 0) cells.Remove(pos);
        }
        if (changed) UpdateFlows(passable);
        return changed;
    }

    // One pour from a cell into a neighbour. A live neighbour is simply kept fed and carries the flood on; an empty
    // or fading block is reached only once the cell has held its own long enough, and then starts its clock anew.
    private bool Pour(BlockPos into, Cell from, int steps, float source, float delay, Queue<BlockPos> queue, HashSet<BlockPos> seen)
    {
        bool live = cells.TryGetValue(into, out var there) && there.Live;
        if (!live && from.Since < delay) return false;   // not there yet: it arrives when the delay is up
        bool changed = Feed(into, steps, source, 0);
        if (live) { if (seen.Add(into)) queue.Enqueue(into); }
        else if (there != null) there.Since = 0;
        return changed;
    }

    // Where a grounded cell pours: towards the nearest drops within reach (the first step of each shortest path),
    // or, with no drop around, to every passable side - the game's liquids do the same.
    private List<BlockFacing> SpreadDirections(BlockPos pos, System.Func<BlockPos, bool> passable)
    {
        var towardsDrains = new List<BlockFacing>();
        int best = int.MaxValue;
        foreach (var face in BlockFacing.HORIZONTALS)
        {
            var first = pos.AddCopy(face);
            if (!passable(first)) continue;
            int d = DistanceToDrop(first, passable, DrainSearch);
            if (d < 0 || d > best) continue;
            if (d < best) { best = d; towardsDrains.Clear(); }
            towardsDrains.Add(face);
        }
        if (towardsDrains.Count > 0) return towardsDrains;
        return BlockFacing.HORIZONTALS.Where(f => passable(pos.AddCopy(f))).ToList();
    }

    // Steps to the nearest block with room below it, searching outwards through passable blocks; -1 when none within reach.
    private static int DistanceToDrop(BlockPos start, System.Func<BlockPos, bool> passable, int reach)
    {
        var frontier = new List<BlockPos> { start };
        var visited = new HashSet<BlockPos> { start };
        for (int d = 0; d < reach; d++)
        {
            var next = new List<BlockPos>();
            foreach (var p in frontier)
            {
                if (passable(p.DownCopy())) return d;
                foreach (var face in BlockFacing.HORIZONTALS)
                {
                    var n = p.AddCopy(face);
                    if (passable(n) && visited.Add(n)) next.Add(n);
                }
            }
            frontier = next;
        }
        return -1;
    }

    // The slope: down wherever there is room below, else from a cell towards its lower neighbours (an empty passable side counts as level zero).
    private void UpdateFlows(System.Func<BlockPos, bool> passable)
    {
        foreach (var (pos, cell) in cells)
        {
            if (passable(pos.DownCopy())) { cell.Flow = new Vec3i(0, -1, 0); continue; }   // over room below (filled or not) it falls
            double fx = 0, fz = 0;
            foreach (var face in BlockFacing.HORIZONTALS)
            {
                var n = pos.AddCopy(face);
                if (!passable(n)) continue;
                float there = cells.TryGetValue(n, out var nc) ? nc.Intensity : 0;
                float drop = cell.Intensity - there;
                if (drop <= 0) continue;
                fx += face.Normali.X * drop; fz += face.Normali.Z * drop;
            }
            cell.Flow = new Vec3i(Math.Abs(fx) < 0.02 ? 0 : Math.Sign(fx), 0, Math.Abs(fz) < 0.02 ? 0 : Math.Sign(fz));
        }
    }

    // marks a cell as fed with this many steps and strength; fewer steps or a stronger source win.
    // A source born in this step has held its block since the step began (the door was open all along);
    // a cell the flood pours into arrives at the step's end.
    private bool Feed(BlockPos pos, int steps, float source, float since)
    {
        float strength = source * (1f - (float)steps / MaxSteps);
        if (strength <= 0) return false;
        if (!cells.TryGetValue(pos, out var cell))
        {
            cells[pos.Copy()] = new Cell { Steps = steps, Strength = strength, FedThisStep = true, Since = since };
            return true;
        }
        if (cell.FedThisStep && cell.Strength >= strength) return false;
        bool changed = cell.Strength != strength || cell.Fade != 1;
        if (!cell.FedThisStep || steps < cell.Steps) cell.Steps = Math.Min(cell.Steps, steps);
        cell.Strength = Math.Max(cell.FedThisStep ? cell.Strength : 0, strength);
        cell.Fade = 1;
        cell.FedThisStep = true;
        return changed;
    }

    // ---- sync: offsets from the machine (sbyte each), the intensity as a byte, the flow as a code (x+1)*3 + (z+1), 9 = falling

    public byte[] Serialize(BlockPos origin)
    {
        var list = new List<byte>(cells.Count * 5);
        foreach (var (pos, cell) in cells)
        {
            int dx = pos.X - origin.X, dy = pos.Y - origin.Y, dz = pos.Z - origin.Z;
            if (dx is < -127 or > 127 || dy is < -127 or > 127 || dz is < -127 or > 127) continue;
            list.Add((byte)(sbyte)dx); list.Add((byte)(sbyte)dy); list.Add((byte)(sbyte)dz);
            list.Add((byte)Math.Round(Math.Clamp(cell.Intensity, 0, 1) * 255));
            list.Add(cell.Flow.Y < 0 ? (byte)9 : (byte)((cell.Flow.X + 1) * 3 + (cell.Flow.Z + 1)));
        }
        return list.ToArray();
    }

    public void Deserialize(byte[] data, BlockPos origin)
    {
        cells.Clear();
        if (data == null) return;
        for (int i = 0; i + 4 < data.Length; i += 5)
        {
            var pos = new BlockPos(origin.X + (sbyte)data[i], origin.Y + (sbyte)data[i + 1], origin.Z + (sbyte)data[i + 2], origin.dimension);
            int code = data[i + 4];
            var flow = code >= 9 ? new Vec3i(0, -1, 0) : new Vec3i(code / 3 - 1, 0, code % 3 - 1);
            cells[pos] = new Cell { Strength = data[i + 3] / 255f, Fade = 1, Flow = flow };
        }
    }
}
