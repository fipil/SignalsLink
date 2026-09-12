using System;
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
    /// counted, so two anchors whose selections overlap do not release each other's ground. And the
    /// claims are saved: after a restart nothing would ever load the chunk an anchor stands in, so
    /// the anchor could never claim anything - it cannot pull itself up by its own bootstraps.
    ///
    /// The same reason will make this the home of the wake schedule later: a sleeping anchor's own
    /// column is unloaded, so its block entity is not ticking and cannot wake itself either.
    /// </summary>
    public class ChunkAnchors : ModSystem
    {
        /// <summary>Never change: saved games hold anchors under this key.</summary>
        public const string SaveKey = "signalslinkChunkAnchors";

        /// <summary>
        /// Marks the saved form that carries a list of columns. The first generation wrote a plain
        /// count of anchors followed by a radius each, so any non-negative first number is one of
        /// those and is read the old way.
        /// </summary>
        private const int ColumnsFormat = -1;

        private ICoreServerAPI sapi;

        /// <summary>Which columns each anchor holds.</summary>
        private readonly Dictionary<BlockPos, HashSet<long>> anchors = new Dictionary<BlockPos, HashSet<long>>();

        /// <summary>How many anchors want each column. A column at zero is let go.</summary>
        private readonly Dictionary<long, int> held = new Dictionary<long, int>();

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;

            api.Event.SaveGameLoaded += Restore;
            api.Event.GameWorldSave += Store;

            api.Event.RegisterGameTickListener(DrainReleases, 5000);
        }

        /// <summary>The columns this anchor holds, or an empty set when it holds none.</summary>
        public IReadOnlyCollection<long> ColumnsOf(BlockPos pos)
        {
            if (pos != null && anchors.TryGetValue(pos, out HashSet<long> columns)) return columns;

            return System.Array.Empty<long>();
        }

        /// <summary>
        /// Where an anchor stands, in column coordinates. Floor, not integer division: the latter
        /// rounds towards zero, so everything west or north of the origin would land one column
        /// off - and disagree with the map, which floors.
        /// </summary>
        public (int X, int Z) ColumnAt(BlockPos pos)
        {
            int size = sapi?.WorldManager?.ChunkSize ?? 32;

            return ((int)Math.Floor((double)pos.X / size), (int)Math.Floor((double)pos.Z / size));
        }

        /// <summary>
        /// Sets what an anchor holds, loading what is newly wanted and letting go of what is not.
        ///
        /// One method for claiming, changing and re-claiming after a restart, because they are the
        /// same thing: only the difference against what is held now is acted on. Claiming twice
        /// therefore costs nothing, which is what makes it safe to call from Initialize.
        /// </summary>
        public void SetColumns(BlockPos pos, IEnumerable<long> wanted)
        {
            if (sapi == null || pos == null) return;

            (int cx, int cz) = ColumnAt(pos);

            HashSet<long> after = AnchorArea.Sanitise(wanted, cx, cz);

            if (!anchors.TryGetValue(pos, out HashSet<long> before))
            {
                before = new HashSet<long>();
                anchors[pos.Copy()] = before;
            }

            int claimed = 0, released = 0;

            foreach (long key in after)
            {
                if (before.Contains(key)) continue;

                claimed++;

                held.TryGetValue(key, out int count);
                held[key] = count + 1;

                if (count == 0)
                {
                    (int x, int z) = AnchorArea.Of(key);
                    sapi.WorldManager.LoadChunkColumn(x, z, true);
                }
            }

            foreach (long key in before)
            {
                if (after.Contains(key)) continue;

                released++;
                LetGo(key);
            }

            before.Clear();
            foreach (long key in after) before.Add(key);

            // An anchor is claimed twice on every start - once from the saved list, so that its own
            // chunk loads at all, and again when its block entity comes up in that chunk. The
            // second one is a no-op by design, and saying so in the log only makes it look like
            // something happened twice.
            if (claimed == 0 && released == 0) return;

            sapi.Logger.Notification("[SignalsLink] anchor at " + pos + " holds "
                + after.Count + " chunk column(s); " + held.Count + " held in all.");
        }

        public void Release(BlockPos pos)
        {
            if (sapi == null || pos == null || !anchors.TryGetValue(pos, out HashSet<long> columns)) return;

            anchors.Remove(pos);

            foreach (long key in columns) LetGo(key);

            sapi.Logger.Notification("[SignalsLink] anchor at " + pos + " let go; "
                + held.Count + " columns still held.");
        }

        /// <summary>Everything held by every anchor.</summary>
        public int HeldColumns => held.Count;

        private void LetGo(long key)
        {
            if (!held.TryGetValue(key, out int count)) return;

            if (count > 1)
            {
                held[key] = count - 1;
                return;
            }

            held.Remove(key);

            // NOT unloaded here. UnloadChunkColumn is documented as acting "independent of any
            // nearby players", and it means it: unticking a column on the map while standing in it
            // pulls the ground out from under whoever is there. There is no API to merely clear the
            // keep-loaded flag, so the column is queued and dropped once nobody is near it.
            releasing.Add(key);
        }

        /// <summary>Columns nobody claims any more, waiting for the last player to walk away.</summary>
        private readonly HashSet<long> releasing = new HashSet<long>();

        private readonly List<long> ready = new List<long>();

        /// <summary>
        /// Drops what can safely be dropped. A column is safe once no player is within their own
        /// view distance of it - inside that, the game would have it loaded anyway, so holding on a
        /// little longer costs nothing and yanking it away costs a lot.
        /// </summary>
        private void DrainReleases(float dt)
        {
            if (releasing.Count == 0) return;

            ready.Clear();

            int margin = (sapi.Server?.Config?.MaxChunkRadius ?? 12) + 1;

            foreach (long key in releasing)
            {
                // Claimed again while it waited: nothing to do.
                if (held.ContainsKey(key)) { ready.Add(key); continue; }

                (int x, int z) = AnchorArea.Of(key);
                if (PlayerNear(x, z, margin)) continue;

                sapi.WorldManager.UnloadChunkColumn(x, z);
                ready.Add(key);
            }

            foreach (long key in ready) releasing.Remove(key);
        }

        private bool PlayerNear(int cx, int cz, int margin)
        {
            int size = sapi.WorldManager.ChunkSize;

            foreach (IPlayer player in sapi.World.AllOnlinePlayers)
            {
                Vintagestory.API.Common.Entities.EntityPos at = player?.Entity?.Pos;
                if (at == null) continue;

                int px = (int)Math.Floor(at.X / size);
                int pz = (int)Math.Floor(at.Z / size);

                if (Math.Abs(px - cx) <= margin && Math.Abs(pz - cz) <= margin) return true;
            }

            return false;
        }

        // ---------------------------------------------------------------- across a restart

        private void Store()
        {
            using MemoryStream buffer = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(buffer);

            writer.Write(ColumnsFormat);
            writer.Write(anchors.Count);

            foreach (KeyValuePair<BlockPos, HashSet<long>> anchor in anchors)
            {
                writer.Write(anchor.Key.X);
                writer.Write(anchor.Key.Y);
                writer.Write(anchor.Key.Z);

                writer.Write(anchor.Value.Count);
                foreach (long key in anchor.Value) writer.Write(key);
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

            List<(BlockPos Pos, HashSet<long> Columns)> saved;

            try
            {
                int first = reader.ReadInt32();

                saved = first == ColumnsFormat ? ReadColumns(reader) : ReadSquares(reader, first);
            }
            catch (EndOfStreamException)
            {
                // Half a record is worth nothing, and throwing here would stop the world loading.
                sapi.Logger.Warning("[SignalsLink] the saved chunk anchors are cut short; what could be read is kept.");
                return;
            }

            // Once the world is actually running: claiming during load is too early for the chunk
            // loader to answer.
            sapi.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () =>
            {
                foreach ((BlockPos pos, HashSet<long> columns) in saved) SetColumns(pos, columns);
            });
        }

        private static List<(BlockPos, HashSet<long>)> ReadColumns(BinaryReader reader)
        {
            List<(BlockPos, HashSet<long>)> saved = new List<(BlockPos, HashSet<long>)>();

            int count = reader.ReadInt32();

            for (int i = 0; i < count; i++)
            {
                BlockPos pos = new BlockPos(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());

                HashSet<long> columns = new HashSet<long>();
                int columnCount = reader.ReadInt32();

                for (int c = 0; c < columnCount; c++) columns.Add(reader.ReadInt64());

                saved.Add((pos, columns));
            }

            return saved;
        }

        /// <summary>
        /// The first generation's form: a radius per anchor, meaning a filled square. Read rather
        /// than discarded so an anchor already standing in a world goes on holding what it held.
        /// </summary>
        private List<(BlockPos, HashSet<long>)> ReadSquares(BinaryReader reader, int count)
        {
            List<(BlockPos, HashSet<long>)> saved = new List<(BlockPos, HashSet<long>)>();
            int size = sapi.WorldManager.ChunkSize;

            for (int i = 0; i < count; i++)
            {
                BlockPos pos = new BlockPos(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                int radius = reader.ReadInt32();

                saved.Add((pos, AnchorArea.Square(
                    (int)Math.Floor((double)pos.X / size), (int)Math.Floor((double)pos.Z / size), radius)));
            }

            return saved;
        }
    }
}
