using signals.src;
using signals.src.signalNetwork;
using signals.src.transmission;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace SignalsLink.src.signals
{
    public class BEBehaviorSignalSensor : BEBehaviorSignalConnector
    {
        Connection con;
        SignalNetworkMod signalMod;

        public BEBehaviorSignalSensor(BlockEntity blockentity) : base(blockentity)
        {
        }

        public void commute(byte state)
        {
            if (signalMod.Api.Side == EnumAppSide.Client) return;
            signalMod.netManager.UpdateConnection(con, state, 15);
        }

        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            signalMod = api.ModLoader.GetModSystem<SignalNetworkMod>();
            base.Initialize(api, properties);

            NodePos pos1 = new NodePos(this.Pos, 0);
            NodePos pos2 = new NodePos(this.Pos, 1);

            ISignalNode node1 = GetNodeAt(pos1);
            ISignalNode node2 = GetNodeAt(pos2);

            if (node1 == null || node2 == null) return;
            if (signalMod.Api.Side == EnumAppSide.Client) return;
            BESensor be = Blockentity as BESensor;
            con = new Connection(node1, node2, be.state, 15);
            signalMod.netManager.AddConnection(con);
        }
    }
}
