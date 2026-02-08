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

namespace SignalsLink.EP.src.epmeter
{
    public class BlockEntityEPMeter : BlockEntityEConnector, IBESignalReceptor
    {
        public byte state;
        public byte outputState = 0;

        private const byte BATTERY_CAPACITY_METER = 1;
        private const byte POWER_BALLANCE_METER = 2;
        private const byte CURRENT_FLOW_METER = 3;
        private const string CURRENT_FACE = "currentFace";

        private IServerNetworkChannel serverChannel;
        private ICoreAPI serverApi;

        public bool? HasSignal;

        SignalNetworkMod signalMod;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            if (api.Side == EnumAppSide.Server)
            {
                serverChannel = ((ICoreServerAPI)api).Network
                    .GetChannel(SignalsLinkEPMod.MODID);
                serverApi = api;
            }

            signalMod = api.ModLoader.GetModSystem<SignalNetworkMod>();
            signalMod.RegisterSignalTickListener(OnSignalNetworkTick);

            SetSignalled(state != 0);
            if (Api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(OnSlowServerTick, 1000);
            }
        }

        private void OnSlowServerTick(float dt)
        {
            switch(state)
            {
                case BATTERY_CAPACITY_METER:
                    calculateBatteryCapacity();
                    break;
                case POWER_BALLANCE_METER:
                    calculatePowerBallance();
                    break;
                case CURRENT_FLOW_METER:
                    calculateCurrentFlow();
                    break;
                default:
                    outputState = 0;
                    break;
            }
        }

        private void calculateCurrentFlow()
        {
            var neighborFacings = new List<BlockFacing>();
            neighborFacings.AddRange(FacingHelper.Directions(ContactsFacing));
            neighborFacings.AddRange(FacingHelper.Faces(ContactsFacing));

            var pos = Pos.Copy();

            foreach (var blockFacing in neighborFacings)
            {
                pos = pos.AddCopy(blockFacing);
                var networks = ElectricalProgressive?.System.GetNetworks(pos, Facing.AllAll, CURRENT_FACE);
                var maxCurrent = networks.eParamsInNetwork.maxCurrent * networks.eParamsInNetwork.lines;
                if (maxCurrent > 0)
                {
                    var current = Math.Abs(networks.current);
                    float ratio = current / maxCurrent;
                    outputState = (byte)Math.Clamp((int)Math.Ceiling(ratio * 12f), 0, 15);
                    return;
                }
            }
            outputState = 0;
        }

        private void calculateBatteryCapacity()
        {
            var networks = ElectricalProgressive?.System.GetNetworks(Pos, ContactsFacing);
            float ratio = networks.MaxCapacity > 0 ? networks.Capacity / networks.MaxCapacity : 0f;
            outputState = (byte)Math.Clamp((int)Math.Round(ratio * 15f), 0, 15);

        }

        private void calculatePowerBallance()
        {
            var networks = ElectricalProgressive?.System.GetNetworks(Pos, ContactsFacing);
            outputState = computePowerBalanceSignal(networks.Production, networks.Consumption);

        }

        private byte computePowerBalanceSignal(float production, float consumption)
        {
            // Neaktivní měření – např. žádná data
            if (production < 0 || consumption < 0)
                return 0;

            // Ochrana proti dělení nulou
            if (consumption == 0)
                return (byte)(production > 0 ? 15 : 0);

            double ratio = production / consumption;

            if (ratio < 1.0)
            {
                // Poměr < 1 → škála 1–7
                return (byte)Math.Clamp((int)Math.Ceiling(ratio * 7), 1, 7);
            }
            else if (ratio == 1.0)
            {
                return 8;
            }
            else
            {
                // Poměr > 1 → škála 9–15
                return (byte)Math.Clamp(8 + (int)Math.Floor((ratio - 1.0) * 7), 9, 15);
            }
        }

        private byte? lastOutputState;

        public void OnSignalNetworkTick()
        {
            BEBehaviorSignalConnector beb = GetBehavior<BEBehaviorSignalConnector>();
            if (beb == null) return;

            if (lastOutputState == outputState) return;

            ISignalNode nodeSource = beb.GetNodeAt(new NodePos(this.Pos, 1));
            signalMod.netManager.UpdateSource(nodeSource, outputState);
            lastOutputState = outputState;

            MarkDirty();
        }

        public void OnValueChanged(NodePos pos, byte value)
        {
            if (pos.index != 0) return;
            if (state == value) return;

            state = value;

            SetSignalled(state != 0);
        }

        public void SetSignalled(bool powered)
        {
            if (HasSignal != powered)
            {
                HasSignal = powered;
                UpdateBlockState();
                MarkDirty(true);

                var elPowered = powered ? ContactsFacing : Facing.None;
                if (ElectricalProgressive != null)
                    ElectricalProgressive.Connection = elPowered;
            }
        }


        public void UpdateBlockState()
        {
            Block currentBlock = Api.World.BlockAccessor.GetBlock(Pos);

            string newCode = currentBlock.Code.Domain + ":epmeter-" +
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

        public override void OnBlockPlaced(ItemStack byItemStack = null)
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

            // 3 kontakty na základně
            Facing contactFacing =
                FacingHelper.From(sideFace, forwardFace);        // levý kontakt

            return contactFacing;
        }
    }
}
