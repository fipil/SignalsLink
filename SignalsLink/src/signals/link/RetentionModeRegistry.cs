using System.Collections.Concurrent;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.link
{
    /// <summary>The three ways a link endpoint can treat the room boundary it sits in.</summary>
    public static class RetentionMode
    {
        /// <summary>Default. Behaves like stone or soil: the room stays a cellar.</summary>
        public const int Cooling = 0;
        /// <summary>Behaves like a built wall: a heated room keeps its warmth.</summary>
        public const int Insulating = 1;
        /// <summary>A hole: the room is not closed here at all.</summary>
        public const int Open = 2;

        public const int Count = 3;

        /// <summary>
        /// What <c>Block.GetRetention</c> should answer for a mode. The sign carries the meaning:
        /// 0 is a hole, positive insulates, negative cools — which is what makes a cellar a cellar.
        /// The magnitude of the insulating case is a starting value, not a measured one.
        /// </summary>
        public static int RetentionValue(int mode)
        {
            if (mode == Open) return 0;
            if (mode == Insulating) return 3;
            return -1;
        }

        public static string LangKey(int mode)
        {
            if (mode == Open) return "signalslink:retention-open";
            if (mode == Insulating) return "signalslink:retention-insulating";
            return "signalslink:retention-cooling";
        }

        public static int Next(int mode) => (mode + 1) % Count;
    }

    /// <summary>
    /// Position → retention mode, so that <c>Block.GetRetention</c> can answer per placement.
    ///
    /// It cannot live on the Block: a block instance is a shared singleton, one per variant, so a
    /// field on it would be the same value for every damper in the world. Encoding the mode into
    /// the block code would work but costs a whole extra variant dimension — and would be a dead
    /// end for <c>ManagedWallChute</c>, whose variants cannot be touched without breaking pipes in
    /// existing worlds. A concurrent map keyed by position has neither problem, and it is safe to
    /// read from whatever thread room detection runs on (<c>Room.AnyChunkUnloaded</c> and the
    /// ChunkRooms cache suggest it is not always the main one).
    ///
    /// The map is a cache, not storage — the mode itself lives in the block entity's tree
    /// attributes and is republished from there whenever the block entity initialises.
    /// </summary>
    public class RetentionModeRegistry : ModSystem
    {
        private readonly ConcurrentDictionary<BlockPos, int> modes = new ConcurrentDictionary<BlockPos, int>();

        public override bool ShouldLoad(EnumAppSide forSide) => true;

        public void Publish(BlockPos pos, int mode)
        {
            if (pos == null) return;
            modes[pos.Copy()] = mode; // copied: callers reuse and mutate their BlockPos instances
        }

        public void Withdraw(BlockPos pos)
        {
            if (pos == null) return;
            modes.TryRemove(pos, out _);
        }

        public int Get(BlockPos pos, int fallback = RetentionMode.Cooling)
        {
            return pos != null && modes.TryGetValue(pos, out int mode) ? mode : fallback;
        }
    }

    /// <summary>A block entity whose room-sealing mode can be cycled with a wrench.</summary>
    public interface IRetentionModeHost
    {
        /// <summary>True when this placement actually forms a room boundary (the ceiling variant).</summary>
        bool SupportsRetentionMode { get; }

        int RetentionMode { get; }

        /// <summary>Advance to the next mode. Server side.</summary>
        void CycleRetentionMode();
    }
}
