using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Config;
using Vintagestory.API.Util;

namespace SignalsLink.src.signals.yard
{
    /// <summary>
    /// One paving tile of a storage yard. Goods sit on top of it as ordinary ground storage, which
    /// carries its own block entity — so the tile itself needs none, and a yard of four hundred of
    /// them costs the world nothing to save or tick.
    ///
    /// The only thing the tile does for itself is draw a lighter strip along the outside edge of
    /// the area, which is what tells a yard apart from ordinary paving.
    /// </summary>
    public class BlockYardTile : Block
    {
        /// <summary>
        /// Recognised by an attribute rather than by a block code, so another material — or another
        /// mod — is a JSON-only addition.
        /// </summary>
        public static bool IsYardTile(Block block)
        {
            return block?.Attributes?["yardTile"].AsBool(false) == true;
        }

        /// <summary>
        /// The top of the block, where a flattened kerb ends up.
        ///
        /// <b>The border is geometry, not colour.</b> Writing vertex colours in
        /// <c>OnJsonTesselation</c> does not survive - the log showed white vertices before the
        /// write, the right quads written, and nothing on screen. The mesh itself does survive, so
        /// the shape carries a kerb on every border plate and the ones that are not on an open edge
        /// are pressed flat here.
        /// </summary>
        private const float BlockTop = 1f;

        /// <summary>Where the border ends, in block space. Matches the shape's 3/16 plates.</summary>
        private const float Edge = 3f / 16f;

        public override void OnJsonTesselation(ref MeshData sourceMesh, ref int[] lightRgbsByCorner, BlockPos pos, Block[] chunkExtBlocks, int extIndex3d)
        {
            base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, pos, chunkExtBlocks, extIndex3d);

            // The mesh handed in is the one cached for this variant and shared by every tile of it,
            // so it is replaced with a copy before a single vertex is touched. Writing into the
            // shared one would paint the strip onto tiles that have no edge.
            sourceMesh = sourceMesh?.Clone();

            // Whether a tile is on the edge is a purely LOCAL question - is there another tile that
            // way? - so no flood fill is needed here, and none would be possible: tesselation runs
            // off the main thread. Only assembling the inventory needs the whole area.
            layout = ExtendedChunkLayout.Of(chunkExtBlocks?.Length ?? 0, extIndex3d, pos, layout);

            bool north = HasTileTowards(chunkExtBlocks, extIndex3d, layout, BlockFacing.NORTH);
            bool south = HasTileTowards(chunkExtBlocks, extIndex3d, layout, BlockFacing.SOUTH);
            bool west = HasTileTowards(chunkExtBlocks, extIndex3d, layout, BlockFacing.WEST);
            bool east = HasTileTowards(chunkExtBlocks, extIndex3d, layout, BlockFacing.EAST);

            int tinted = FlattenInnerKerbs(sourceMesh, north, south, west, east);

            Report(sourceMesh, chunkExtBlocks, extIndex3d, north, south, west, east, tinted, layout);
        }

        /// <summary>
        /// The layout as last worked out. Kept because a block whose coordinates coincide cannot
        /// tell the axes apart, and there is no reason to lose what the block before it proved.
        /// </summary>
        private static ExtendedChunkLayout layout;

        private static bool reported;

        /// <summary>
        /// Says once per session what the first tile actually saw.
        ///
        /// Tesselation is off the main thread and per chunk, so there is nowhere to put a
        /// breakpoint and nothing to print without drowning the log. One line, from the first tile
        /// tesselated, is enough to tell a mesh with no colours from a neighbour lookup that reads
        /// the wrong block - and those two look identical from the outside: no strip at all.
        /// </summary>
        private void Report(MeshData mesh, Block[] chunkExtBlocks, int extIndex3d,
            bool north, bool south, bool west, bool east, int tinted, ExtendedChunkLayout layout)
        {
            if (reported) return;

            reported = true;

            api?.Logger?.Notification("[SignalsLink] yard tile mesh: vertices=" + (mesh?.VerticesCount ?? -1)
                + " rgba=" + (mesh?.Rgba == null ? "null" : mesh.Rgba.Length.ToString())
                + " xyz=" + (mesh?.xyz == null ? "null" : mesh.xyz.Length.ToString())
                + " kerbsStanding=" + tinted
                + " | neighbours n=" + north + " s=" + south + " w=" + west + " e=" + east
                + " | ext=" + (chunkExtBlocks?.Length ?? -1) + " at " + extIndex3d
                + " | layout " + (layout.Known ? "resolved, step east=" + layout.StepFor(BlockFacing.EAST)
                    + " south=" + layout.StepFor(BlockFacing.SOUTH) : "UNRESOLVED"));
        }

