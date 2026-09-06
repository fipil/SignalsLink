using System.Collections.Generic;
using signals.src.signalNetwork;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.link
{
    public enum LinkPathResult
    {
        /// <summary>Nothing solid between the two anchors.</summary>
        Clear,
        /// <summary>A solid block sits on the straight line between the anchors.</summary>
        Blocked,
        /// <summary>Part of the path is in an unloaded chunk — the caller must not act on this.</summary>
        Unknown,
    }

    /// <summary>
    /// Tests whether a sleeve may span two anchors. Unlike a hose, a sleeve must not pass through
    /// blocks; the rule is applied to the <b>straight chord</b> between the two anchor centres,
    /// which is why the sleeve is drawn with a much shallower sag than a hose (see
    /// <see cref="LinkProfile"/>) — the picture has to match the rule.
    /// </summary>
    public static class LinkPathChecker
    {
        /// <summary>
        /// Step used to enumerate which cells the chord crosses, in blocks. It is not the precision
        /// of the test itself — each cell that lands on the list is then tested against its
        /// collision boxes exactly. It only decides how narrow a corner clip may be before a cell is
        /// skipped altogether, so it is kept well below a block: 80 lookups for a full length
        /// segment, run at most once a minute per sleeve.
        /// </summary>
        private const double Step = 0.125;

        /// <summary>
        /// Blocks at or above this replaceability are walked through without even asking for their
        /// shape: grass, flowers, snow. Same threshold <c>InventoryToWorldTransfer</c> uses to
        /// decide where an item may be placed.
        /// </summary>
        private const int PassableReplaceable = 6000;

        /// <summary>
        /// Does this connection have a clear path? Returns <see cref="LinkPathResult.Unknown"/>
        /// — and nothing else — as soon as any sampled position is in an unloaded chunk. Reading
        /// air out of a chunk that simply is not there yet would disconnect lines all over the
        /// world, and that damage is not undoable: the sleeve falls on the ground.
        /// </summary>
        public static LinkPathResult Check(IWorldAccessor world, LinkConnection con, out BlockPos blockedAt)
        {
            return Walk(world, con, null, out blockedAt);
        }

        /// <summary>
        /// Same walk, but also collects the cells the chord crosses. Only meaningful when the
        /// result is <see cref="LinkPathResult.Clear"/> — otherwise the list stops at the problem.
        /// Used to build the occupancy map, so that placing a block is an O(1) question.
        /// </summary>
        public static LinkPathResult CollectCells(IWorldAccessor world, LinkConnection con, List<BlockPos> cells)
        {
            return Walk(world, con, cells, out _);
        }

        private static LinkPathResult Walk(IWorldAccessor world, LinkConnection con, List<BlockPos> cells, out BlockPos blockedAt)
        {
            blockedAt = null;
            if (world == null || con?.pos1 == null || con.pos2 == null) return LinkPathResult.Unknown;

            Vec3d a = AnchorWorldPos(world, con.pos1);
            Vec3d b = AnchorWorldPos(world, con.pos2);
            if (a == null || b == null) return LinkPathResult.Unknown;

            HashSet<BlockPos> exempt = new HashSet<BlockPos>();
            AddEndpointExemptions(world, con.pos1.blockPos, exempt);
            AddEndpointExemptions(world, con.pos2.blockPos, exempt);

            double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
            double length = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
            int steps = (int)System.Math.Ceiling(length / Step);
            if (steps < 1) steps = 1;

            // Copied from an endpoint so the cursor carries the right dimension; a bare
            // new BlockPos() would silently walk dimension 0.
            BlockPos cursor = con.pos1.blockPos.Copy();
            BlockPos last = null;

            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                cursor.Set((int)System.Math.Floor(a.X + dx * t),
                           (int)System.Math.Floor(a.Y + dy * t),
                           (int)System.Math.Floor(a.Z + dz * t));

                if (last != null && cursor.Equals(last)) continue; // still inside the same cell
                last = cursor.Copy();

                if (world.BlockAccessor.GetChunkAtBlockPos(last) == null) return LinkPathResult.Unknown;
                if (exempt.Contains(last)) continue;

                cells?.Add(last);

                // SolidBlocks skips the liquid layer, so water never gets in the way.
                Block block = world.BlockAccessor.GetBlock(last, BlockLayersAccess.SolidBlocks);
                if (IsBlockedBy(world, block, last, a, dx, dy, dz))
                {
                    blockedAt = last.Copy();
                    return LinkPathResult.Blocked;
                }
            }

            return LinkPathResult.Clear;
        }

        /// <summary>
        /// Does the block in this cell actually stand in the sleeve's way? The test is against the
        /// block's <b>collision boxes</b>, not against the whole cell, so a sleeve may pass over a
        /// slab, between the rails of a fence or past a pipe — and a block with no collision at all
        /// (a torch, a rail, a plant) never blocks anything.
        ///
        /// The chord is tested exactly (slab method, no sampling), so a shape as thin as a pane is
        /// caught with certainty once its cell is on the list.
        /// </summary>
        private static bool IsBlockedBy(IWorldAccessor world, Block block, BlockPos cell, Vec3d from, double dx, double dy, double dz)
        {
            if (block == null || block.Replaceable >= PassableReplaceable) return false;

            Cuboidf[] boxes = block.GetCollisionBoxes(world.BlockAccessor, cell);
            if (boxes == null || boxes.Length == 0) return false;

            // Segment and boxes both expressed relative to the cell's corner.
            double ox = from.X - cell.X;
            double oy = from.Y - cell.Y;
            double oz = from.Z - cell.Z;

            foreach (Cuboidf box in boxes)
            {
                if (box == null) continue;
                if (SegmentHitsBox(ox, oy, oz, dx, dy, dz, box)) return true;
            }

            return false;
        }

        /// <summary>Slab method: does the segment from+t*d, t in [0,1], meet the box?</summary>
        private static bool SegmentHitsBox(double ox, double oy, double oz, double dx, double dy, double dz, Cuboidf box)
        {
            double tmin = 0.0, tmax = 1.0;

            return ClipToSlab(ox, dx, box.X1, box.X2, ref tmin, ref tmax)
                && ClipToSlab(oy, dy, box.Y1, box.Y2, ref tmin, ref tmax)
                && ClipToSlab(oz, dz, box.Z1, box.Z2, ref tmin, ref tmax);
        }

        private static bool ClipToSlab(double origin, double dir, double lo, double hi, ref double tmin, ref double tmax)
        {
            const double Parallel = 1e-9;

            if (System.Math.Abs(dir) < Parallel)
            {
                // Parallel to this pair of planes: either inside them for the whole segment, or never.
                return origin >= lo && origin <= hi;
            }

            double t1 = (lo - origin) / dir;
            double t2 = (hi - origin) / dir;
            if (t1 > t2) { double swap = t1; t1 = t2; t2 = swap; }

            if (t1 > tmin) tmin = t1;
            if (t2 < tmax) tmax = t2;

            return tmin <= tmax;
        }

        /// <summary>
        /// The endpoint's own block, plus the one block it is mounted on. Without the host
        /// exemption a sleeve could barely be attached anywhere — a damper on a chest has the
        /// chest right next to it — and a ceiling damper would never work at all, because its
        /// sleeve passes through the very ceiling it hangs from. The exemption is one block deep
        /// at each end, so a two block thick ceiling still stops the sleeve.
        /// </summary>
        private static void AddEndpointExemptions(IWorldAccessor world, BlockPos pos, HashSet<BlockPos> exempt)
        {
            if (pos == null) return;
            exempt.Add(pos.Copy());

            string sideCode = world.BlockAccessor.GetBlock(pos)?.Variant?["side"];
            BlockFacing side = sideCode != null ? BlockFacing.FromCode(sideCode) : null;
            if (side != null) exempt.Add(pos.AddCopy(side));
        }

        /// <summary>World-space centre of an anchor, or null if the block there is not an anchor.</summary>
        private static Vec3d AnchorWorldPos(IWorldAccessor world, NodePos node)
        {
            if (world.BlockAccessor.GetChunkAtBlockPos(node.blockPos) == null) return null;
            if (world.BlockAccessor.GetBlock(node.blockPos) is not ILinkAnchor anchor) return null;

            Vec3f local = anchor.GetLinkAnchorPosInBlock(node);
            return new Vec3d(node.blockPos.X + local.X, node.blockPos.Y + local.Y, node.blockPos.Z + local.Z);
        }
    }
}
