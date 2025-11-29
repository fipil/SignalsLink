using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.blocksensor.scanners
{
    public class EntityScanner : IBlockSensorScanner
    {
        const byte IS_PLAYER = 2;
        const byte IS_ANIMAL = 3;
        const byte IS_WILDANIMAL = 4;
        const byte IS_CREATURE = 5;
        const byte DETECT_ENTITY_TYPE = 6;

        public bool CanScan(Block block, BlockEntity blockEntity, byte inputSignal)
        {
            return inputSignal >= IS_PLAYER && inputSignal <= DETECT_ENTITY_TYPE;
        }

        public byte CalculateSignal(IWorldAccessor world, BlockPos pos, Block block, BlockEntity blockEntity, byte inputSignal)
        {
            var center = pos.ToVec3d().Add(0.5, 0.5, 0.5);

            var entities = world.GetEntitiesAround(center, 1.0f, 1.0f, (e) => e.Alive);

            if (entities == null || entities.Length == 0)
                return 0;

            foreach (var ent in entities)
            {
                BlockPos entBlockPos = ent.ServerPos.AsBlockPos;

                if (!entBlockPos.Equals(pos))
                    continue;

                if (inputSignal == DETECT_ENTITY_TYPE)
                {
                    if (ent is EntityPlayer)
                        return IS_PLAYER;

                    if (EntityClassifier.IsCreature(ent))
                        return IS_CREATURE;

                    if (EntityClassifier.IsWildAnimal(ent))
                        return IS_WILDANIMAL;

                    if (EntityClassifier.IsAnimal(ent))
                        return IS_ANIMAL;
                }
                else
                {
                    switch (inputSignal)
                    {
                        case IS_PLAYER:
                            if (ent is EntityPlayer)
                                return 1;
                            break;
                        case IS_CREATURE:
                            if (EntityClassifier.IsCreature(ent))
                                return 1;
                            break;
                        case IS_WILDANIMAL:
                            if (EntityClassifier.IsWildAnimal(ent))
                                return 1;
                            break;
                        case IS_ANIMAL:
                            if (EntityClassifier.IsAnimal(ent))
                                return 1;
                            break;
                    }
                }

            }

            return 0;
        }


    }

}
