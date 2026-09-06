using signals.src;
using signals.src.signalNetwork;
using SignalsLink.src.signals.behaviours;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.igniter
{
    /// <summary>
    /// Igniter block entity. Anchor 0 = Input, anchor 1 = Output.
    ///
    /// A rising edge on Input costs one charge and fires one spray of embers. There is deliberately
    /// <b>no continuous mode</b>: every spray drains the temporal gear, and a pin left at 15 would
    /// quietly burn a gear to nothing.
    ///
    /// It never places a <c>fire</c> block — vanilla fire spreads, and a signal that could set a
    /// base alight is not a component anyone wants. Instead it asks the target itself to light up,
    /// which is why the list of things it can ignite is a list, not a general mechanism.
    /// </summary>
    public class BEIgniter : BlockEntity, IBESignalReceptor, ITemporalChargeHolder
    {
        public const int INPUT = 0;
        public const int OUTPUT = 1;

        /// <summary>Air cells the embers keep falling through before they burn out.</summary>
        private int sparkFallDistance = 3;

        /// <summary>Charge one spray costs, in the same tick-scale the gear is measured in.</summary>
        private float chargePerIgnition = 1000f;

        private float currentCharge;

        public byte signalState;
        public byte outputState;
        private byte? lastPushedOutput;

        private SignalNetworkMod signalMod;

        private static readonly AssetLocation igniteSound = new AssetLocation("sounds/effect/embers");

        // Bumped server-side on every spray and synced, so the client can draw the arc. Same trick
        // the valve's drain uses (drainPulse) - particles are client-side and non-deterministic,
        // so the server decides what was hit and the client only illustrates it.
        private int sparkPulse;
        private int lastClientSparkPulse = -1;

        // Whether this client has seen the counter at all yet. Without it the first value that
        // arrives from the server differs from the starting -1 and the block greets every joining
        // player with a spray it never fired.
        private bool sparkPulseSynced;

        public int SignalInputsCount => 2;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            sparkFallDistance = Block?.Attributes?["sparkFallDistance"].AsInt(3) ?? 3;
            chargePerIgnition = Block?.Attributes?["chargePerIgnition"].AsFloat(1000f) ?? 1000f;

            signalMod = api.ModLoader.GetModSystem<SignalNetworkMod>();
            signalMod.RegisterSignalTickListener(OnSignalNetworkTick);

            if (api is ICoreClientAPI)
            {
                RegisterGameTickListener(ClientSparkTick, 50);
            }
        }

        #region Aiming

        /// <summary>
        /// Where the embers are thrown. Built from the same basis the BlockSensor scans along:
        /// forward from <c>orientation</c>, up from the opposite of <c>side</c>, right from their
        /// cross product — so the two aim modes keep meaning the same thing however the block is
        /// mounted and turned.
        /// </summary>
        private BlockPos GetAimedPosition()
        {
            Block block = Api.World.BlockAccessor.GetBlock(Pos);
            string orientation = block?.Variant?["orientation"];
            string side = block?.Variant?["side"];
            if (orientation == null || side == null) return null;

            Vec3i forward = Facing(orientation);
            Vec3i down = Facing(side);
            Vec3i up = new Vec3i(-down.X, -down.Y, -down.Z);
            Vec3i right = Cross(forward, up);

            Vec3i offset = block.Variant["aim"] == "fwdright"
                ? new Vec3i(forward.X + right.X, forward.Y + right.Y, forward.Z + right.Z)
                : forward;

            return Pos.AddCopy(offset.X, offset.Y, offset.Z);
        }

        /// <summary>Outward direction of the barrel, for placing the muzzle and aiming the sparks.</summary>
        private Vec3f GetBarrelDirection()
        {
            Block block = Api.World.BlockAccessor.GetBlock(Pos);
            string orientation = block?.Variant?["orientation"];
            string side = block?.Variant?["side"];
            if (orientation == null || side == null) return new Vec3f(0, 0, -1);

            Vec3i forward = Facing(orientation);
            Vec3i down = Facing(side);
            Vec3i up = new Vec3i(-down.X, -down.Y, -down.Z);
            Vec3i right = Cross(forward, up);

            Vec3f dir = block.Variant["aim"] == "fwdright"
                ? new Vec3f(forward.X + right.X, forward.Y + right.Y, forward.Z + right.Z)
                : new Vec3f(forward.X, forward.Y, forward.Z);

            dir.Normalize();
            return dir;
        }

        private static Vec3i Facing(string code)
        {
            switch (code)
            {
                case "north": return new Vec3i(0, 0, -1);
                case "south": return new Vec3i(0, 0, 1);
                case "east": return new Vec3i(1, 0, 0);
                case "west": return new Vec3i(-1, 0, 0);
                case "up": return new Vec3i(0, 1, 0);
                default: return new Vec3i(0, -1, 0);
            }
        }

        private static Vec3i Cross(Vec3i a, Vec3i b)
        {
            return new Vec3i(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X);
        }

        #endregion

        #region Igniting

        /// <summary>
        /// One spray. Tries the aimed block; if that is open air the embers fall, cell by cell,
        /// until they hit something to try — or until they burn out. A block that is there but
        /// refuses to light simply stops them.
        /// </summary>
        private bool Fire()
        {
            BlockPos pos = GetAimedPosition();
            if (pos == null) return false;

            for (int step = 0; ; step++)
            {
                if (TryIgniteAt(pos)) return true;
                if (!IsOpenAir(pos)) return false;      // something solid, but nothing lit
                if (step >= sparkFallDistance) return false; // embers burnt out on the way down

                pos = pos.DownCopy();
            }
        }

        private bool IsOpenAir(BlockPos pos)
        {
            Block block = Api.World.BlockAccessor.GetBlock(pos);
            return block == null || block.Replaceable >= 6000;
        }

        /// <summary>
        /// Asks the thing at this position to light itself.
        ///
        /// The game's <c>IIgnitable</c> wants an <c>EntityAgent</c>, which an igniter has not got.
        /// What saves this is that the concrete block entities also offer entity-free methods, so
        /// they are called directly. The consequence is that this is a list of supported targets
        /// rather than a general mechanism: a block with only IIgnitable cannot be lit.
        /// </summary>
        private bool TryIgniteAt(BlockPos pos)
        {
            BlockEntity be = Api.World.BlockAccessor.GetBlockEntity(pos);
            if (be == null) return false;

            // Charcoal pit before the piles: it is what a lit pile of firewood turns into.
            if (be is BlockEntityCharcoalPit pit)
            {
                if (pit.Lit) return false;
                pit.IgniteNow();
                return true;
            }

            // TryIgnite reports nothing, so success is read back off the pile itself. Checking
            // first also stops a spray being counted as a hit on something already alight.
            if (be is BlockEntityCoalPile coalPile)
            {
                if (coalPile.IsBurning) return false;
                coalPile.TryIgnite();
                return coalPile.IsBurning;
            }

            if (be is BlockEntityGroundStorage pile)
            {
                if (pile.IsBurning) return false;
                pile.TryIgnite();
                return pile.IsBurning;
            }

            if (be is BlockEntityFirepit firepit)
            {
                if (firepit.IsBurning) return false;
                firepit.igniteFuel();
                return firepit.IsBurning;
            }

            return false;
        }

        #endregion

        #region Signals

        public void OnValueChanged(NodePos pos, byte value)
        {
            if (pos.index != INPUT) return;

            byte previous = signalState;
            signalState = value;
            MarkDirty();

            // Rising edge only: one spray per signal, never a stream.
            if (previous != 0 || value == 0) return;
            if (Api is not ICoreServerAPI) return;

            Spray();
        }

        private void Spray()
        {
            if (currentCharge < chargePerIgnition)
            {
                SetOutput(0);
                return;
            }

            // The gear powers the spray, not the outcome - a miss costs the same as a hit.
            currentCharge -= chargePerIgnition;

            bool lit = Fire();
            SetOutput((byte)(lit ? 1 : 0));

            sparkPulse++;
            MarkDirty();

            Api.World.PlaySoundAt(igniteSound, Pos, 0.0, range: 12f, volume: 0.6f);
        }

        public void SetOutput(byte value)
        {
            if (outputState == value) return;
            outputState = value;
            MarkDirty();
        }

        private void OnSignalNetworkTick()
        {
            BEBehaviorSignalConnector beb = GetBehavior<BEBehaviorSignalConnector>();
            if (beb == null) return;
            if (lastPushedOutput == outputState) return;

            ISignalNode node = beb.GetNodeAt(new NodePos(Pos, OUTPUT));
            if (node == null) return;

            signalMod.netManager.UpdateSource(node, outputState);
            lastPushedOutput = outputState;
            MarkDirty();
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            signalMod?.DisposeSignalTickListener(OnSignalNetworkTick);
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            signalMod?.DisposeSignalTickListener(OnSignalNetworkTick);
        }

        #endregion

        #region Temporal charge

        public float GetCurrentCharge() => currentCharge;

        public void SetCurrentCharge(float charge)
        {
            currentCharge = charge;
            MarkDirty();
        }

        /// <summary>
        /// Only the EntitySensor scales its draw by a volume; an igniter pays per spray, so this
        /// is a formality the interface asks for. (It is worth dropping from the interface one day
        /// and leaving the sensor a private method - see igniter-design.md.)
        /// </summary>
        public float GetOperationalVolume() => 1f;

        #endregion

        #region Particles (client)

        private void ClientSparkTick(float dt)
        {
            if (Api is not ICoreClientAPI capi) return;

            if (!sparkPulseSynced)
            {
                // Adopt whatever the counter is at, without drawing anything.
                sparkPulseSynced = true;
                lastClientSparkPulse = sparkPulse;
                return;
            }

            if (sparkPulse == lastClientSparkPulse) return;

            lastClientSparkPulse = sparkPulse;
            SpawnSparks(capi);
        }

        /// <summary>
        /// Embers leave the muzzle horizontally and gravity bends them into a short arc.
        /// Deliberately short and steep: a ballistic path drifts sideways as it falls, and if the
        /// sparks visibly flew a block further than the logic reached, the picture would lie about
        /// what got lit. Same discipline as the sleeve's shallow sag.
        /// </summary>
        private void SpawnSparks(ICoreClientAPI capi)
        {
            Vec3f dir = GetBarrelDirection();
            Vec3d muzzle = new Vec3d(
                Pos.X + 0.5 + dir.X * 0.45,
                Pos.Y + 0.5 + dir.Y * 0.45,
                Pos.Z + 0.5 + dir.Z * 0.45);

            const float Speed = 1.6f;
            const float Spread = 0.35f;

            SimpleParticleProperties sparks = new SimpleParticleProperties(
                12, 20,
                ColorUtil.ToRgba(255, 255, 190, 60),
                new Vec3d(muzzle.X - 0.03, muzzle.Y - 0.03, muzzle.Z - 0.03),
                new Vec3d(muzzle.X + 0.03, muzzle.Y + 0.03, muzzle.Z + 0.03),
                new Vec3f(dir.X * Speed - Spread, dir.Y * Speed - Spread, dir.Z * Speed - Spread),
                new Vec3f(dir.X * Speed + Spread, dir.Y * Speed + Spread, dir.Z * Speed + Spread),
                0.8f,   // life length
                1.2f,   // gravity - pulls the arc down quickly, so it stays honest
                0.15f, 0.35f,
                EnumParticleModel.Cube);

            sparks.WithTerrainCollision = true;
            sparks.VertexFlags = 128; // glowing embers

            capi.World.SpawnParticles(sparks);
        }

        #endregion

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            signalState = (byte)tree.GetInt("signalState", 0);
            outputState = (byte)tree.GetInt("outputState", 0);
            currentCharge = tree.GetFloat("currentCharge", 0f);
            sparkPulse = tree.GetInt("sparkPulse", 0);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetInt("signalState", signalState);
            tree.SetInt("outputState", outputState);
            tree.SetFloat("currentCharge", currentCharge);
            tree.SetInt("sparkPulse", sparkPulse);
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);

            // Looking at a connector should say what the connector is, not how much charge the
            // block has left. The wire anchors come first in the selection boxes, so anything below
            // SignalInputsCount is one of them.
            if (forPlayer?.CurrentBlockSelection?.SelectionBoxIndex < SignalInputsCount) return;

            int shots = chargePerIgnition > 0 ? (int)(currentCharge / chargePerIgnition) : 0;
            dsc.AppendLine(Lang.Get("signalslink:igniter-info-shots", shots));
        }
    }
}
