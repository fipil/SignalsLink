using System.Reflection;
using SignalsLink.src.signals.yard;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SignalsLink.Tests;

public class YardSignFacingTests
{
    [Theory]
    [InlineData("north")]
    [InlineData("east")]
    [InlineData("south")]
    [InlineData("west")]
    public void Placed_board_faces_the_player(string side)
    {
        var towardsPlayer = BlockFacing.FromCode(side);
        Assert.Equal(towardsPlayer, BlockYardSign.FacingPlayer(
            towardsPlayer.Normali.X * 3, towardsPlayer.Normali.Z * 3));
    }

    [Theory]
    [InlineData("north")]
    [InlineData("east")]
    [InlineData("south")]
    [InlineData("west")]
    public void Increasing_texture_u_moves_right_for_a_viewer_in_front(string side)
    {
        var facing = BlockFacing.FromCode(side);
        float angle = YardSignRenderer.TextRotation(facing) * MathF.PI / 180;
        var mesh = YardSignRenderer.CreateTextQuad();
        int left = Enumerable.Range(0, 4).First(v => mesh.Uv[v * 2] == 0 && mesh.Uv[v * 2 + 1] == 0);
        int right = Enumerable.Range(0, 4).First(v => mesh.Uv[v * 2] == 1 && mesh.Uv[v * 2 + 1] == 0);
        float localX = mesh.xyz[right * 3] - mesh.xyz[left * 3];
        float dx = localX * MathF.Cos(angle), dz = -localX * MathF.Sin(angle);
        Assert.True(mesh.xyz[left * 3 + 1] > 0); // Top of the raster is the top of the board.
        // The viewer looks towards the board, opposite its outward normal.
        // Camera right = view direction cross world up.
        float viewerRightX = facing.Normali.Z, viewerRightZ = -facing.Normali.X;
        Assert.True(dx * viewerRightX + dz * viewerRightZ > 0.99f);
    }

}
