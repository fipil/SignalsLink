using signals.src;
using signals.src.signalNetwork;
using signals.src.transmission;
using SignalsLink.src.signals.behaviours;
using SignalsLink.src.signals.blocksensor.scanners;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.entitysensor
{
    public enum PoweredMode
    {
        Off = 0, On = 1, Active = 2
    }

    public enum EntitySensorOutputConfig : byte
    {
        Count = 1, //count of detected entities
        Category = 2, //entity category boolean flags: player, creature, animal, wild animal
        LifeState = 3, //all detected entities are: 1-dead, 2-alive or 15-mixed
        Gender = 4, //all detected entities are: 1-male, 2-female or 15-mixed
        Age = 5, //all detected entities are: 1-baby, 2-adult or 15-mixed
        Species = 6, //all detected entities are: 1-player, 2-animal, 3-wild animal, 4-creature or 15-mixed
        ReproductiveState = 7, //all detected entities are: 1-readyToMate, 2-pregnant, 3-lactating or 15-mixed
        MinGeneration = 8, //minimal detected generation is: 0–15
        MaxGeneration = 9, //maximal detected generation is: 0–15
        MinWeight = 10, //minimal detected weight is: 1-starving, 2-low, 3-decent, 4-good
        MaxWeight = 11 //maximal detected weight is: 1-starving, 2-low, 3-decent, 4-good
    }

    public class BEEntitySensor : BlockEntity, IBESignalReceptor, ITemporalChargeHolder
    {
        const int ERROR_INDEX = 3;
        const int OUTPUT1_CONFIG_INDEX = 4;
        const int OUTPUT2_CONFIG_INDEX = 5;
        const int OUTPUT1_INDEX = 6;
        const int OUTPUT2_INDEX = 7;

        public byte x,y,z,output1config, output2config;
        public byte error,output1,output2;

        public PoweredMode Powered = PoweredMode.Off;

        SignalNetworkMod signalMod;

        private float currentCharge = 0f;
        private float consumptionPerTick = 0f;
        private double lastGameHours = 0;
        private BlockBehaviorTemporalCharge chargeBehavior;

        private static SensorScannerFactory scannerFactory;

        // Cachované hodnoty z behavioru (synchronizované na klienta)
        private float maxCharge = 0f;
        private float baseConsumptionFactor = 1.0f;
        private float referenceVolume = 100f;

        public BEEntitySensor()
        {
            if (scannerFactory == null)
            {
                scannerFactory = new SensorScannerFactory();
            }
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            signalMod = api.ModLoader.GetModSystem<SignalNetworkMod>();
            signalMod.RegisterSignalTickListener(OnSignalNetworkTick);

            SetPowered(DeterminePoweredFromInputs());

            chargeBehavior = Block.GetBehavior<BlockBehaviorTemporalCharge>();

            if (Api.Side == EnumAppSide.Server)
            {
                lastGameHours = Api.World.Calendar.TotalHours;

                if (chargeBehavior != null)
                {
                    UpdateConsumption();

                    maxCharge = chargeBehavior.GetMaxCharge();
                    baseConsumptionFactor = chargeBehavior.BaseConsumptionFactor;
                    referenceVolume = chargeBehavior.ReferenceVolume;
                }

                RegisterGameTickListener(OnSlowServerTick, 300);
            }
        }

        private void OnSlowServerTick(float dt)
        {
            if (chargeBehavior == null)
            {
                // Žádný charge systém, běž normálně
                CalculateOutputSignal();
                return;
            }

            if (currentCharge <= 0)
            {
                SendErrorSignal();
                return;
            }
            if(error != 0)
            {
                // Opraveno
                error = 0;
                SetPowered(DeterminePoweredFromInputs());
            }
            double currentGameHours = Api.World.Calendar.TotalHours;
            double hoursElapsed = currentGameHours - lastGameHours;

            if (hoursElapsed < 0 || hoursElapsed > 24) // max 1 herní den najednou
            {
                lastGameHours = currentGameHours;
                CalculateOutputSignal();
                return;
            }

            lastGameHours = currentGameHours; ;

            // Spotřeba: při volume 100 bloků se 1 gear spotřebuje za 100 dnů
            // 1 gear = GearTotalCharge = 100 * 24000 = 2,400,000
            // Za 100 dnů = 2400 hodin se má spotřebovat 2,400,000
            // Za 1 hodinu se má spotřebovat 1000 (při volume 100)

            float volumeRatio = GetOperationalVolume() / chargeBehavior.ReferenceVolume;
            float consumptionPerHour = (chargeBehavior.GearTotalCharge / (100f * 24f)) * volumeRatio * chargeBehavior.BaseConsumptionFactor;

            float consumption = consumptionPerHour * (float)hoursElapsed;
            currentCharge -= consumption;

            if (currentCharge < 0)
                currentCharge = 0;

            MarkDirty(true);

            CalculateOutputSignal();
        }

        private void SendErrorSignal()
        {
            if (error == 0)
            {
                error = 1;
                output1 = 0;
                output2 = 0;
                SetPowered(PoweredMode.Off);
            }
        }

        private void CalculateOutputSignal()
        {
            if (Powered == PoweredMode.Off)
            {
                return;
            }

            var entities = GetEntitiesInScanRegion(x, y, z);

            var results1 = new List<byte>();
            var results2 = new List<byte>();

            foreach (var ent in entities)
            {
                IEnumerable<string> tags = (IEnumerable<string>)ent.Tags.ToArray().Select<ushort, string>(new System.Func<ushort, string>(Api.TagRegistry.EntityTagIdToTag));
                BlockPos entBlockPos = ent.ServerPos.AsBlockPos;
                
                if(output1config>0)
                {
                    PrepareOutputSignal(results1, ent, output1config, tags, entBlockPos);
                }
                if(output2config>0)
                {
                    PrepareOutputSignal(results2, ent, output2config, tags, entBlockPos);
                }
            }

            if (output1config > 0)
            {
                if (output1config == (byte)EntitySensorOutputConfig.Count)
                {
                    output1 = (byte)Math.Min(entities.Count(), 15); //max 15
                }
                else
                {
                    ProcessResults(results1, output1config, out output1);
                }
            }
            if (output2config > 0)
            {
                if (output2config == (byte)EntitySensorOutputConfig.Count)
                {
                    output2 = (byte)Math.Min(entities.Count(), 15); //max 15
                }
                else
                {
                    ProcessResults(results2, output2config, out output2);
                }
            }

            SetPowered(entities.Count()>0 ? PoweredMode.Active : PoweredMode.On); 
            
        }

        private void ProcessResults(List<byte> results, byte config, out byte output)
        {
            if (results.Count == 0)
            {
                output = 0;
                return;
            }

            switch(config)
            {
                case (byte)EntitySensorOutputConfig.MinGeneration:
                    output = results.Min();
                    return;
                case (byte)EntitySensorOutputConfig.MaxGeneration:
                    output = results.Max();
                    return;
                case (byte)EntitySensorOutputConfig.MinWeight:
                    output = results.Min();
                    return;
                case (byte)EntitySensorOutputConfig.MaxWeight:
                    output = results.Max();
                    return;
                case (byte)EntitySensorOutputConfig.Category:
                    {
                        byte combined = 0;
                        foreach (var r in results)
                        {
                            combined |= r;
                        }
                        output = combined;
                        return;
                    }
                case (byte)EntitySensorOutputConfig.LifeState:
                case (byte)EntitySensorOutputConfig.Gender:
                case (byte)EntitySensorOutputConfig.Age:
                case (byte)EntitySensorOutputConfig.Species:
                case (byte)EntitySensorOutputConfig.ReproductiveState:
                    bool allSame = results.All(r => r == results[0]);
                    if (allSame)
                    {
                        output = results[0];
                    }
                    else
                    {
                        output = 15; //mixed
                    }
                    return;
            }
            output = 0;
        }

        private void PrepareOutputSignal(List<byte> results, Entity ent, byte config, IEnumerable<string> tags, BlockPos entBlockPos)
        {

            byte? result = config switch
            {
                (byte)EntitySensorOutputConfig.Species or (byte)EntitySensorOutputConfig.Category => ent switch
                {
                    _ when ent is EntityPlayer => 1,
                    _ when EntityClassifier.IsAnimal(ent) => 2,
                    _ when EntityClassifier.IsWildAnimal(ent) => 3,
                    _ when EntityClassifier.IsCreature(ent) => 4,
                    _ => null
                },
                (byte)EntitySensorOutputConfig.Gender => tags.Contains("male") || !(EntityClassifier.IsAnimal(ent) || EntityClassifier.IsWildAnimal(ent)) ? (byte)1 : (byte)2,
                (byte)EntitySensorOutputConfig.LifeState => ent.Alive ? (byte)2 : (byte)1,
                (byte)EntitySensorOutputConfig.Age => tags.Contains("adult") || !(EntityClassifier.IsAnimal(ent) || EntityClassifier.IsWildAnimal(ent)) ? (byte)2 : (byte)1,
                _ => null
            };

            if(result.HasValue)
            {
                results.Add(result.Value);
            }

        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            signalMod.DisposeSignalTickListener(OnSignalNetworkTick);
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            signalMod.DisposeSignalTickListener(OnSignalNetworkTick);
        }

        public override void OnBlockBroken(IPlayer byPlayer = null)
        {
            output1 = output2 = error = 0;
            SetPowered(PoweredMode.Off);

            base.OnBlockBroken(byPlayer);
        }

        public void OnSignalNetworkTick()
        {
            BEBehaviorSignalConnector beb = GetBehavior<BEBehaviorSignalConnector>();
            if (beb == null) return;
            ISignalNode errorSource = beb.GetNodeAt(new NodePos(this.Pos, ERROR_INDEX));
            ISignalNode output1Source = beb.GetNodeAt(new NodePos(this.Pos, OUTPUT1_INDEX));
            ISignalNode output2Source = beb.GetNodeAt(new NodePos(this.Pos, OUTPUT2_INDEX));
            signalMod.netManager.UpdateSource(errorSource, error);
            signalMod.netManager.UpdateSource(output1Source, output1);
            signalMod.netManager.UpdateSource(output2Source, output2);
            MarkDirty();
        }

        public void OnValueChanged(NodePos pos, byte value)
        {
            switch (pos.index)
            {
                case 0:
                    if (x == value) return;
                    x = value;
                    break;
                case 1:
                    if (y == value) return;
                    y = value;
                    break;
                case 2:
                    if (z == value) return;
                    z = value;
                    break;
                case OUTPUT1_CONFIG_INDEX:
                    if (output1config == value) return;
                    output1config = value;
                    break;
                case OUTPUT2_CONFIG_INDEX:
                    if (output2config == value) return;
                    output2config = value;
                    break;
                default:
                    return;
            };

            UpdateConsumption();
            SetPowered(DeterminePoweredFromInputs());
        }

        private void UpdateConsumption()
        {
            if(chargeBehavior != null)
                consumptionPerTick = chargeBehavior.CalculateConsumptionPerTick(GetOperationalVolume());
        }

        private PoweredMode DeterminePoweredFromInputs()
        {
            if(error != 0)
            {
                return PoweredMode.Off;
            }

            return (x > 0 && y > 0 && z > 0)
                            ? PoweredMode.On
                            : PoweredMode.Off;
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            byte[] inputs = tree.GetBytes("xyzcfg1cfg2", new byte[] { 0, 0, 0, 0, 0 }); 
            x = inputs[0];
            y = inputs[1];
            z = inputs[2];
            output1config = inputs[3];
            output2config = inputs[4];

            maxCharge = tree.GetFloat("maxCharge", 0f);
            baseConsumptionFactor = tree.GetFloat("baseConsumptionFactor", 1.0f);
            referenceVolume = tree.GetFloat("referenceVolume", 100f);

            currentCharge = tree.GetFloat("currentCharge", 0f);

            Powered = DeterminePoweredFromInputs();
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetBytes("xyzcfg1cfg2", new byte[5] { x, y, z, output1config, output2config });
            tree.SetFloat("currentCharge", currentCharge);

            tree.SetFloat("maxCharge", maxCharge);
            tree.SetFloat("baseConsumptionFactor", baseConsumptionFactor);
            tree.SetFloat("referenceVolume", referenceVolume);
        }

        public void SetPowered(PoweredMode mode)
        {
            if (Powered != mode)
            {
                Powered = mode;
                UpdateBlockState();
                MarkDirty(true);
            }
        }

        public float GetCurrentCharge() => currentCharge;

        public void SetCurrentCharge(float charge)
        {
            if (chargeBehavior != null)
            {
                currentCharge = Math.Max(0, Math.Min(charge, chargeBehavior.GetMaxCharge()));
            }
            else
            {
                currentCharge = charge;
            }
            MarkDirty(true);
        }

        public float GetOperationalVolume()
        {
            return x * y * z;
        }

        public void UpdateBlockState()
        {
            Block currentBlock = Api.World.BlockAccessor.GetBlock(Pos);

            string newCode = currentBlock.Code.Domain + ":entitysensor-" +
                             GetPoweredString() + "-" +
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

        public string GetPoweredString()
        {
            switch (Powered)
            {
                case PoweredMode.Off:
                    return "off";
                case PoweredMode.On:
                    return "on";
                case PoweredMode.Active:
                    return "active";
                default:
                    return "off";
            }
        }

        /// <summary>
        /// Spočítá lokální osy F (forward), R (right), U (up) z orientation/side.
        /// </summary>
        private (Vec3i F, Vec3i R, Vec3i U) GetLocalBasis()
        {
            Block currentBlock = Api.World.BlockAccessor.GetBlock(Pos);
            string orientation = currentBlock.Variant?["orientation"];
            string side = currentBlock.Variant?["side"];

            if (orientation == null || side == null)
            {
                throw new InvalidOperationException($"EntitySensor at {Pos} missing orientation/side variants.");
            }

            var forwardFace = BlockFacing.FromCode(orientation);
            var downFace = BlockFacing.FromCode(side);

            Vec3i F = forwardFace.Normali;           // kam „talíř“ míří
            Vec3i U = downFace.Opposite.Normali;     // lokální nahoru (opačný směr k side)
            Vec3i R = Cross(F, U);                   // pravotočivě => „right“

            return (F, R, U);
        }

        private static Vec3i Cross(Vec3i a, Vec3i b)
        {
            return new Vec3i(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X
            );
        }

        private static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        /// <summary>
        /// Spočítá střed koule a poloměr, která zakrývá skenovací kvádr o velikosti x,y,z.
        /// x,y,z jsou v blocích (1..15).
        /// </summary>
        public void GetScanSphere(int x, int y, int z, out Vec3d center, out double radius)
        {
            x = Clamp(x, 1, 15);
            y = Clamp(y, 1, 15);
            z = Clamp(z, 1, 15);

            var (F, R, U) = GetLocalBasis();

            // Startovní blok kvádru = diagonální soused (NE v základní pozici)
            BlockPos startBlockPos = Pos.AddCopy(F.X + R.X, F.Y + R.Y, F.Z + R.Z);

            // Střed prvního bloku
            Vec3d startCenter = startBlockPos.ToVec3d().Add(0.5, 0.5, 0.5);

            // Posun středu kvádru od prvního bloku
            double halfSpanX = (x - 1) / 2.0;
            double halfSpanY = (y - 1) / 2.0;
            double halfSpanZ = (z - 1) / 2.0;

            center = startCenter
                .Add(F.X * halfSpanX + R.X * halfSpanY + U.X * halfSpanZ,
                     F.Y * halfSpanX + R.Y * halfSpanY + U.Y * halfSpanZ,
                     F.Z * halfSpanX + R.Z * halfSpanY + U.Z * halfSpanZ);

            // Poloměr koule, která kvádr pokryje
            radius = 0.5 * Math.Sqrt(x * x + y * y + z * z);
        }

        /// <summary>
        /// Vrátí true, pokud je daná pozice uvnitř skenovacího kvádru o velikosti x,y,z.
        /// </summary>
        public bool IsInsideScanCuboid(Vec3d position, int x, int y, int z)
        {
            x = Clamp(x, 1, 15);
            y = Clamp(y, 1, 15);
            z = Clamp(z, 1, 15);

            var (F, R, U) = GetLocalBasis();

            GetScanSphere(x, y, z, out Vec3d center, out _); // použijeme jen center

            Vec3d d = position - center;

            double coordF = d.X * F.X + d.Y * F.Y + d.Z * F.Z;
            double coordR = d.X * R.X + d.Y * R.Y + d.Z * R.Z;
            double coordU = d.X * U.X + d.Y * U.Y + d.Z * U.Z;

            double halfX = x / 2.0;
            double halfY = y / 2.0;
            double halfZ = z / 2.0;

            return Math.Abs(coordF) <= halfX
                && Math.Abs(coordR) <= halfY
                && Math.Abs(coordU) <= halfZ;
        }

        /// <summary>
        /// Vrátí entity uvnitř skenovacího kvádru o velikosti x,y,z.
        /// (Používá kouli pro broad-phase a pak filtr na kvádr.)
        /// </summary>
        public IEnumerable<Entity> GetEntitiesInScanRegion(int x, int y, int z, System.Func<Entity, bool> extraFilter = null)
        {
            GetScanSphere(x, y, z, out Vec3d center, out double radius);

            var entities = Api.World.GetEntitiesAround(center, (float)radius, (float)radius, null);

            foreach (var entity in entities)
            {
                if (extraFilter != null && !extraFilter(entity)) continue;

                Vec3d epos = entity.ServerPos.XYZ; // nebo entity.PosXYZ, podle toho co používáš

                if (IsInsideScanCuboid(epos, x, y, z))
                {
                    yield return entity;
                }
            }
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            // Používej cachované hodnoty (funguje na obou stranách)
            if (maxCharge > 0 && baseConsumptionFactor > 0)
            {
                if (currentCharge > 0)
                {
                    float daysRemaining = currentCharge / (24000f * baseConsumptionFactor);
                    float maxDays = maxCharge / (24000f * baseConsumptionFactor);
                    float chargePercent = (currentCharge / maxCharge) * 100f;

                    dsc.AppendLine(Lang.Get("signalslink:blockinfo-charge",
                        daysRemaining.ToString("F1"),
                        maxDays.ToString("F0"),
                        chargePercent.ToString("F0")));

                    float operationalVolume = GetOperationalVolume();

                    // OPRAVENÝ výpočet - stejný jako v behavioru
                    const float REFERENCE_DAYS = 100f;
                    float volumeRatio = operationalVolume / referenceVolume;
                    float actualConsumptionPerTick = volumeRatio * baseConsumptionFactor / REFERENCE_DAYS / 24000f;

                    // Ochrana proti dělení nulou
                    if (actualConsumptionPerTick > 0)
                    {
                        float referenceDaysRemaining = currentCharge / (24000f * baseConsumptionFactor);
                        float actualDaysRemaining = referenceDaysRemaining / volumeRatio;

                        dsc.AppendLine(Lang.Get("signalslink:blockinfo-charge-at-volume",
                            operationalVolume.ToString("F0"),
                            actualDaysRemaining.ToString("F1")));
                    }
                }
                else
                {
                    dsc.AppendLine(Lang.Get("signalslink:blockinfo-charge-empty"));
                }
            }

            base.GetBlockInfo(forPlayer, dsc);


        }
    }
}