        /// <summary>
        /// Is the neighbour one step in this direction another yard tile?
        ///
        /// A neighbour that cannot be read is reported as PRESENT, which draws no border. Guessing
        /// the other way would ring every tile in the world with a kerb, and a missing border is a
        /// far quieter kind of wrong than a border that is everywhere.
        /// </summary>
        private static bool HasTileTowards(Block[] chunkExtBlocks, int extIndex3d, ExtendedChunkLayout layout, BlockFacing facing)
        {
            if (!layout.Known) return true;

            int index = extIndex3d + layout.StepFor(facing);
            if (index < 0 || index >= chunkExtBlocks.Length) return true;

            return IsYardTile(chunkExtBlocks[index]);
        }

        /// <summary>
        /// How the extended chunk array is laid out - <b>worked out, not assumed.</b>
        ///
        /// The array is 34 x 34 x 34 (a chunk and one block of its neighbours all round) and the
        /// engine documents only "use extIndex3d + TileSideEnum.MoveIndex[side]", whose values are
        /// nowhere to be read. Guessing which axis runs fastest is how this came to read random
        /// cells for weeks: the border then appeared and disappeared by position, because whichever
        /// block the wrong index landed on sometimes happened to be paving.
        ///
        /// There is no need to guess. The block's own position is known here as well as its index
        /// in the array, and only one of the six ways of ordering three axes can turn one into the
        /// other. So all six are tried and the one that reproduces the index is the layout - checked
        /// afresh on every block that can tell the axes apart, which is nearly all of them.
        /// </summary>
        public readonly struct ExtendedChunkLayout
        {
            public bool Known { get; }

            public int StepX => stepX;
            public int StepZ => stepZ;

            private readonly int stepX;
            private readonly int stepZ;

            private ExtendedChunkLayout(int stepX, int stepZ)
            {
                this.stepX = stepX;
                this.stepZ = stepZ;
                Known = true;
            }

            public int StepFor(BlockFacing facing)
            {
                return facing.Normali.X * stepX + facing.Normali.Z * stepZ;
            }

            /// <summary>
            /// Works the layout out from one block, or hands back what was known before when this
            /// particular block cannot tell the axes apart.
            /// </summary>
            public static ExtendedChunkLayout Of(int arrayLength, int extIndex3d, BlockPos pos, ExtendedChunkLayout last)
            {
                if (arrayLength == 0 || pos == null) return last;

                int side = (int)System.Math.Round(System.Math.Cbrt(arrayLength));
                if (side < 3 || side * side * side != arrayLength) return last;

                const int border = 1;
                int chunkSize = side - 2 * border;

                // Where the block sits inside its own chunk, plus the border the array carries.
                int[] local =
                {
                    Wrap(pos.X, chunkSize) + border,
                    Wrap(pos.Y, chunkSize) + border,
                    Wrap(pos.Z, chunkSize) + border,
                };

                // Two axes at the same coordinate cannot be told apart; the next block will do.
                if (local[0] == local[1] || local[1] == local[2] || local[0] == local[2]) return last;

                foreach (int[] order in Orders)
                {
                    int index = (local[order[0]] * side + local[order[1]]) * side + local[order[2]];
                    if (index != extIndex3d) continue;

                    int[] weight = new int[3];
                    weight[order[0]] = side * side;
                    weight[order[1]] = side;
                    weight[order[2]] = 1;

                    return new ExtendedChunkLayout(weight[0], weight[2]);
                }

                return last;   // nothing matched: leave whatever was known before
            }

            /// <summary>Slowest axis first, fastest last; the axes are 0 = x, 1 = y, 2 = z.</summary>
            private static readonly int[][] Orders =
            {
                new[] { 1, 2, 0 }, new[] { 1, 0, 2 }, new[] { 0, 1, 2 },
                new[] { 0, 2, 1 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 },
            };

            /// <summary>Position within the chunk. Negative coordinates are the whole point.</summary>
            private static int Wrap(int coordinate, int size)
            {
                int wrapped = coordinate % size;

                return wrapped < 0 ? wrapped + size : wrapped;
            }
        }

