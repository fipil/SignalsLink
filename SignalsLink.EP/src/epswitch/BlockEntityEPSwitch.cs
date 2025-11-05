using ElectricalProgressive.Content.Block;
using ElectricalProgressive.Content.Block.EConnector;
using ElectricalProgressive.Utils;
using Newtonsoft.Json.Linq;
using signals.src;
using signals.src.signalNetwork;
using signals.src.transmission;
using SignalsLink.EP.src.messages;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SignalsLink.EP.src.epswitch
{
    public class BlockEntityEPSwitch: BlockEntityEConnector, IBESignalReceptor
    {
        public byte state;

        private IServerNetworkChannel serverChannel;
        private ICoreAPI serverApi;

        BlockFacing SignalOrientation = BlockFacing.NORTH;
        BlockFacing SignalSide = BlockFacing.DOWN;

        public bool? HasSignal;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            if (api.Side == EnumAppSide.Server)
            {
                serverChannel = ((ICoreServerAPI)api).Network
                    .GetChannel(SignalsLinkEPMod.MODID);
                serverApi = api;
            }

            if (this.Block.Variant["orientation"] != null)
            {
                BlockFacing facing = BlockFacing.FromCode(this.Block.Variant["orientation"]);
                if (facing != null)
                {
                    SignalOrientation = facing;
                }
            }
            if (this.Block.Variant["side"] != null)
            {
                BlockFacing facing = BlockFacing.FromCode(this.Block.Variant["side"]);
                if (facing != null)
                {
                    SignalSide = facing;
                }
            }

        }

        public void OnValueChanged(NodePos pos, byte value)
        {
            if (pos.index != 0) return;
            if (state == value) return;

            state = value;

            SetPowered(state != 0);
        }

        private void NotifyClients()
        {
            if (serverApi == null) return;

            const double range= 32;

            foreach(IServerPlayer player in serverApi.World.AllOnlinePlayers)
        {
                // Kontrola vzdálenosti
                if (player.Entity.Pos.SquareDistanceTo(Pos.ToVec3d()) < range * range)
                {
                    serverChannel.SendPacket(new EpSwitchSwitchedMessage()
                    {
                        Pos = this.Pos,
                        IsOn = state != 0
                    }, player);
                }
            }
        }

        public void SetPowered(bool signal)
        {
            if (HasSignal != signal)
            {
                HasSignal = signal;
                UpdateBlockState();
                MarkDirty(true);

                NotifyClients();
                SetEPSwitchConduction(signal);
             }
        }

        private void SetEPSwitchConduction(bool powered)
        {
            var elPowered = powered ? ContactsFacing : Facing.None;

            if (ElectricalProgressive != null)
                ElectricalProgressive.Connection = elPowered;
        }

        public void UpdateBlockState()
        {
            Block currentBlock = Api.World.BlockAccessor.GetBlock(Pos);

            string newCode = currentBlock.Code.Domain + ":epswitch-" +
                             ((HasSignal ?? false) ? "on" : "off") + "-" +
                             currentBlock.Variant["orientation"] + "-" +
                             currentBlock.Variant["side"];

            if (currentBlock.Code.Path == newCode.Split(':')[1])
            {
                return;
            }

            Block newBlock = Api.World.GetBlock(new AssetLocation(newCode));

            if (newBlock != null)
            {
                Api.World.BlockAccessor.ExchangeBlock(newBlock.Id, Pos);
                Api.World.BlockAccessor.MarkBlockDirty(Pos);
            }
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            state = tree.GetBytes("state", new byte[1] { 0 })[0];
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetBytes("state", new byte[1] { state });
        }

        public override void OnBlockPlaced(ItemStack? byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);

            if (ElectricalProgressive != null)
                ElectricalProgressive.Connection = Facing.None;
        }

        private Facing? contactsFacing;

        public Facing ContactsFacing
        {
            get
            {
                if (contactsFacing == null)
                {
                    contactsFacing = GetContactFacing();
                }
                return contactsFacing.Value;
            }
        }

        protected Facing GetContactFacing()
        {
            Block currentBlock = Api.World.BlockAccessor.GetBlock(Pos);
            string orientation = currentBlock.Variant?["orientation"];
            string side = currentBlock.Variant?["side"];

            if (orientation == null || side == null)
            {
                Api.World.Logger.Warning("epSwitch at {0} has missing variants (orientation/side).", Pos);
                return Facing.None;
            }

            BlockFacing sideFace = BlockFacing.FromCode(side);
            BlockFacing forwardFace = BlockFacing.FromCode(orientation);

            // Lokální báze
            BlockFacing upFace = sideFace.Opposite;
            BlockFacing rightFace = GetRightFace(forwardFace, upFace);
            BlockFacing leftFace = rightFace.Opposite;

            // 3 kontakty na základně
            Facing contactFacing =
                FacingHelper.From(sideFace, forwardFace) |    // přední kontakt
                FacingHelper.From(sideFace, rightFace) |    // pravý kontakt
                FacingHelper.From(sideFace, leftFace);        // levý kontakt

            return contactFacing;
        }

        private BlockFacing GetRightFace(BlockFacing forward, BlockFacing up)
        {
            // Vytvoří pravotočivý systém (forward × up)
            Vec3i f = forward.Normali;
            Vec3i u = up.Normali;
            Vec3i r = Cross(f, u);

            return BlockFacing.FromNormal( new Vec3i(r.X, r.Y, r.Z));
        }

        private Vec3i Cross(Vec3i a, Vec3i b)
        {
            return new Vec3i(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X
            );
        }
    }
}
