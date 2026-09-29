using System.Reflection;
using Newtonsoft.Json.Linq;
using SignalsLink.src.signals.yard;
using Vintagestory.API.Client.Tesselation;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SignalsLink.Tests;

public class YardTileNeighbourTests
{
    [Fact]
    public void Border_lookup_uses_engine_offsets_including_chunk_edges()
    {
        // The engine fills this table at startup; unit tests have no client startup.
        int[] saved = TileSideEnum.MoveIndex;
        try
        {
            TileSideEnum.MoveIndex = new[] { -34, 1, 34, -1, 1156, -1156 };
            var blocks = new Block[34 * 34 * 34];
            var tile = new Block { Attributes = new JsonObject(JObject.Parse("{\"yardTile\":true}")) };
            foreach (int x in new[] { 1, 32 })
            foreach (int z in new[] { 1, 32 })
            {
                int here = (3 * 34 + z) * 34 + x;
                foreach (var direction in BlockFacing.HORIZONTALS)
                {
                    int adjacent = here + TileSideEnum.MoveIndex[direction.Index];
                    Assert.False(HasTile(blocks, here, direction));
                    blocks[adjacent] = tile;
                    Assert.True(HasTile(blocks, here, direction));
                    blocks[adjacent] = null;
                }
                foreach (var ns in new[] { BlockFacing.NORTH, BlockFacing.SOUTH })
                foreach (var ew in new[] { BlockFacing.EAST, BlockFacing.WEST })
                {
                    int adjacent = here + TileSideEnum.MoveIndex[ns.Index] + TileSideEnum.MoveIndex[ew.Index];
                    Assert.False(HasTile(blocks, here, ns, ew));
                    blocks[adjacent] = tile;
                    Assert.True(HasTile(blocks, here, ns, ew));
                    blocks[adjacent] = null;
                }
            }
            Assert.True(HasTile(null, 0, BlockFacing.NORTH));
        }
        finally { TileSideEnum.MoveIndex = saved; }
    }

    private static bool HasTile(Block[] blocks, int index, BlockFacing facing, BlockFacing second = null) =>
        (bool)typeof(BlockYardTile).GetMethod("HasTileTowards", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { blocks, index, facing, second });
}
