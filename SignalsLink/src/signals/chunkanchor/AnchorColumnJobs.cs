using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SignalsLink.src.signals.chunkanchor;

public readonly record struct AnchorCount(int Blocks, int Creatures);

/// <summary>One shared census per column, bounded loads and resumable scans on the server thread.</summary>
public sealed class AnchorColumnJobs : IDisposable
{
    private sealed class Job
    {
        public long Key;
        public double Started;
        public IEnumerator<AnchorCount> Scan;
        public AnchorCount Count;
    }
    private readonly Queue<long> queue = new();
    private readonly HashSet<long> queued = new();
    private readonly List<Job> running = new();
    private readonly HashSet<long> retained = new();
    private readonly HashSet<long> pinned = new();
    private readonly Dictionary<long, (AnchorCount Count, double At)> cache = new();
    private double nextCacheSweep;
    private readonly Func<long, bool> ready;
    private readonly Action<long> load, release;
    private readonly Func<long, IEnumerator<AnchorCount>> scan;
    public int Pending => queued.Count + running.Count;
    public const int MaxInFlight = 4;
    public const int StartsPerTick = 2;
    public const int StepsPerTick = 256;

    public AnchorColumnJobs(Func<long, bool> ready, Action<long> load, Action<long> release,
        Func<long, IEnumerator<AnchorCount>> scan)
    {
        this.ready = ready; this.load = load; this.release = release; this.scan = scan;
    }

    /// <param name="census">
    /// False for a column held only so a convoy stays whole: nobody ever asks what is in it, so
    /// scanning it would be work for nothing.
    /// </param>
    public void Retain(long key, bool census = true)
    {
        if (!retained.Add(key)) return;

        if (census) { Request(key); return; }

        load(key);
        pinned.Add(key);
    }
    public void LetGo(long key)
    {
        retained.Remove(key);
        queued.Remove(key);
        int index = running.FindIndex(j => j.Key == key);
        if (index >= 0) { running[index].Scan?.Dispose(); running.RemoveAt(index); }
        // Cancellation must not keep a sleeping factory alive to finish a census.
        Unpin(key);
    }
    public void Request(long key)
    {
        if (!queued.Contains(key) && !running.Exists(j => j.Key == key))
        { queued.Add(key); queue.Enqueue(key); }
    }
    public bool TryGet(long key, double now, out AnchorCount count, double? requestStarted = null)
    {
        if (cache.TryGetValue(key, out var item) && item.At >= (requestStarted ?? now) - 60)
        { count = item.Count; return true; }
        Request(key);
        count = default;
        return false;
    }
    public bool IsReady(long key) => ready(key);

    public void Tick(double seconds)
    {
        var watch = Stopwatch.StartNew();
        for (int n = 0; n < StartsPerTick && queue.Count > 0 && running.Count < MaxInFlight; n++)
        {
            long key = queue.Dequeue();
            if (!queued.Remove(key)) continue;
            load(key);
            pinned.Add(key);
            running.Add(new Job { Key = key, Started = seconds });
        }
        int budget = StepsPerTick;
        for (int i = 0; i < running.Count && budget > 0 && watch.ElapsedMilliseconds < 3;)
        {
            Job job = running[i];
            if (!ready(job.Key))
            {
                if (seconds - job.Started < 60) { i++; continue; }
                Finish(i, false, seconds);
                if (retained.Contains(job.Key)) Request(job.Key);
                continue;
            }
            job.Scan ??= scan(job.Key);
            bool finished = false;
            while (budget-- > 0 && watch.ElapsedMilliseconds < 3)
            {
                if (!job.Scan.MoveNext()) { finished = true; break; }
                job.Count = job.Scan.Current;
            }
            if (finished) Finish(i, true, seconds); else i++;
        }
        // Cache is only a preview, not save data. Bound memory on long-running servers.
        if (cache.Count > 4096 && seconds >= nextCacheSweep)
        {
            nextCacheSweep = seconds + 60;
            foreach (long key in new List<long>(cache.Keys))
                if (!retained.Contains(key) && seconds - cache[key].At >= 60) cache.Remove(key);
        }
    }
    private void Unpin(long key)
    {
        if (pinned.Remove(key)) release(key);
    }
    public void Dispose()
    {
        foreach (var job in running) job.Scan?.Dispose();
        running.Clear(); queue.Clear(); queued.Clear(); retained.Clear(); pinned.Clear(); cache.Clear();
    }
    private void Finish(int index, bool success, double now)
    {
        Job job = running[index];
        job.Scan?.Dispose();
        running.RemoveAt(index);
        if (success) cache[job.Key] = (job.Count, now);
        if (!retained.Contains(job.Key)) Unpin(job.Key);
    }
}
