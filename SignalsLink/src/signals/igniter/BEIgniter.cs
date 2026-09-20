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

        /// <summary>Shape of one ember arc. Tunable per aim variant from the blocktype.</summary>
        private struct SparkArc
        {
            public float Speed;
            public float Gravity;
            public float Life;

            /// <summary>How long the embers are in the air before the target catches.</summary>
            public int DelayMs;
        }

        // Two arcs, because the two situations look nothing alike. Landing on the block right in
        // front is a short drooping spray; reaching an open cell and dropping through it needs to
        // carry further and hang longer. Which one is used is decided by what the SERVER found, so
        // the picture cannot disagree with what was actually lit.
        private SparkArc directArc = new SparkArc { Speed = 1.6f, Gravity = 1.2f, Life = 0.8f, DelayMs = 250 };
        private SparkArc fallingArc = new SparkArc { Speed = 1.5f, Gravity = 1.0f, Life = 1.2f, DelayMs = 450 };

        /// <summary>Cells the embers fell before they hit something; 0 = the aimed cell itself.</summary>
        private int sparkFallSteps;

        private float currentCharge;

        public byte signalState;
        public byte outputState;
        private byte? lastPushedOutput;

        private SignalNetworkMod signalMod;

        // Picked at random per spray, so a bank of igniters does not click in unison.
        private static readonly AssetLocation[] igniteSounds =
        {
            new AssetLocation("signalslink:sounds/effect/igniter1"),
            new AssetLocation("signalslink:sounds/effect/igniter2"),
        };

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
            var arcs = Block?.Attributes?["sparkArc"];
            directArc = ReadArc(arcs?["direct"], directArc);
            fallingArc = ReadArc(arcs?["falling"], fallingArc);

            signalMod = api.ModLoader.GetModSystem<SignalNetworkMod>();
            signalMod.RegisterSignalTickListener(OnSignalNetworkTick);

            if (api is ICoreClientAPI)
            {
                RegisterGameTickListener(ClientSparkTick, 50);
            }
        }

        private static SparkArc ReadArc(Vintagestory.API.Datastructures.JsonObject cfg, SparkArc fallback)
        {
            if (cfg == null || !cfg.Exists) return fallback;

            return new SparkArc
            {
                Speed = cfg["speed"].AsFloat(fallback.Speed),
                Gravity = cfg["gravity"].AsFloat(fallback.Gravity),
                Life = cfg["life"].AsFloat(fallback.Life),
                DelayMs = cfg["delayMs"].AsInt(fallback.DelayMs),
            };
        }

        #region Aiming

        /// <summary>
        /// Where the embers are thrown. Built from the same basis the BlockSensor scans along:
        /// forward from <c>orientation</c>, up from the opposite of <c>side</c>, right from their
        /// cross product — so the two aim modes keep meaning the same thing however the block is
        /// mounted and turned.
        /// </summary>
        /// <summary>
        /// The block's own axes in world terms: <paramref name="forward"/> from <c>orientation</c>,
        /// <paramref name="up"/> from the opposite of <c>side</c>, and right from their cross
        /// product. The canonical pose the shape is drawn in is orientation=north, side=down, so
        /// there forward is -Z, up is +Y and right is +X.
        /// </summary>
        private bool TryGetBasis(out Vec3i forward, out Vec3i up, out Vec3i right, out bool diagonal)
        {
            forward = up = right = null;
            diagonal = false;

            Block block = Api.World.BlockAccessor.GetBlock(Pos);
            string orientation = block?.Variant?["orientation"];
            string side = block?.Variant?["side"];
            if (orientation == null || side == null) return false;

            forward = Facing(orientation);
            Vec3i down = Facing(side);
            up = new Vec3i(-down.X, -down.Y, -down.Z);
            right = Cross(forward, up);
            diagonal = block.Variant["aim"] == "fwdright";
            return true;
        }

        private BlockPos GetAimedPosition()
        {
            if (!TryGetBasis(out Vec3i forward, out Vec3i up, out Vec3i right, out bool diagonal)) return null;

            Vec3i offset = diagonal
                ? new Vec3i(forward.X + right.X, forward.Y + right.Y, forward.Z + right.Z)
                : forward;

            return Pos.AddCopy(offset.X, offset.Y, offset.Z);
        }

        /// <summary>Outward direction of the barrel — the way the embers are thrown.</summary>
        private Vec3f GetBarrelDirection()
        {
            if (!TryGetBasis(out Vec3i forward, out Vec3i up, out Vec3i right, out bool diagonal))
            {
                return new Vec3f(0, 0, -1);
            }

            Vec3f dir = diagonal
                ? new Vec3f(forward.X + right.X, forward.Y + right.Y, forward.Z + right.Z)
                : new Vec3f(forward.X, forward.Y, forward.Z);

            dir.Normalize();
            return dir;
        }

        /// <summary>
        /// World position the embers leave from: the middle of the model's <c>core</c> cuboid.
        /// It is configured per aim variant in the blocktype (<c>muzzle</c>, in the shape's own
        /// 0..16 coordinates), because the diagonal model has the whole engine turned 45 degrees
        /// and the core sits somewhere else entirely.
        ///
        /// Turning that local point into a world one uses the block's own axes rather than a table
        /// of cases, so it comes out right on any wall and any rotation. Note the sign on the last
        /// term: +Z in the canonical pose points BEHIND the barrel, since forward there is -Z.
        /// </summary>
        private Vec3d GetMuzzleWorldPos()
        {
            float mx = 8f, my = 5.5f, mz = 3.5f;

            var cfg = Block?.Attributes?["muzzle"];
            if (cfg != null && cfg.Exists)
            {
                mx = cfg["x"].AsFloat(mx);
                my = cfg["y"].AsFloat(my);
                mz = cfg["z"].AsFloat(mz);
            }

            double dRight = mx / 16.0 - 0.5;
            double dUp = my / 16.0 - 0.5;
            double dBack = mz / 16.0 - 0.5;

            if (!TryGetBasis(out Vec3i forward, out Vec3i up, out Vec3i right, out bool _))
            {
                return new Vec3d(Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5);
            }

            double ox = right.X * dRight + up.X * dUp - forward.X * dBack;
            double oy = right.Y * dRight + up.Y * dUp - forward.Y * dBack;
            double oz = right.Z * dRight + up.Z * dUp - forward.Z * dBack;

            return new Vec3d(Pos.X + 0.5 + ox, Pos.Y + 0.5 + oy, Pos.Z + 0.5 + oz);
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
        /// Where this spray ends up. Walks the aimed block and then, if that is open air, the cells
        /// below it until something can be lit or the embers burn out.
        ///
        /// It deliberately only LOOKS. Lighting happens later, once the embers have had time to get
        /// there — a target that caught fire before the sparks arrived looked wrong.
        /// </summary>
        private BlockPos ResolveImpact(out int fallSteps)
        {
            fallSteps = 0;

            BlockPos pos = GetAimedPosition();
            if (pos == null) return null;

            for (int step = 0; ; step++)
            {
                fallSteps = step;

                if (CanIgniteAt(pos)) return pos.Copy();
                if (!IsOpenAir(pos)) return null;        // something solid, but nothing to light
                if (step >= sparkFallDistance) return null; // embers burnt out on the way down

                pos = pos.DownCopy();
            }
        }

        /// <summary>
        /// Would this thing light? The same questions <see cref="TryIgniteAt"/> asks, without doing
        /// anything — and asked in the same order, so the cell chosen here is the cell lit later.
        /// </summary>
        private bool CanIgniteAt(BlockPos pos)
        {
            BlockEntity be = Api.World.BlockAccessor.GetBlockEntity(pos);
            if (be == null) return false;

            if (be is BlockEntityCharcoalPit pit) return !pit.Lit;
            if (be is BlockEntityCoalPile coalPile) return !coalPile.IsBurning && coalPile.CanIgnite;
            if (be is BlockEntityGroundStorage pile) return !pile.IsBurning && pile.CanIgnite;
            if (be is BlockEntityFirepit firepit)
            {
                return !firepit.IsBurning && firepit.GetIgnitableState(5f) == EnumIgniteState.IgniteNow;
            }

            return false;
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

            // TryIgnite reports nothing, so success is read back off the pile itself. Asking
            // CanIgnite first is not just tidiness: these are the game's own guards, and calling
            // in without them is how an empty firepit took the server down with a
            // NullReferenceException out of igniteFuel().
            if (be is BlockEntityCoalPile coalPile)
            {
                if (coalPile.IsBurning || !coalPile.CanIgnite) return false;
                coalPile.TryIgnite();
                return coalPile.IsBurning;
            }

            if (be is BlockEntityGroundStorage pile)
            {
                if (pile.IsBurning || !pile.CanIgnite) return false;
                pile.TryIgnite();
                return pile.IsBurning;
            }

            if (be is BlockEntityFirepit firepit)
            {
                if (firepit.IsBurning) return false;

                // A firepit with nothing in it is not ignitable, and pointing an igniter at one is
                // an easy mistake to make. Let the firepit itself say so.
                if (firepit.GetIgnitableState(5f) != EnumIgniteState.IgniteNow) return false;

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

            // Decide where the embers go, then throw them. Lighting waits until they arrive.
            BlockPos impact = ResolveImpact(out int fallSteps);
            sparkFallSteps = fallSteps;

            sparkPulse++;
            MarkDirty();

            AssetLocation sound = igniteSounds[Api.World.Rand.Next(igniteSounds.Length)];
            Api.World.PlaySoundAt(sound, Pos, 0.0, range: 12f, volume: 0.6f);

            int delayMs = (fallSteps <= 0 ? directArc : fallingArc).DelayMs;
            RegisterDelayedCallback(dt => Land(impact), delayMs);
        }

        /// <summary>
        /// The embers have arrived. Everything is re-checked here rather than trusted from the
        /// moment of firing: the world had time to change while they were in the air, and the
        /// block may not even be there any more.
        /// </summary>
        private void Land(BlockPos impact)
        {
            if (Api is not ICoreServerAPI) return;

            bool lit = impact != null && TryIgniteAt(impact);
            SetOutput((byte)(lit ? 1 : 0));
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
        /// Embers leave the muzzle along the barrel and only bend downwards once they are clear of
        /// the igniter, so the spray reads as being shot rather than dribbled.
        ///
        /// Two things decide that. The <b>spread is never allowed to point backwards or down</b> —
        /// applying one symmetric spread to all three axes gave some of them a downward start, and
        /// those fell straight out through the underside of the barrel. And the speed has to carry
        /// them past the edge of the block before gravity matters: at 4.5 they cross the remaining
        /// quarter block in well under a tenth of a second, so the first stretch is essentially
        /// straight and the arc happens outside.
        ///
        /// The arc still has to stay short. A ballistic path drifts sideways as it falls, and if
        /// the sparks visibly flew a block further than the logic reached, the picture would lie
        /// about what got lit — the same discipline as the sleeve's shallow sag.
        /// </summary>
        private void SpawnSparks(ICoreClientAPI capi)
        {
            Vec3f dir = GetBarrelDirection();
            Vec3d muzzle = GetMuzzleWorldPos();

            // Nothing to fall through means the block in front took the sparks head on.
            SparkArc arc = sparkFallSteps <= 0 ? directArc : fallingArc;

            const float Spread = 0.45f;  // sideways scatter, across the barrel
            const float Lift = 0.25f;    // a touch of loft, so none of them start out falling

            Vec3f baseVel = new Vec3f(dir.X * arc.Speed, dir.Y * arc.Speed, dir.Z * arc.Speed);

            // Scatter sideways and a little upward, never downward: the lower bound keeps the base
            // velocity on the axis the barrel points along.
            Vec3f minVel = new Vec3f(
                baseVel.X - Spread * (1f - System.Math.Abs(dir.X)),
                baseVel.Y,
                baseVel.Z - Spread * (1f - System.Math.Abs(dir.Z)));

            Vec3f maxVel = new Vec3f(
                baseVel.X + Spread * (1f - System.Math.Abs(dir.X)),
                baseVel.Y + Lift,
                baseVel.Z + Spread * (1f - System.Math.Abs(dir.Z)));

            SimpleParticleProperties sparks = new SimpleParticleProperties(
                12, 20,
                ColorUtil.ToRgba(255, 255, 190, 60),
                new Vec3d(muzzle.X - 0.02, muzzle.Y - 0.02, muzzle.Z - 0.02),
                new Vec3d(muzzle.X + 0.02, muzzle.Y + 0.02, muzzle.Z + 0.02),
                minVel,
                maxVel,
                arc.Life,
                arc.Gravity,
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
            sparkFallSteps = tree.GetInt("sparkFallSteps", 0);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetInt("signalState", signalState);
            tree.SetInt("outputState", outputState);
            tree.SetFloat("currentCharge", currentCharge);
            tree.SetInt("sparkPulse", sparkPulse);
            tree.SetInt("sparkFallSteps", sparkFallSteps);
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
