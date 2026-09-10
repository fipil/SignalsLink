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

        /// <summary>How much lighter the edge strip is drawn.</summary>
        private const float EdgeBrightness = 1.45f;

        /// <summary>Where the border ends, in block space. Matches the shape's 3/16 plates.</summary>
        private const float Edge = 3f / 16f;

        /// <summary>Anything at least this high is one of the thin top plates.</summary>
        private const float PlateTop = 0.999f;

        public override void OnJsonTesselation(ref MeshData sourceMesh, ref int[] lightRgbsByCorner, BlockPos pos, Block[] chunkExtBlocks, int extIndex3d)
        {
            base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, pos, chunkExtBlocks, extIndex3d);

            // Whether a tile is on the edge is a purely LOCAL question - is there another tile that
            // way? - so no flood fill is needed here, and none would be possible: tesselation runs
            // off the main thread. Only assembling the inventory needs the whole area.
            bool north = HasTileTowards(chunkExtBlocks, extIndex3d, BlockFacing.NORTH);
            bool south = HasTileTowards(chunkExtBlocks, extIndex3d, BlockFacing.SOUTH);
            bool west = HasTileTowards(chunkExtBlocks, extIndex3d, BlockFacing.WEST);
            bool east = HasTileTowards(chunkExtBlocks, extIndex3d, BlockFacing.EAST);

            if (north && south && west && east) return;   // fully surrounded: no edge to draw

            TintOuterPlates(sourceMesh, north, south, west, east);
        }

        /// <summary>
        /// Is the neighbour one step in this direction another yard tile?
        ///
        /// The step is worked out from the array itself rather than from an engine constant: the
        /// extended chunk is a cube, so its side is the cube root of its length, and Vintage Story
        /// lays such arrays out as ((y * side) + z) * side + x. One less undocumented helper to be
        /// broken by a game update.
        /// </summary>
        private static bool HasTileTowards(Block[] chunkExtBlocks, int extIndex3d, BlockFacing facing)
        {
            if (chunkExtBlocks == null || chunkExtBlocks.Length == 0) return true;

            int side = (int)System.Math.Round(System.Math.Cbrt(chunkExtBlocks.Length));
            if (side < 3 || side * side * side != chunkExtBlocks.Length) return true;

            int step = facing.Normali.X + facing.Normali.Z * side + facing.Normali.Y * side * side;
            int index = extIndex3d + step;

            if (index < 0 || index >= chunkExtBlocks.Length) return true;

            return IsYardTile(chunkExtBlocks[index]);
        }

        /// <summary>
        /// Lightens the vertices of the top plates that sit along an open edge.
        ///
        /// The top face is split into nine plates in the shape itself, so there is no surgery on
        /// the mesh here — the quads already exist and only their vertex colour changes. Quads are
        /// found by position rather than by index, which survives face culling and any reordering
        /// of the shape's elements.
        /// </summary>
        private static void TintOuterPlates(MeshData mesh, bool north, bool south, bool west, bool east)
        {
            if (mesh?.xyz == null || mesh.Rgba == null) return;

            int vertexCount = mesh.VerticesCount;

            for (int start = 0; start + 3 < vertexCount; start += 4)
            {
                if (!IsTopQuad(mesh, start)) continue;

                float centerX = 0f;
                float centerZ = 0f;

                for (int i = 0; i < 4; i++)
                {
                    centerX += mesh.xyz[(start + i) * 3];
                    centerZ += mesh.xyz[(start + i) * 3 + 2];
                }

                centerX /= 4f;
                centerZ /= 4f;

                if (!IsOnOpenEdge(centerX, centerZ, north, south, west, east)) continue;

                for (int i = 0; i < 4; i++) Lighten(mesh, start + i);
            }
        }

        private static bool IsTopQuad(MeshData mesh, int start)
        {
            for (int i = 0; i < 4; i++)
            {
                if (mesh.xyz[(start + i) * 3 + 1] < PlateTop) return false;
            }

            return true;
        }

        private static bool IsOnOpenEdge(float x, float z, bool north, bool south, bool west, bool east)
        {
            // North is -Z in Vintage Story.
            if (!north && z < Edge) return true;
            if (!south && z > 1f - Edge) return true;
            if (!west && x < Edge) return true;
            if (!east && x > 1f - Edge) return true;

            return false;
        }

        private static void Lighten(MeshData mesh, int vertex)
        {
            int at = vertex * 4;

            for (int channel = 0; channel < 3; channel++)
            {
                int value = (int)(mesh.Rgba[at + channel] * EdgeBrightness);
                mesh.Rgba[at + channel] = (byte)(value > 255 ? 255 : value);
            }
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
