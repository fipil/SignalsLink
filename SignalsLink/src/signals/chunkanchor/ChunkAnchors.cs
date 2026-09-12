using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SignalsLink.src.signals.chunkanchor
{
    /// <summary>
    /// Keeps chunk columns loaded on behalf of the anchors standing in the world.
    ///
    /// A ModSystem rather than the block entity's own business, for two reasons. Columns are
    /// counted, so two anchors whose squares overlap do not release each other's ground. And the
    /// claims are saved: after a restart nothing would ever load the chunk an anchor stands in, so
    /// the anchor could never claim anything - it cannot pull itself up by its own bootstraps.
    /// </summary>
    public class ChunkAnchors : ModSystem
    {
        /// <summary>Never change: saved games hold anchors under this key.</summary>
        public const string SaveKey = "signalslinkChunkAnchors";

        private ICoreServerAPI sapi;

        /// <summary>Where the anchors are, and how far each reaches.</summary>
        private readonly Dictionary<BlockPos, int> anchors = new Dictionary<BlockPos, int>();

        /// <summary>How many anchors want each column. A column at zero is let go.</summary>
        private readonly Dictionary<long, int> held = new Dictionary<long, int>();

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;

            api.Event.SaveGameLoaded += Restore;
            api.Event.GameWorldSave += Store;
        }

        /// <summary>
        /// Claims a square of <paramref name="radius"/> columns around the anchor. Repeating a
        /// claim for the same anchor changes nothing.
        /// </summary>
        public void Claim(BlockPos pos, int radius)
        {
            if (sapi == null || pos == null || anchors.ContainsKey(pos)) return;

            anchors[pos.Copy()] = radius;

            foreach ((int x, int z) in Columns(pos, radius))
            {
                long key = Key(x, z);

                held.TryGetValue(key, out int count);
                held[key] = count + 1;

                if (count == 0) sapi.WorldManager.LoadChunkColumn(x, z, true);
            }

            sapi.Logger.Notification("[SignalsLink] anchor at " + pos + " holds "
                + Area(radius) + " chunk columns; " + held.Count + " held in all.");
        }

        public void Release(BlockPos pos)
        {
            if (sapi == null || pos == null || !anchors.TryGetValue(pos, out int radius)) return;

            anchors.Remove(pos);

            foreach ((int x, int z) in Columns(pos, radius))
            {
                long key = Key(x, z);
                if (!held.TryGetValue(key, out int count)) continue;

                if (count > 1)
                {
                    held[key] = count - 1;
                    continue;
                }

                held.Remove(key);
                sapi.WorldManager.UnloadChunkColumn(x, z);
            }

            sapi.Logger.Notification("[SignalsLink] anchor at " + pos + " let go; "
                + held.Count + " columns still held.");
        }

        /// <summary>What one anchor holds, for the block's own description.</summary>
        public int ColumnsOf(int radius) => Area(radius);

        /// <summary>Everything held by every anchor.</summary>
        public int HeldColumns => held.Count;

        private IEnumerable<(int X, int Z)> Columns(BlockPos pos, int radius)
        {
            int size = sapi.WorldManager.ChunkSize;
            int cx = pos.X / size;
            int cz = pos.Z / size;

            for (int x = cx - radius; x <= cx + radius; x++)
            for (int z = cz - radius; z <= cz + radius; z++)
            {
                yield return (x, z);
            }
        }

        private static int Area(int radius) => (2 * radius + 1) * (2 * radius + 1);

        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        // ---------------------------------------------------------------- across a restart

        private void Store()
        {
            using MemoryStream buffer = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(buffer);

            writer.Write(anchors.Count);

            foreach (KeyValuePair<BlockPos, int> anchor in anchors)
            {
                writer.Write(anchor.Key.X);
                writer.Write(anchor.Key.Y);
                writer.Write(anchor.Key.Z);
                writer.Write(anchor.Value);
            }

            sapi.WorldManager.SaveGame.StoreData(SaveKey, buffer.ToArray());
        }

        /// <summary>
        /// Claims again what was claimed before the server stopped. Done from the saved list and
        /// not from the blocks, because the blocks are in chunks nobody has asked for yet.
        /// </summary>
        private void Restore()
        {
            byte[] data = sapi.WorldManager.SaveGame.GetData(SaveKey);
            if (data == null || data.Length == 0) return;

            using MemoryStream buffer = new MemoryStream(data);
            using BinaryReader reader = new BinaryReader(buffer);

            int count = reader.ReadInt32();
            List<(BlockPos Pos, int Radius)> saved = new List<(BlockPos, int)>();

            for (int i = 0; i < count; i++)
            {
                BlockPos pos = new BlockPos(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                saved.Add((pos, reader.ReadInt32()));
            }

            // Once the world is actually running: claiming during load is too early for the
            // chunk loader to answer.
            sapi.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () =>
            {
                foreach ((BlockPos pos, int radius) in saved) Claim(pos, radius);
            });
        }
    }
}
