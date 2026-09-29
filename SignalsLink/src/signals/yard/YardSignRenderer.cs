using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.yard;

/// <summary>Uses the sign text renderer with a plane tied to our board instead of vanilla geometry.</summary>
public class YardSignRenderer : BlockEntitySignRenderer
{
    public YardSignRenderer(BlockPos pos, ICoreClientAPI api, Block block, TextAreaConfig config) : base(pos, api, config)
    {
        var facing = BlockFacing.FromCode(block.Variant["side"]) ?? BlockFacing.NORTH;
        rotY = TextRotation(facing);
        // Define the readable side ourselves instead of inheriting vanilla sign UVs.
        quadModelRef.Dispose();
        quadModelRef = api.Render.UploadMesh(CreateTextQuad());
        var center = TextCenter(facing,
            block.Attributes?["textVoxelFront"].AsFloat(5.98f) ?? 5.98f,
            block.Attributes?["textVoxelCenterY"].AsFloat(10f) ?? 10f);
        translateX = center.X;
        translateY = center.Y;
        translateZ = center.Z;
        offsetX = offsetY = offsetZ = 0;
        // The base renderer centres text horizontally; Middle centres the raster vertically.
        // Its default canvas height is otherwise independent of config.MaxHeight.
        TextHeight = config.MaxHeight;
    }

    public static float TextRotation(BlockFacing facing) => facing.Index switch { 0 => 0, 1 => 270, 2 => 180, _ => 90 };

    public static MeshData CreateTextQuad()
    {
        var mesh = QuadMeshUtil.GetQuad();
        for (int v = 0; v < mesh.VerticesCount; v++)
        {
            mesh.Uv[v * 2] = mesh.xyz[v * 3] > 0 ? 0 : 1;
            mesh.Uv[v * 2 + 1] = mesh.xyz[v * 3 + 1] > 0 ? 0 : 1;
        }
        mesh.Rgba = Enumerable.Repeat((byte)255, mesh.VerticesCount * 4).ToArray();
        return mesh;
    }

    public static Vec3f TextCenter(BlockFacing facing, float frontVoxel, float centerYVoxel)
    {
        float distance = (8f - frontVoxel) / 16f;
        return new Vec3f(0.5f + facing.Normali.X * distance, centerYVoxel / 16f,
            0.5f + facing.Normali.Z * distance);
    }
}