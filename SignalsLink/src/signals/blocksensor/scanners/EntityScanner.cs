using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SignalsLink.src.signals.blocksensor.scanners
{
    public class EntityScanner : IBlockSensorScanner
    {
        const byte IS_PLAYER = 2;
        const byte IS_ANIMAL = 3;
        const byte IS_WILDANIMAL = 4;
        const byte IS_CREATURE = 5;
        const byte DETECT_ENTITY_TYPE = 6;
        const byte IS_BABY_ANIMAL = 7;

        public bool CanScan(Block block, BlockEntity blockEntity, byte inputSignal)
        {
            return inputSignal >= IS_PLAYER && inputSignal <= IS_BABY_ANIMAL;
        }

        public byte CalculateSignal(IWorldAccessor world, BlockPos pos, Block block, BlockEntity blockEntity, byte inputSignal)
        {
            var center = pos.ToVec3d().Add(0.5, 0.5, 0.5);

            var entities = world.GetEntitiesAround(center, 1.0f, 1.0f, (e) => e.Alive);

            if (entities == null || entities.Length == 0)
                return 0;

            List<byte> results = new List<byte>();
            int detectedEntities = 0;

            foreach (var ent in entities)
            {
                IEnumerable<string> tags = (IEnumerable<string>)ent.Tags.ToArray().Select<ushort, string>(new System.Func<ushort, string>(world.Api.TagRegistry.EntityTagIdToTag));
                BlockPos entBlockPos = ent.ServerPos.AsBlockPos;

                if (!entBlockPos.Equals(pos))
                    continue;

                detectedEntities++;

                if (inputSignal == DETECT_ENTITY_TYPE)
                {
                    if (ent is EntityPlayer)
                        results.Add(IS_PLAYER);

                    if (EntityClassifier.IsCreature(ent))
                        results.Add(IS_CREATURE);

                    if (EntityClassifier.IsWildAnimal(ent))
                        results.Add(IS_WILDANIMAL);

                    if (EntityClassifier.IsAnimal(ent))
                        results.Add(IS_ANIMAL)  ;
                }
                else
                {
                    switch (inputSignal)
                    {
                        case IS_PLAYER:
                            if (ent is EntityPlayer)
                                results.Add(1);
                            break;
                        case IS_CREATURE:
                            if (EntityClassifier.IsCreature(ent))
                                results.Add(1);
                            break;
                        case IS_WILDANIMAL:
                            if (EntityClassifier.IsWildAnimal(ent))
                                results.Add(1);
                            break;
                        case IS_ANIMAL:
                            if (EntityClassifier.IsAnimal(ent))
                                results.Add(1);
                            break;
                        case IS_BABY_ANIMAL:
                            if (EntityClassifier.IsAnimal(ent) && !tags.Contains("adult"))
                                results.Add(1);
                            break;
                    }
                }

            }

            if(detectedEntities>0)
            {
                if (results.Count == detectedEntities && results.All(r => r == results.First()))
                    return results.First();
                else
                    return inputSignal == DETECT_ENTITY_TYPE ? DETECT_ENTITY_TYPE : (byte)2;
            }

            return 0;
        }


    }

}
