using signals.src.signalNetwork;
using signals.src.transmission;
using SignalsLink.src.signals.blocksensor;
using SignalsLink.src.signals.link;
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
        // GetRetention is called from room detection, possibly off the main thread.
        private RetentionModeRegistry retentionModes;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            retentionModes = api.ModLoader.GetModSystem<RetentionModeRegistry>();
        }

        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);

            (world.BlockAccessor.GetBlockEntity(pos) as BEManagedChute)?.OnNeighbourBlockChange(neibpos);
        }

        /// <summary>
        /// The chute sits in a wall, so the room on either side should keep its climate. Which
        /// climate is up to the player (wrench): see <see cref="RetentionMode"/>. Cooling by
        /// default, which is what let the chute feed a cellar all along.
        /// </summary>
        public override int GetRetention(BlockPos pos, BlockFacing facing, EnumRetentionType type)
        {
            return RetentionMode.RetentionValue(retentionModes?.Get(pos) ?? RetentionMode.Cooling);
        }
    }

}
