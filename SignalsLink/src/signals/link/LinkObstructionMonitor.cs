using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SignalsLink.src.signals.link
{
    /// <summary>
    /// Keeps sleeves honest after they have been placed: a sleeve must not pass through a block,
    /// but a block can appear across one long after it was hung. Server-side only.
    ///
    /// Two mechanisms, because blocks do not only appear under a player's hand:
    /// <list type="bullet">
    /// <item>An <b>occupancy map</b> from cell to the sleeves crossing it, so reacting to a placed
    /// block is a dictionary lookup rather than a walk over every sleeve in the world.</item>
    /// <item>A <b>sparse sweep</b> as the safety net, for everything else — gravity, plant growth,
    /// worldgen, other mods. It runs once a minute and only re-checks a slice of the sleeves per
    /// pass, so a world full of sleeves cannot turn it into a server stall.</item>
    /// </list>
    ///
    /// The one rule that matters more than reacting quickly: an unloaded chunk anywhere on a
    /// sleeve's path means <b>do nothing</b>. Reading air out of a chunk that has not streamed in
    /// yet would cut lines all over the world, and that is not undoable — the sleeve falls on the
    /// ground and the player has to hang it again.
    /// </summary>
    public class LinkObstructionMonitor
    {
        /// <summary>How often the safety-net sweep runs. Deliberately slow; the occupancy map is
        /// what makes a player's own building feel responsive.</summary>
        private const int SweepIntervalMs = 60000;

        /// <summary>Sleeves re-checked per sweep pass, so one pass stays bounded.</summary>
        private const int SweepBatch = 40;

        private readonly ICoreServerAPI sapi;
        private readonly LinkNetworkMod mod;

        // Cell -> the sleeves crossing it. Rebuilt whenever the graph changes, and once per sweep
        // so that lines skipped because of an unloaded chunk get indexed once it is there.
        private readonly Dictionary<BlockPos, List<LinkConnection>> cellIndex = new Dictionary<BlockPos, List<LinkConnection>>();
        private readonly List<LinkConnection> sleeves = new List<LinkConnection>();
        private int indexedVersion = -1;

        private int sweepCursor;

        public LinkObstructionMonitor(ICoreServerAPI sapi, LinkNetworkMod mod)
        {
            this.sapi = sapi;
            this.mod = mod;

            sapi.Event.DidPlaceBlock += OnDidPlaceBlock;
            sapi.Event.RegisterGameTickListener(OnSweepTick, SweepIntervalMs, sapi.World.Rand.Next(SweepIntervalMs));
        }

        private void OnDidPlaceBlock(IServerPlayer byPlayer, int oldblockId, BlockSelection blockSel, ItemStack withItemStack)
        {
            if (blockSel?.Position == null) return;

            EnsureIndex(force: false);
            if (!cellIndex.TryGetValue(blockSel.Position, out List<LinkConnection> touching)) return;

            // Copy first: breaking a sleeve mutates the graph and rebuilds the index under us.
            CheckAndBreak(new List<LinkConnection>(touching));
        }

        private void OnSweepTick(float dt)
        {
            EnsureIndex(force: true);
            if (sleeves.Count == 0) return;

            List<LinkConnection> slice = new List<LinkConnection>();
            int count = System.Math.Min(SweepBatch, sleeves.Count);
            for (int i = 0; i < count; i++)
            {
                slice.Add(sleeves[(sweepCursor + i) % sleeves.Count]);
            }
            sweepCursor = (sweepCursor + count) % sleeves.Count;

            CheckAndBreak(slice);
        }

        /// <summary>Re-checks the given sleeves and breaks the ones a block has grown into.</summary>
        private void CheckAndBreak(List<LinkConnection> candidates)
        {
            foreach (LinkConnection con in candidates)
            {
                if (!mod.data.connections.Contains(con)) continue; // already gone

                // Unknown (an unloaded chunk on the path) deliberately does nothing.
                if (LinkPathChecker.Check(sapi.World, con, out BlockPos blockedAt) != LinkPathResult.Blocked) continue;

                mod.BreakLinkAt(con, blockedAt);
            }
        }

        private void EnsureIndex(bool force)
        {
            if (!force && indexedVersion == mod.DataVersion) return;

            cellIndex.Clear();
            sleeves.Clear();

            List<BlockPos> cells = new List<BlockPos>();
            foreach (LinkConnection con in mod.data.connections)
            {
                if (con.kind != LinkKind.Sleeve) continue;
                sleeves.Add(con);

                cells.Clear();
                // A path that is currently blocked or unreadable is left out of the map; the sweep
                // deals with it, and the next rebuild picks it up once the chunks are there.
                if (LinkPathChecker.CollectCells(sapi.World, con, cells) != LinkPathResult.Clear) continue;

                foreach (BlockPos cell in cells)
                {
                    if (!cellIndex.TryGetValue(cell, out List<LinkConnection> list))
                    {
                        list = new List<LinkConnection>();
                        cellIndex[cell] = list;
                    }
                    list.Add(con);
                }
            }

            indexedVersion = mod.DataVersion;
            if (sweepCursor >= sleeves.Count) sweepCursor = 0;
        }
    }
}
