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
    /// The only thing the tile does for itself is draw a raised kerb along the outside edge of
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

        private const float BlockTop = 1f;
        private const float Edge = 3f / 16f;

        public override void OnJsonTesselation(ref MeshData sourceMesh, ref int[] lightRgbsByCorner, BlockPos pos, Block[] chunkExtBlocks, int extIndex3d)
        {
            base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, pos, chunkExtBlocks, extIndex3d);
            // The variant mesh is shared between tiles. Only edit a per-tile copy.
            sourceMesh = sourceMesh?.Clone();
            FlattenInnerKerbs(sourceMesh,
                HasTileTowards(chunkExtBlocks, extIndex3d, BlockFacing.NORTH),
                HasTileTowards(chunkExtBlocks, extIndex3d, BlockFacing.SOUTH),
                HasTileTowards(chunkExtBlocks, extIndex3d, BlockFacing.WEST),
                HasTileTowards(chunkExtBlocks, extIndex3d, BlockFacing.EAST),
                HasTileTowards(chunkExtBlocks, extIndex3d, BlockFacing.NORTH, BlockFacing.WEST),
                HasTileTowards(chunkExtBlocks, extIndex3d, BlockFacing.NORTH, BlockFacing.EAST),
                HasTileTowards(chunkExtBlocks, extIndex3d, BlockFacing.SOUTH, BlockFacing.WEST),
                HasTileTowards(chunkExtBlocks, extIndex3d, BlockFacing.SOUTH, BlockFacing.EAST));
        }

        private static bool HasTileTowards(Block[] chunkExtBlocks, int extIndex3d, BlockFacing facing, BlockFacing second = null)
        {
            // Use the engine's layout, without world access or shared layout inference on
            // tessellation threads. An unavailable neighbour suppresses the border.
            int step = Vintagestory.API.Client.Tesselation.TileSideEnum.MoveIndex[facing.Index];
            if (second != null) step += Vintagestory.API.Client.Tesselation.TileSideEnum.MoveIndex[second.Index];
            int index = extIndex3d + step;
            if (chunkExtBlocks == null || step == 0 || index < 0 || index >= chunkExtBlocks.Length) return true;
            return IsYardTile(chunkExtBlocks[index]);
        }

        /// <summary>
        /// Flatten whole plate faces, including their vertical walls. At a shared boundary the
        /// face centre alone is ambiguous: offset it inward along its normal to identify the
        /// owning plate. Every face of that plate then gets the same height decision, even if
        /// faces were culled or reordered. Classifying individual corners would make ramps.
        /// </summary>
        private static int FlattenInnerKerbs(MeshData mesh, bool north, bool south, bool west, bool east,
            bool northwest, bool northeast, bool southwest, bool southeast)
        {
            if (mesh?.xyz == null) return -1;
            int standing = 0;
            for (int vertex = 0; vertex + 3 < mesh.VerticesCount; vertex += 4)
            {
                int at = vertex * 3;
                float x = 0, z = 0, top = BlockTop;
                for (int v = 0; v < 4; v++)
                {
                    x += mesh.xyz[at + v * 3];
                    z += mesh.xyz[at + v * 3 + 2];
                    top = Math.Max(top, mesh.xyz[at + v * 3 + 1]);
                }
                if (top <= BlockTop) continue;
                x /= 4;
                z /= 4;

                // JSON box quads have outward winding. This identifies the owning side of
                // shared boundaries without depending on element order or lighting metadata.
                float ax = mesh.xyz[at + 3] - mesh.xyz[at];
                float ay = mesh.xyz[at + 4] - mesh.xyz[at + 1];
                float az = mesh.xyz[at + 5] - mesh.xyz[at + 2];
                float bx = mesh.xyz[at + 6] - mesh.xyz[at];
                float by = mesh.xyz[at + 7] - mesh.xyz[at + 1];
                float bz = mesh.xyz[at + 8] - mesh.xyz[at + 2];
                const float inset = 0.0001f;
                x -= Math.Sign(ay * bz - az * by) * inset;
                z -= Math.Sign(ax * by - ay * bx) * inset;

                bool raised = (!north && z < Edge) || (!south && z > 1f - Edge)
                    || (!west && x < Edge) || (!east && x > 1f - Edge)
                    // Fill the corner where two neighbouring rims meet around a missing
                    // diagonal tile. The cardinal sides themselves remain flat.
                    || (!northwest && x < Edge && z < Edge)
                    || (!northeast && x > 1f - Edge && z < Edge)
                    || (!southwest && x < Edge && z > 1f - Edge)
                    || (!southeast && x > 1f - Edge && z > 1f - Edge);
                for (int v = 0; v < 4; v++)
                {
                    int y = at + v * 3 + 1;
                    if (mesh.xyz[y] <= BlockTop) continue;
                    if (raised) standing++;
                    else mesh.xyz[y] = BlockTop;
                }
            }
            return standing;
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
        /// longer there. Explicit redraws cover chunk borders — inside a chunk it would happen
        /// anyway, but a neighbour across the border would not hear about it.
        /// </summary>
        public override void OnBlockPlaced(IWorldAccessor world, BlockPos pos, ItemStack byItemStack = null)
        {
            base.OnBlockPlaced(world, pos, byItemStack);
            RedrawDiagonalNeighbours(world, pos);
        }

        public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
        {
            base.OnBlockRemoved(world, pos);
            RedrawDiagonalNeighbours(world, pos);
        }

        private static void RedrawDiagonalNeighbours(IWorldAccessor world, BlockPos pos)
        {
            // Normal neighbour notifications cover cardinal neighbours only. A diagonal tile
            // can be in a different chunk, whose concave corner must be rebuilt as well.
            for (int dx = -1; dx <= 1; dx += 2)
            for (int dz = -1; dz <= 1; dz += 2)
                world.BlockAccessor.MarkBlockDirty(pos.AddCopy(dx, 0, dz));
        }

        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);
            world.BlockAccessor.MarkBlockDirty(pos);
        }
    }
}
