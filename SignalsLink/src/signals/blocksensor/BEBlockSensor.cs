using Newtonsoft.Json.Linq;
using signals.src;
using signals.src.signalNetwork;
using signals.src.transmission;
using SignalsLink.src.signals.blocksensor.scanners;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.blocksensor
{
    class BEBlockSensor : BlockEntity, IBESignalReceptor
    {
        public byte state;
        public byte outputState = 3;

        public bool IsPowered;
        public string ScanningDirection = "fwd";

        BlockFacing Orientation = BlockFacing.NORTH;
        BlockFacing Side = BlockFacing.DOWN;

        SignalNetworkMod signalMod;

        private static SensorScannerFactory scannerFactory;
        BlockPos scannedPosition;
        IBlockSensorScanner activeScanner;

        public BEBlockSensor()
        {
            if (scannerFactory == null)
            {
                scannerFactory = new SensorScannerFactory();
            }
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            if (this.Block.Variant["orientation"] != null)
            {
                BlockFacing facing = BlockFacing.FromCode(this.Block.Variant["orientation"]);
                if (facing != null)
                {
                    Orientation = facing;
                }
            }
            if (this.Block.Variant["side"] != null)
            {
                BlockFacing facing = BlockFacing.FromCode(this.Block.Variant["side"]);
                if (facing != null)
                {
                    Side = facing;
                }
            }

            signalMod = api.ModLoader.GetModSystem<SignalNetworkMod>();
            signalMod.RegisterSignalTickListener(OnSignalNetworkTick);

            SetPowered(state != 0);
            if (Api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(OnSlowServerTick, 300);
            }
        }

        private void OnSlowServerTick(float dt)
        {
            outputState = CalculateOutputSignal(state);
        }

        public void OnNeighbourBlockChange(BlockPos neibpos)
        {
            if (Api.Side == EnumAppSide.Client)
                return;

            if (neibpos.Equals(ScannedPosition))
            {
                scannedPosition = null;
                activeScanner = null;
                outputState = CalculateOutputSignal(state);
                MarkDirty();
            }
        }

        private byte CalculateOutputSignal(byte inputSignal)
        {
            if(inputSignal == 0 || !IsPowered)
            {
                return 0;
            }

            Block block = Api.World.BlockAccessor.GetBlock(ScannedPosition);
            BlockEntity blockEntity = Api.World.BlockAccessor.GetBlockEntity(ScannedPosition);

            if(activeScanner?.CanScan(block, blockEntity, inputSignal) != true)
            {
                activeScanner = scannerFactory.GetScanner(block, blockEntity, inputSignal);
            }

            // Calculate signal
            return activeScanner.CalculateSignal(Api.World, ScannedPosition, block, blockEntity, inputSignal);
        }

        BlockPos ScannedPosition {
            get
            {
                if (scannedPosition == null)
                {
                    scannedPosition = GetScannedBlockPosition();
                }
                return scannedPosition;
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

        public void OnSignalNetworkTick()
        {
            BEBehaviorSignalConnector beb = GetBehavior<BEBehaviorSignalConnector>();
            if (beb == null) return;
            ISignalNode nodeProbe = beb.GetNodeAt(new NodePos(this.Pos, 0));
            ISignalNode nodeSource = beb.GetNodeAt(new NodePos(this.Pos, 1));
            signalMod.netManager.UpdateSource(nodeSource, outputState);
            MarkDirty();
        }

        public void OnValueChanged(NodePos pos, byte value)
        {
            if (pos.index != 0) return;
            if( state == value) return;

            state = value;

            SetPowered(state != 0);
            SetScanningDirection(ScanningDirection);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            state = tree.GetBytes("state", new byte[1] { 0 })[0];
            ScanningDirection = tree.GetString("scanning", "fwd");
            IsPowered = state!=0;
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetBytes("state", new byte[1] { state });
            tree.SetString("scanning", ScanningDirection);
        }

        public void SetPowered(bool powered)
        {
            if (IsPowered != powered)
            {
                IsPowered = powered;
                UpdateBlockState();
                MarkDirty(true);
            }
        }

        public void SetScanningDirection(string direction)
        {
            if (ScanningDirection != direction)
            {
                ScanningDirection = direction;
                UpdateBlockState();
                MarkDirty(true);

                BlockPos scannedPos = GetScannedBlockPosition();
                scannedPosition = scannedPos;
                activeScanner = null;
                PlayTimeswitchSound();
                if(state != 0)
                    SpawnTemporalParticles(scannedPos);
            }
        }

        public void UpdateBlockState()
        {
            Block currentBlock = Api.World.BlockAccessor.GetBlock(Pos);

            string newCode = currentBlock.Code.Domain + ":blocksensor-" +
                             (IsPowered ? "on" : "off") + "-" +
                             ScanningDirection + "-" +
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

        public BlockPos GetScannedBlockPosition()
        {
            Block currentBlock = Api.World.BlockAccessor.GetBlock(Pos);
            string orientation = currentBlock.Variant?["orientation"];
            string side = currentBlock.Variant?["side"];

            if (orientation == null || side == null)
            {
                Api.World.Logger.Warning("BlockSensor at {0} has missing variants", Pos);
                return Pos;
            }

            Vec3i forward = DirectionFromFacing(orientation);
            Vec3i down = DirectionFromFacing(side);
            Vec3i up = new Vec3i(-down.X, -down.Y, -down.Z);
            Vec3i right = Cross(forward, up);

            Vec3i offset = new Vec3i(0, 0, 0);

            switch (ScanningDirection.ToLowerInvariant())
            {
                case "fwd": offset = forward; break;
                case "fwdup": offset = forward + up; break;
                case "fwddown": offset = forward - up; break;
                case "fwdright": offset = forward + right; break;
                case "fwdrightup": offset = forward + right + up; break;
                case "fwdrightdown": offset = forward + right - up; break;
                default:
                    Api.World.Logger.Warning("Unknown ScanningDirection '{0}'", ScanningDirection);
                    break;
            }

            return Pos.AddCopy(offset.X, offset.Y, offset.Z);
        }

        private Vec3i DirectionFromFacing(string facing)
        {
            switch (facing.ToLowerInvariant())
            {
                case "north": return new Vec3i(0, 0, -1);
                case "south": return new Vec3i(0, 0, 1);
                case "east": return new Vec3i(1, 0, 0);
                case "west": return new Vec3i(-1, 0, 0);
                case "up": return new Vec3i(0, 1, 0);
                case "down": return new Vec3i(0, -1, 0);
                default: return new Vec3i(0, 0, 0);
            }
        }

        private Vec3i Cross(Vec3i a, Vec3i b)
        {
            return new Vec3i(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X
            );
        }

        public void SpawnTemporalParticles(BlockPos pos)
        {
            if (Api.Side == EnumAppSide.Client)
            {
                SimpleParticleProperties particles = new SimpleParticleProperties(
                minQuantity: 5,
                maxQuantity: 10,
                ColorUtil.ToRgba(200, 0, 255, 255),
                new Vec3d(pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5),
                new Vec3d(pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5),
                new Vec3f(-0.1f, -0.05f, -0.1f),
                new Vec3f(0.1f, 0.05f, 0.1f)
            );

                particles.MinSize = 0.1f;
                particles.MaxSize = 0.3f;
                particles.LifeLength = 1.5f;
                particles.GravityEffect = 0.0f;
                particles.WithTerrainCollision = false;
                particles.ParticleModel = EnumParticleModel.Quad;
                particles.SelfPropelled = false;
                particles.OpacityEvolve = EvolvingNatFloat.create(
                    EnumTransformFunction.LINEAR,
                    -255 / particles.LifeLength
                );

                Api.World.SpawnParticles(particles);
            }
        }

        public void PlayTimeswitchSound()
        {
            if (Api.Side == EnumAppSide.Client)
            {
                Api.World.PlaySoundAt(new AssetLocation("signalslink:sounds/effect/metalslide"), Pos.X, Pos.Y, Pos.Z);
            }
        }
    }
}
