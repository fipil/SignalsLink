using signals.src.transmission;
using SignalsLink.src.signals.blocksensor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.managedchute
{
    public class ManagedWallChute : BlockConnection
    {
        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);

            (world.BlockAccessor.GetBlockEntity(pos) as BEManagedChute)?.OnNeighbourBlockChange(neibpos);
        }

        public override int GetRetention(BlockPos pos, BlockFacing facing, EnumRetentionType type)
        {
            return -1; // To allow put this pipe to cellar wall, to transfer items from/to cellars
        }
    }

}
