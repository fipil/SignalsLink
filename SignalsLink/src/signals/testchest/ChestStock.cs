using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace SignalsLink.src.signals.testchest;

/// <summary>Server-only stock policy on a real vanilla InventoryBase. No replacement inventory or fake slots.</summary>
public sealed class ChestStock : IDisposable
{
    private readonly InventoryBase inventory;
    private readonly IWorldAccessor world;
    private readonly bool infinite;
    private readonly Action changed;
    private readonly ItemStack[] templates, previous;
    private readonly List<Arrival> arrivals = new();
    private bool updating, draining, recycling;
    public int IntervalSeconds { get; private set; } = 5;
    public bool StartAtHalf { get; private set; }
    public static bool ValidInterval(int value) => value is 0 or 1 or 2 or 5 or 10;
    private double delay = 5;
    public long Supplied { get; private set; }
    public long Received { get; private set; }
    public long Recycled { get; private set; }
    public bool Draining => draining;
    public int TemplateCount => templates.Count(s => s != null);
    public double DelayRemaining => delay;
    private sealed record Arrival(int Slot, int Count);

    public ChestStock(InventoryBase inventory, IWorldAccessor world, bool infinite, Action changed)
    {
        this.inventory = inventory; this.world = world; this.infinite = infinite; this.changed = changed;
        templates = new ItemStack[inventory.Count]; previous = new ItemStack[inventory.Count];
        for (int i = 0; i < inventory.Count; i++) previous[i] = inventory[i].Itemstack?.Clone();
        inventory.SlotModified += Modified;
    }
    private bool Same(ItemStack a, ItemStack b) => a != null && b != null
        && a.Equals(world, b, GlobalConstants.IgnoredStackAttributes);
    private void Modified(int index)
    {
        if (updating || index < 0 || index >= inventory.Count) return;
        updating = true;
        try
        {
            var slot = inventory[index]; var stack = slot.Itemstack;
            if (infinite)
            {
                var template = templates[index];
                if (stack != null && (template == null || !Same(stack, template) || stack.StackSize > template.StackSize))
                    templates[index] = template = stack.Clone();
                if (template != null && (stack == null || stack.StackSize < template.StackSize))
                {
                    Supplied += template.StackSize - (stack?.StackSize ?? 0);
                    slot.Itemstack = template.Clone(); slot.MarkDirty();
                }
            }
            else
            {
                var before = previous[index];
                int oldCount = before?.StackSize ?? 0, newCount = stack?.StackSize ?? 0;
                bool same = Same(before, stack);
                if (!same) RemoveFromQueue(index, int.MaxValue);
                else if (oldCount > newCount) RemoveFromQueue(index, oldCount - newCount);
                int added = same ? Math.Max(0, newCount - oldCount) : newCount;
                if (added > 0)
                {
                    Received += added;
                    if (arrivals.Count > 0 && arrivals[^1].Slot == index)
                        arrivals[^1] = arrivals[^1] with { Count = arrivals[^1].Count + added };
                    else arrivals.Add(new Arrival(index, added));
                }
                previous[index] = stack?.Clone();
                if (!draining && ThresholdReached()) { draining = true; delay = IntervalSeconds; }
                if (inventory.Empty) { draining = false; delay = IntervalSeconds; }
            }
            changed?.Invoke();
        }
        finally { updating = false; }
        if (!infinite && draining && IntervalSeconds == 0 && !recycling) Tick(0);
    }
    private bool ThresholdReached()
    {
        double filled = inventory.Sum(s => s.Empty ? 0d : Math.Min(1d,
            (double)s.StackSize / Math.Max(1, Math.Min(s.MaxSlotStackSize, s.Itemstack.Collectible.MaxStackSize))));
        return inventory.Count > 0 && filled >= inventory.Count * (StartAtHalf ? .5 : 1);
    }
    public void Configure(int intervalSeconds, bool startAtHalf)
    {
        if (!ValidInterval(intervalSeconds)) throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
        IntervalSeconds = intervalSeconds; StartAtHalf = startAtHalf;
        if (!infinite)
        {
            draining = !inventory.Empty && (draining || ThresholdReached());
            delay = IntervalSeconds;
            if (draining && IntervalSeconds == 0) Tick(0);
        }
        changed?.Invoke();
    }
    private void RemoveFromQueue(int slot, int count)
    {
        for (int i = 0; i < arrivals.Count && count > 0;)
        {
            var entry = arrivals[i];
            if (entry.Slot != slot) { i++; continue; }
            int remove = Math.Min(entry.Count, count); count -= remove;
            if (remove == entry.Count) arrivals.RemoveAt(i);
            else { arrivals[i] = entry with { Count = entry.Count - remove }; i++; }
        }
    }
    /// <summary>Timed mode removes one FIFO batch. Immediate mode drains a finite queue snapshot.</summary>
    public void Tick(double seconds)
    {
        if (infinite || !draining || recycling) return;
        delay -= Math.Max(0, seconds);
        if (delay > 0) return;
        delay = IntervalSeconds;
        recycling = true;
        try
        {
            int attempts = IntervalSeconds == 0 ? arrivals.Count : 1;
            for (int i = 0; i < attempts && arrivals.Count > 0; i++)
            {
                var entry = arrivals[0];
                var slot = inventory[entry.Slot];
                int count = Math.Min(entry.Count, slot.StackSize);
                if (count > 0)
                {
                    slot.TakeOut(count); slot.MarkDirty(); Recycled += count;
                }
                else arrivals.RemoveAt(0);
            }
            if (inventory.Empty || arrivals.Count == 0) draining = false;
            changed?.Invoke();
        }
        finally { recycling = false; }
    }
    public void Clear()
    {
        updating = true;
        try
        {
            Array.Clear(templates); Array.Clear(previous); arrivals.Clear(); draining = false; delay = IntervalSeconds;
            for (int i = 0; i < inventory.Count; i++) { inventory[i].Itemstack = null; inventory[i].MarkDirty(); }
            changed?.Invoke();
        }
        finally { updating = false; }
    }
    public void Write(ITreeAttribute tree)
    {
        tree.SetLong("supplied", Supplied); tree.SetLong("received", Received); tree.SetLong("recycled", Recycled);
        tree.SetBool("draining", draining); tree.SetDouble("delay", delay);
        tree.SetInt("intervalSeconds", IntervalSeconds); tree.SetBool("startAtHalf", StartAtHalf);
        for (int i = 0; i < templates.Length; i++) if (templates[i] != null) tree.SetItemstack("template" + i, templates[i]);
        tree.SetInt("arrivals", arrivals.Count);
        for (int i = 0; i < arrivals.Count; i++)
        { tree.SetInt("slot" + i, arrivals[i].Slot); tree.SetInt("count" + i, arrivals[i].Count); }
    }
    public void Read(ITreeAttribute tree)
    {
        Array.Clear(templates); arrivals.Clear();
        Supplied = tree?.GetLong("supplied") ?? 0; Received = tree?.GetLong("received") ?? 0; Recycled = tree?.GetLong("recycled") ?? 0;
        draining = tree?.GetBool("draining") ?? false;
        int interval = tree?.GetInt("intervalSeconds", 5) ?? 5;
        IntervalSeconds = ValidInterval(interval) ? interval : 5;
        StartAtHalf = tree?.GetBool("startAtHalf") ?? false;
        double savedDelay = tree?.GetDouble("delay", IntervalSeconds) ?? IntervalSeconds;
        delay = double.IsFinite(savedDelay) ? Math.Clamp(savedDelay, 0, IntervalSeconds) : IntervalSeconds;
        for (int i = 0; i < inventory.Count; i++)
        {
            templates[i] = tree?.GetItemstack("template" + i)?.Clone();
            if (templates[i] != null && world != null && !templates[i].ResolveBlockOrItem(world)) templates[i] = null;
            previous[i] = inventory[i].Itemstack?.Clone();
            if (infinite && templates[i] == null) templates[i] = previous[i]?.Clone();
        }
        int n = tree?.GetInt("arrivals") ?? 0;
        var remaining = inventory.Select(s => s.StackSize).ToArray();
        for (int i = 0; i < n; i++)
        {
            int slot = tree.GetInt("slot" + i, -1), count = tree.GetInt("count" + i);
            if (slot < 0 || slot >= inventory.Count || count <= 0) continue;
            count = Math.Min(count, remaining[slot]);
            if (count > 0) { arrivals.Add(new Arrival(slot, count)); remaining[slot] -= count; }
        }
        if (!infinite)
            for (int i = 0; i < remaining.Length; i++) if (remaining[i] > 0) arrivals.Add(new Arrival(i, remaining[i]));
        if (!infinite && ThresholdReached() && !draining) { draining = true; delay = IntervalSeconds; }
    }
    public void Dispose() => inventory.SlotModified -= Modified;
}
