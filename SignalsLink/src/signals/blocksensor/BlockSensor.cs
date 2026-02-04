using signals.src.hangingwires;
using signals.src.signalNetwork;
using signals.src.transmission;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.blocksensor
{
    public class BlockSensor : BlockConnection
    {
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            // Workaround of the Signal's BlockConnection bug

            // 1. Wires work
            PlacingWiresMod modSystem = api.ModLoader.GetModSystem<PlacingWiresMod>();
            if (modSystem != null)
            {
                NodePos nodePosForWire = GetNodePosForWire(world, blockSel, modSystem.GetPendingNode());
                if (nodePosForWire != null && CanAttachWire(world, nodePosForWire, modSystem.GetPendingNode()))
                {
                    modSystem.ConnectWire(nodePosForWire, byPlayer, this);
                    return false;
                }
            }

            // 2. BlockBehaviors interactions
            foreach (var behavior in BlockBehaviors)
            {
                EnumHandling handling = EnumHandling.PassThrough;
                bool result = behavior.OnBlockInteractStart(world, byPlayer, blockSel, ref handling);

                if (handling == EnumHandling.PreventDefault)
                    return result;
            }

            // 3. BlockSensor specific interaction: change scanning direction
            BEBlockSensor be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BEBlockSensor;
            if (be != null)
            {
                string[] scanningModes = { "fwddown", "fwd", "fwdup", "fwdrightup", "fwdright", "fwdrightdown" };
                int currentIndex = Array.IndexOf(scanningModes, be.ScanningDirection);
                int nextIndex = (currentIndex + 1) % scanningModes.Length;
                be.SetScanningDirection(scanningModes[nextIndex]);
                return true;
            }

            return false;
        }

        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);

            (world.BlockAccessor.GetBlockEntity(pos) as BEBlockSensor)?.OnNeighbourBlockChange(neibpos);
        }

    }
}
