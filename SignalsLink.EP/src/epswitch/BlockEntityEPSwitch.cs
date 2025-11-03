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

            if (ElectricalProgressive != null)
                ElectricalProgressive.Connection = Facing.None;

            SetPowered(state != 0);

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
            var elPowered = powered ? Facing.AllAll : Facing.None;

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
        
    }
}
