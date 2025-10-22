using signals.src.signalNetwork;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace SignalsLink.src.signals
{
    class BESensor : BlockEntity, IBESignalReceptor
    {
        public byte state;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            BEBehaviorSignalSensor sensor = GetBehavior<BEBehaviorSignalSensor>();
            sensor?.commute(5);
        }

        public void OnServerGameTick(float dt)
        {
        }

        public void OnValueChanged(NodePos pos, byte value)
        {
            if (pos.index != 0) return;
            state = value;
            BEBehaviorSignalSensor sensor = GetBehavior<BEBehaviorSignalSensor>();
            sensor?.commute(5);
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
    }
}
