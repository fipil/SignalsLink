using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.chunkanchor
{
    /// <summary>
    /// Holds a square of chunk columns loaded around itself, so that machines keep working with no
    /// player anywhere near.
    /// </summary>
    public class BEChunkAnchor : BlockEntity
    {
        /// <summary>Columns to each side. 3 gives a 7x7 square with this one in the middle.</summary>
        public const int DefaultRadius = 3;

        public int Radius { get; private set; } = DefaultRadius;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            Radius = Block?.Attributes?["chunkRadius"].AsInt(DefaultRadius) ?? DefaultRadius;

            if (api is not ICoreServerAPI) return;

            // The claim is made again on every load: it is runtime state, not saved with the block.
            Anchors?.Claim(Pos, Radius);
        }

        /// <summary>
        /// Broken by a player. Unloading is deliberately NOT done when the block entity merely
        /// unloads - that happens on shutdown, and there is nothing left to hold by then.
        /// </summary>
        public override void OnBlockRemoved()
        {
            Anchors?.Release(Pos);

            base.OnBlockRemoved();
        }

        public override void GetBlockInfo(IPlayer forPlayer, System.Text.StringBuilder sb)
        {
            base.GetBlockInfo(forPlayer, sb);

            int side = 2 * Radius + 1;
            sb.AppendLine(Lang.Get("signalslink:chunkanchor-holding", side, side, side * side));
        }

        private ChunkAnchors Anchors => Api?.ModLoader?.GetModSystem<ChunkAnchors>();
    }
}
