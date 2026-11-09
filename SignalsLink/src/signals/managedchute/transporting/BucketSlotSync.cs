using System.Reflection;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.managedchute.transporting
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class BucketSlotClearedPacket
    {
        public int X;
        public int Y;
        public int Z;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class PressMashChangedPacket
    {
        public int X;
        public int Y;
        public int Z;

        /// <summary>The mash slot after the change, so the client reloads once its copy agrees.</summary>
        public bool MashEmpty;
        public string MashCode;
    }

    /// <summary>
    /// Redraw hints for vanilla blocks a device changed on the server. Their clients rebuild some
    /// meshes only inside the player interaction: the condenser drops its bucket mesh there, and
    /// the fruit press reloads its mash mesh there (its sync reloads only when the whole inventory
    /// turns empty or non-empty, which a bucket in the other slot prevents). Clients that load the
    /// chunk later never had the stale mesh.
    /// </summary>
    public class BucketSlotSync : ModSystem
    {
        public const string ChannelName = "signalslinkbucketslot";

        private static readonly FieldInfo BucketMeshField = typeof(BlockEntityCondenser)
            .GetField("bucketMesh", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo PressRendererField = typeof(BlockEntityFruitPress)
            .GetField("renderer", BindingFlags.Instance | BindingFlags.NonPublic);

        private IServerNetworkChannel serverChannel;
        private ICoreClientAPI capi;

        public override bool ShouldLoad(EnumAppSide forSide) => true;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            if (api is ICoreClientAPI client)
            {
                capi = client;
                client.Network.RegisterChannel(ChannelName)
                    .RegisterMessageType(typeof(BucketSlotClearedPacket))
                    .RegisterMessageType(typeof(PressMashChangedPacket))
                    .SetMessageHandler<BucketSlotClearedPacket>(OnCleared)
                    .SetMessageHandler<PressMashChangedPacket>(OnMashChanged);
            }
            else
            {
                serverChannel = ((ICoreServerAPI)api).Network.RegisterChannel(ChannelName)
                    .RegisterMessageType(typeof(BucketSlotClearedPacket))
                    .RegisterMessageType(typeof(PressMashChangedPacket));
            }
        }

        public void SendCleared(ICoreAPI api, BlockPos pos)
        {
            if (api is not ICoreServerAPI || serverChannel == null || pos == null) return;

            serverChannel.BroadcastPacket(new BucketSlotClearedPacket { X = pos.X, Y = pos.Y, Z = pos.Z });
        }

        public void SendMashChanged(ICoreAPI api, BlockEntityFruitPress press)
        {
            if (api is not ICoreServerAPI || serverChannel == null || press?.Pos == null) return;

            ItemStack mash = press.MashSlot.Itemstack;
            serverChannel.BroadcastPacket(new PressMashChangedPacket
            {
                X = press.Pos.X, Y = press.Pos.Y, Z = press.Pos.Z,
                MashEmpty = mash == null,
                MashCode = mash?.Collectible?.Code?.ToString()
            });
        }

        private void OnCleared(BucketSlotClearedPacket packet)
        {
            BlockPos pos = new BlockPos(packet.X, packet.Y, packet.Z);
            if (capi?.World?.BlockAccessor?.GetBlockEntity(pos) is not BlockEntityCondenser condenser) return;
            if (!condenser.Inventory[1].Empty) return;

            (BucketMeshField?.GetValue(condenser) as MeshData)?.Clear();
            condenser.MarkDirty(true);
        }

        /// <summary>
        /// The hint usually outruns the block entity's own sync, so the reload waits until the
        /// client's mash slot shows the state the server described, and gives up after a while.
        /// </summary>
        private void OnMashChanged(PressMashChangedPacket packet)
        {
            if (capi == null) return;

            BlockPos pos = new BlockPos(packet.X, packet.Y, packet.Z);
            int tries = 0;
            long id = 0;
            id = capi.Event.RegisterGameTickListener(dt =>
            {
                tries++;
                if (capi.World?.BlockAccessor?.GetBlockEntity(pos) is BlockEntityFruitPress press && Agrees(press, packet))
                {
                    ReloadMash(press);
                    capi.Event.UnregisterGameTickListener(id);
                    return;
                }

                if (tries > 60) capi.Event.UnregisterGameTickListener(id);
            }, 50);
        }

        private static bool Agrees(BlockEntityFruitPress press, PressMashChangedPacket packet)
        {
            ItemStack mash = press.MashSlot.Itemstack;
            if (packet.MashEmpty) return mash == null;
            return mash != null && mash.Collectible?.Code?.ToString() == packet.MashCode;
        }

        private static void ReloadMash(BlockEntityFruitPress press)
        {
            if (PressRendererField?.GetValue(press) is not FruitpressContentsRenderer renderer) return;

            renderer.reloadMeshes(press.getJuiceableProps(press.MashSlot.Itemstack), true);
            press.MarkDirty(true);
        }
    }
}