        /// <summary>
        /// Presses flat every kerb that is not along an open edge, leaving the border standing only
        /// where the yard actually ends.
        ///
        /// The kerb is nine plates in the shape, so there is no surgery on the mesh here - the
        /// quads already exist and only their height changes. Flattening a box that has side faces
        /// leaves those faces zero high and therefore invisible; nothing has to be removed.
        ///
        /// Quads are found by position rather than by index, which survives face culling and any
        /// reordering of the shape's elements.
        /// </summary>
        private static int FlattenInnerKerbs(MeshData mesh, bool north, bool south, bool west, bool east)
        {
            if (mesh?.xyz == null) return -1;

            int standing = 0;

            for (int vertex = 0; vertex < mesh.VerticesCount; vertex++)
            {
                int at = vertex * 3;
                if (mesh.xyz[at + 1] <= BlockTop) continue;   // not part of a kerb

                if (StandsOnAnOpenEdge(mesh.xyz[at], mesh.xyz[at + 2], north, south, west, east))
                {
                    standing++;
                    continue;
                }

                mesh.xyz[at + 1] = BlockTop;
            }

            return standing;
        }

        /// <summary>
        /// Does this CORNER of the mesh belong to the rim?
        ///
        /// Asked of one vertex, not of a quad, and that is the whole point. Deciding by the middle
        /// of a quad worked for the tops of the plates and was wrong for their inner walls: such a
        /// wall lies exactly ON the line the border ends at, so the top of a plate stayed up while
        /// the wall under it was pressed flat - and the gap between them was a hole you could look
        /// through into the block.
        ///
        /// Per vertex it cannot happen: two quads that share a corner share its position, so they
        /// always get the same answer. The band is inclusive for the same reason.
        /// </summary>
        private static bool StandsOnAnOpenEdge(float x, float z, bool north, bool south, bool west, bool east)
        {
            const float slack = 0.0001f;

            // North is -Z in Vintage Story.
            if (!north && z <= Edge + slack) return true;
            if (!south && z >= 1f - Edge - slack) return true;
            if (!west && x <= Edge + slack) return true;
            if (!east && x >= 1f - Edge - slack) return true;

            return false;
        }

        /// <summary>
        /// What the player is standing on: whether these tiles already make a yard, and how big it
        /// is.
        ///
        /// The number of tiles is the point of it. It says that the flood fill found what the
        /// player expects - and above all it shows at once when two yards have been glued into one
        /// because they touch somewhere. Without the number that is a mystery.
        /// </summary>
        public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
        {
            string info = base.GetPlacedBlockInfo(world, pos, forPlayer) ?? "";

            // Asked once a frame for as long as the player looks at the tile, and a flood fill over
            // four hundred tiles per frame is not free. The answer is held for a moment instead: it
            // cannot go stale by more than the blink it takes to set down one tile.
            if (pos.Equals(describedPos) && world.ElapsedMilliseconds - describedAt < DescribeHoldMs)
            {
                return info + describedText;
            }

            YardArea area = Yard.AreaAt(world, pos);

            if (area.TooLarge)
            {
                return info + Hold(pos, world, Lang.Get("signalslink:yard-toolarge", YardArea.MaxTiles));
            }

            if (!area.IsValid)
            {
                return info + Hold(pos, world, Lang.Get("signalslink:yard-unfinished", YardArea.MinTiles));
            }

            string name = Yard.ReadName(world, area.OrderedFrom(pos));

            string text = string.IsNullOrEmpty(name)
                ? Lang.Get("signalslink:yard-size", area.Tiles.Count)
                : Lang.Get("signalslink:yard-sizenamed", name, area.Tiles.Count);

            return info + Hold(pos, world, text);
        }

        private const long DescribeHoldMs = 500;

        private static BlockPos describedPos;
        private static long describedAt;
        private static string describedText = "";

        private static string Hold(BlockPos pos, IWorldAccessor world, string text)
        {
            describedPos = pos.Copy();
            describedAt = world.ElapsedMilliseconds;
            describedText = text + "\n";

            return describedText;
        }

        /// <summary>
        /// A tile that gains or loses a neighbour has to be redrawn, or it keeps an edge that is no
        /// longer there. The explicit call is for chunk borders — inside a chunk it would happen
        /// anyway, but a neighbour across the border would not hear about it.
        /// </summary>
        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);
            world.BlockAccessor.MarkBlockDirty(pos);
        }
    }
}
