using System;
using signals.src.signalNetwork;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.link
{
    /// <summary>
    /// A link anchor definition read from the JSON <c>attributes.linkNodes</c>
    /// (index + name + cuboid). Mirror of the Signals <c>WireAnchor</c>, but for hoses/sleeves.
    /// </summary>
    public class LinkAnchor : RotatableCube
    {
        public int Index;
        public string Name;

        public LinkAnchor(int index, string name, float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ)
            : base(MinX, MinY, MinZ, MaxX, MaxY, MaxZ)
        {
            Index = index;
            Name = name;
        }
    }

    /// <summary>Shared operations over an array of link anchors (used by every anchor block).</summary>
    public static class LinkAnchorUtil
    {
        public static LinkAnchor[] Parse(JsonObject attributes, ICoreAPI api, AssetLocation code)
        {
            JsonObject[] arr = attributes?["linkNodes"]?.AsArray();
            if (arr == null) return Array.Empty<LinkAnchor>();

            try
            {
                var result = new LinkAnchor[arr.Length];
                for (int i = 0; i < arr.Length; i++)
                {
                    result[i] = arr[i].AsObject<LinkAnchor>();
                }
                return result;
            }
            catch (Exception e)
            {
                api.World.Logger.Error("Failed loading linkNodes for block {0}. Will ignore. Exception: {1}", code, e);
                return Array.Empty<LinkAnchor>();
            }
        }

        public static Vec3f GetAnchorPosInBlock(LinkAnchor[] anchors, int index)
        {
            foreach (LinkAnchor box in anchors)
            {
                if (box.Index == index)
                {
                    Cuboidf cube = box.RotatedCopy();
                    return new Vec3f(cube.MidX, cube.MidY, cube.MidZ);
                }
            }
            return new Vec3f(0.5f, 0.5f, 0.5f);
        }

        public static NodePos[] GetLinkAnchors(LinkAnchor[] anchors, BlockPos pos)
        {
            NodePos[] nodes = new NodePos[anchors.Length];
            for (int i = 0; i < anchors.Length; i++)
            {
                nodes[i] = new NodePos(pos, anchors[i].Index);
            }
            return nodes;
        }
    }
}
