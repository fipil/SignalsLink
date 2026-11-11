using System.Collections.Generic;
using System.Text;
using signals.src;
using signals.src.signalNetwork;
using SignalsLink.YTT.src.stoker;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SignalsLink.YTT.src.signalman
{
    /// <summary>
    /// The signalman: a plate beside the track that watches the block of rail in front of it.
    /// The output says what is there - 0 nothing, 1 a moving train, 2 a standing one - the
    /// players pin how many players ride it, and the input gives the train an order: 1 to 10 its
    /// power, 15 a whistle, anything else nothing.
    ///
    /// Its MODE, turned with a wrench, says which trains are its business: all, or only those
    /// going right, or only those going left, as seen from the plate looking at the track. A
    /// train the mode does not take is invisible to it, outputs and orders alike.
    ///
    /// An order is given ONCE to each train that turns up, and again only when the value on the
    /// wire changes while the train is still there. It sees loaded trains only: a train that
    /// reaches a loaded station is material by the time it gets here.
    /// </summary>
    public class BESignalman : BlockEntity, IBESignalReceptor
    {
        public const int InputPin = 0;
        public const int OutputPin = 1;
        public const int PlayersPin = 2;

        /// <summary>The block right in front. Placement is the setting.</summary>
        public const int Reach = 1;

        private readonly LocomotiveFinder finder = new LocomotiveFinder();
        private readonly SignalmanMemory memory = new SignalmanMemory();
        private readonly TravelDirections directions = new TravelDirections();

        private byte inputSignal;
        private byte outputSignal;
        private byte playersSignal;
        private byte? lastPushedOutput;
        private byte? lastPushedPlayers;

        /// <summary>What happened last, as a lang key suffix for the info panel.</summary>
        private string state = "";

        /// <summary>Which way the train on the block goes, for the info panel.</summary>
        private TravelDirection direction;

        private SignalNetworkMod signalMod;
        private bool unloaded;

        private YttCommands Commands => Api?.ModLoader?.GetModSystem<SignalsLinkYttMod>()?.Commands;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            signalMod = api.ModLoader.GetModSystem<SignalNetworkMod>();
            signalMod?.RegisterSignalTickListener(PushOutput);

            // A moving train crosses a block in a fraction of a second; "moving" has to mean something.
            if (api.Side == EnumAppSide.Server) RegisterGameTickListener(Watch, 250);
        }

        // ------------------------------------------------------------------ pins

        public void OnValueChanged(NodePos pos, byte value)
        {
            if (unloaded || pos.index != InputPin || inputSignal == value) return;

            inputSignal = value;
            MarkDirty();

            if (Api is ICoreServerAPI) Watch(0);
        }

        private void PushOutput()
        {
            if (unloaded) return;

            BEBehaviorSignalConnector connector = GetBehavior<BEBehaviorSignalConnector>();
            if (connector == null) return;

            Push(connector, OutputPin, outputSignal, ref lastPushedOutput);
            Push(connector, PlayersPin, playersSignal, ref lastPushedPlayers);
        }

        private void Push(BEBehaviorSignalConnector connector, int pin, byte value, ref byte? lastPushed)
        {
            if (lastPushed == value) return;

            ISignalNode node = connector.GetNodeAt(new NodePos(Pos, pin));
            if (node == null) return;

            signalMod.netManager.UpdateSource(node, value);
            lastPushed = value;
        }

        // ------------------------------------------------------------------ the work

        private void Watch(float dt)
        {
            if (Api is not ICoreServerAPI || unloaded) return;

            long now = Api.World.ElapsedMilliseconds;
            BlockFacing facing = Facing();
            LocomotiveSighting sighting = finder.Find(Api.World, Pos, facing, Reach);

            directions.Forget(now);

            if (!sighting.Present)
            {
                memory.Forget();
                Report(0, 0, "notrain", TravelDirection.Unknown);
                return;
            }

            TravelDirection going = directions.Observe(sighting.ConvoyHead, AlongRight(sighting.Vehicles, facing), now);

            // Not this plate's train: invisible, outputs and orders alike.
            if (!SignalmanRules.ModeMatches(Mode, going))
            {
                Report(0, 0, going == TravelDirection.Unknown ? "direction-unknown" : "direction-other", going);
                return;
            }

            byte output = SignalmanRules.OutputFor(true, sighting.Standing);
            byte players = SignalmanRules.PlayersOutput(TrainPassengers.Players(Api.World, sighting.ConvoyHead));
            SignalmanCommand command = SignalmanRules.CommandFor(inputSignal);

            if (command == SignalmanCommand.Nothing)
            {
                Report(output, players, sighting.Standing ? "standing" : "moving", going);
                return;
            }

            if (!memory.ShouldApply(sighting.ConvoyHead, inputSignal))
            {
                // Already given to this train; only the outputs move on.
                Report(output, players, state, going);
                return;
            }

            if (sighting.Locomotive == null)
            {
                Report(output, players, "noengine", going);
                return;
            }

            YttCommands commands = Commands;
            string result;

            if (command == SignalmanCommand.Throttle)
            {
                result = commands?.SetThrottle(sighting.Locomotive, inputSignal) switch
                {
                    ThrottleResult.Applied => "throttled",
                    ThrottleResult.Locked => "locked",
                    _ => "unavailable",
                };
            }
            else
            {
                // A whistle on cooldown is not refused for good: the next tick tries again.
                if (commands?.CanWhistle != true) result = "unavailable";
                else if (!commands.Whistle(sighting.Locomotive)) { Report(output, players, "whistle-wait", going); return; }
                else result = "whistled";
            }

            memory.Applied(sighting.ConvoyHead, inputSignal);
            Report(output, players, result, going);
        }

        /// <summary>Each vehicle's position along the plate's "right" axis - what a direction is read from.</summary>
        private static IEnumerable<(long, double)> AlongRight(IReadOnlyList<Entity> vehicles, BlockFacing facing)
        {
            Vec3i right = facing.GetCW().Normali;

            foreach (Entity vehicle in vehicles)
            {
                yield return (vehicle.EntityId, vehicle.Pos.X * right.X + vehicle.Pos.Z * right.Z);
            }
        }

        private void Report(byte output, byte players, string reason, TravelDirection going)
        {
            if (outputSignal == output && playersSignal == players && state == reason && direction == going) return;

            outputSignal = output;
            playersSignal = players;
            state = reason ?? "";
            direction = going;
            MarkDirty();
        }

        /// <summary>The way the cabinet looks: the orientation variant set when it was placed.</summary>
        private BlockFacing Facing()
        {
            string orientation = Standing()?.Variant?["orientation"];
            return BlockFacing.FromCode(orientation) ?? BlockFacing.NORTH;
        }

        /// <summary>Which trains it is for: the mode variant, turned with a wrench.</summary>
        private SignalmanMode Mode => SignalmanRules.ModeFor(Standing()?.Variant?["mode"]);

        /// <summary>The block as it stands in the WORLD; the entity's own Block property is stale after a swap.</summary>
        private Block Standing() => Api?.World?.BlockAccessor?.GetBlock(Pos);

        // ------------------------------------------------------------------ the player

        /// <summary>Wrench: the next mode, by swapping the variant. The entity and its wires stay. Server side.</summary>
        public void CycleMode()
        {
            Block standing = Standing();
            if (standing?.Code == null) return;

            string wanted = SignalmanRules.VariantOf(SignalmanRules.NextMode(Mode));
            Block swapped = Api.World.GetBlock(standing.CodeWithVariant("mode", wanted));
            if (swapped == null) return;

            Api.World.BlockAccessor.ExchangeBlock(swapped.BlockId, Pos);
            Api.World.BlockAccessor.MarkBlockDirty(Pos);
            MarkDirty();
            Watch(0);
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
        {
            // Looking at a pin: Signals names the pin, nothing else belongs there.
            int pins = (Block as BlockSignalman)?.PinBoxes ?? 0;
            if (forPlayer?.CurrentBlockSelection?.SelectionBoxIndex < pins) return;

            base.GetBlockInfo(forPlayer, sb);

            BlockFacing facing = Facing();
            SignalmanMode mode = Mode;

            sb.AppendLine(mode == SignalmanMode.Both
                ? Lang.Get("signalslinkytt:signalman-mode-both")
                : Lang.Get("signalslinkytt:signalman-mode-one", Compass(mode == SignalmanMode.Right ? facing.GetCW() : facing.GetCCW())));

            switch (SignalmanRules.CommandFor(inputSignal))
            {
                case SignalmanCommand.Throttle:
                    sb.AppendLine(Lang.Get("signalslinkytt:signalman-doing-throttle", inputSignal));
                    break;
                case SignalmanCommand.Whistle:
                    sb.AppendLine(Lang.Get("signalslinkytt:signalman-doing-whistle"));
                    break;
                default:
                    sb.AppendLine(Lang.Get("signalslinkytt:signalman-doing-sensor"));
                    break;
            }

            if (!string.IsNullOrEmpty(state)) sb.AppendLine(Lang.Get("signalslinkytt:signalman-state-" + state));

            if (state != "notrain" && !string.IsNullOrEmpty(state))
            {
                sb.AppendLine(direction == TravelDirection.Unknown
                    ? Lang.Get("signalslinkytt:signalman-train-direction-unknown")
                    : Lang.Get("signalslinkytt:signalman-train-direction", Compass(direction == TravelDirection.Right ? facing.GetCW() : facing.GetCCW())));

                sb.AppendLine(Lang.Get("signalslinkytt:signalman-players", playersSignal));
            }
        }

        private static string Compass(BlockFacing facing) => Lang.Get("signalslinkytt:direction-" + facing.Code);

        // ------------------------------------------------------------------ lifecycle

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);

            inputSignal = (byte)tree.GetInt("inputSignal", 0);
            outputSignal = (byte)tree.GetInt("outputSignal", 0);
            playersSignal = (byte)tree.GetInt("playersSignal", 0);
            state = tree.GetString("state", "");
            direction = (TravelDirection)tree.GetInt("direction", 0);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            tree.SetInt("inputSignal", inputSignal);
            tree.SetInt("outputSignal", outputSignal);
            tree.SetInt("playersSignal", playersSignal);
            tree.SetString("state", state ?? "");
            tree.SetInt("direction", (int)direction);
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            unloaded = true;
            signalMod?.DisposeSignalTickListener(PushOutput);
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            unloaded = true;
            signalMod?.DisposeSignalTickListener(PushOutput);
        }
    }
}
