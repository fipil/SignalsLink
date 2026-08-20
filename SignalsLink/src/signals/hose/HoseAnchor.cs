using System;
using signals.src.signalNetwork;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.hose
{
    /// <summary>
    /// A hose anchor definition read from the JSON <c>attributes.hoseNodes</c>
    /// (index + name + cuboid). Mirror of the Signals <c>WireAnchor</c>, but for ManagedHose.
    /// </summary>
    public class HoseAnchor : RotatableCube
    {
        public int Index;
        public string Name;

        public HoseAnchor(int index, string name, float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ)
            : base(MinX, MinY, MinZ, MaxX, MaxY, MaxZ)
        {
            Index = index;
            Name = name;
        }
    }

    /// <summary>Shared operations over an array of hose anchors (used by Coupling/Intake and Valve).</summary>
    public static class HoseAnchorUtil
    {
        public static HoseAnchor[] Parse(JsonObject attributes, ICoreAPI api, AssetLocation code)
        {
            JsonObject[] arr = attributes?["hoseNodes"]?.AsArray();
            if (arr == null) return Array.Empty<HoseAnchor>();

            try
            {
                var result = new HoseAnchor[arr.Length];
                for (int i = 0; i < arr.Length; i++)
                {
                    result[i] = arr[i].AsObject<HoseAnchor>();
                }
                return result;
            }
            catch (Exception e)
            {
                api.World.Logger.Error("Failed loading hoseNodes for block {0}. Will ignore. Exception: {1}", code, e);
                return Array.Empty<HoseAnchor>();
            }
        }

        public static Vec3f GetAnchorPosInBlock(HoseAnchor[] anchors, int index)
        {
            foreach (HoseAnchor box in anchors)
            {
                if (box.Index == index)
                {
                    Cuboidf cube = box.RotatedCopy();
                    return new Vec3f(cube.MidX, cube.MidY, cube.MidZ);
                }
            }
            return new Vec3f(0.5f, 0.5f, 0.5f);
        }

        public static NodePos[] GetHoseAnchors(HoseAnchor[] anchors, BlockPos pos)
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
