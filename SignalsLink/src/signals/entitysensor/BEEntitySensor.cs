using signals.src;
using signals.src.signalNetwork;
using signals.src.transmission;
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

    public class BEEntitySensor : BlockEntity, IBESignalReceptor
    {
        public byte x,y,z,input;
        public byte kind,tags,entity,numbers;

        public PoweredMode Powered = PoweredMode.Off;

        SignalNetworkMod signalMod;

        private static SensorScannerFactory scannerFactory;

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
            if (Api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(OnSlowServerTick, 300);
            }
        }

        private void OnSlowServerTick(float dt)
        {
            CalculateOutputSignal();
        }

        private void CalculateOutputSignal()
        {
            if (Powered == PoweredMode.Off)
            {
                return;
            }

            var entities = GetEntitiesInScanRegion(x, y, z);

            SetPowered(entities.Count()>0 ? PoweredMode.Active : PoweredMode.On); 
            numbers = (byte)entities.Count();
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

        public void OnSignalNetworkTick()
        {
            BEBehaviorSignalConnector beb = GetBehavior<BEBehaviorSignalConnector>();
            if (beb == null) return;
            ISignalNode nodeSource = beb.GetNodeAt(new NodePos(this.Pos, 7));
            signalMod.netManager.UpdateSource(nodeSource, numbers);
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
                case 3:
                    if (input == value) return;
                    input = value;
                    break;
                default:
                    return;
            };

            SetPowered(DeterminePoweredFromInputs());
        }

        private PoweredMode DeterminePoweredFromInputs()
        {
            return (x > 0 && y > 0 && z > 0)
                            ? PoweredMode.On
                            : PoweredMode.Off;
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            byte[] xyzi = tree.GetBytes("xyzinput", new byte[] { 0, 0, 0, 0 }); 
            x = xyzi[0];
            y = xyzi[1];
            z = xyzi[2];
            input = xyzi[3];

            Powered = DeterminePoweredFromInputs();
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetBytes("xyzinput", new byte[4] { x, y, z, input });
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

            // TODO: uprav podle skutečné VS API signatury
            // Příklad pro něco jako: GetEntitiesAround(center, horRadius, vertRadius)
            var entities = Api.World.GetEntitiesAround(center, (float)radius, (float)radius, (e) => e.Alive);

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
    }
}
