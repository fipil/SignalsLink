using System.Text;
using signals.src;
using signals.src.signalNetwork;
using SignalsLink.YTT.src.stoker;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SignalsLink.YTT.src.signalman
{
    /// <summary>
    /// The signalman: a plate beside the track that watches the block of rail in front of it.
    /// The output says what is there - 0 nothing, 1 a moving train, 2 a standing one - and the
    /// input gives the train an order: 1 to 10 its power, 15 a whistle, anything else nothing.
    ///
    /// An order is given ONCE to each train that turns up, and again only when the value on the
    /// wire changes while the train is still there. It sees loaded trains only: a train that
    /// reaches a loaded station is material by the time it gets here.
    /// </summary>
    public class BESignalman : BlockEntity, IBESignalReceptor
    {
        public const int InputPin = 0;
        public const int OutputPin = 1;

        /// <summary>The block right in front. Placement is the setting.</summary>
        public const int Reach = 1;

        private readonly LocomotiveFinder finder = new LocomotiveFinder();
        private readonly SignalmanMemory memory = new SignalmanMemory();

        private byte inputSignal;
        private byte outputSignal;
        private byte? lastPushedOutput;

        /// <summary>What happened last, as a lang key suffix for the info panel.</summary>
        private string state = "";

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
            if (unloaded || lastPushedOutput == outputSignal) return;

            BEBehaviorSignalConnector connector = GetBehavior<BEBehaviorSignalConnector>();
            ISignalNode node = connector?.GetNodeAt(new NodePos(Pos, OutputPin));
            if (node == null) return;

            signalMod.netManager.UpdateSource(node, outputSignal);
            lastPushedOutput = outputSignal;
        }

        // ------------------------------------------------------------------ the work

        private void Watch(float dt)
        {
            if (Api is not ICoreServerAPI || unloaded) return;

            LocomotiveSighting sighting = finder.Find(Api.World, Pos, Facing(), Reach);

            if (!sighting.Present)
            {
                memory.Forget();
                Report(0, "notrain");
                return;
            }

            byte output = SignalmanRules.OutputFor(true, sighting.Standing);
            SignalmanCommand command = SignalmanRules.CommandFor(inputSignal);

            if (command == SignalmanCommand.Nothing)
            {
                Report(output, sighting.Standing ? "standing" : "moving");
                return;
            }

            if (!memory.ShouldApply(sighting.ConvoyHead, inputSignal))
            {
                // Already given to this train; only the output moves on.
                Report(output, state);
                return;
            }

            if (sighting.Locomotive == null)
            {
                Report(output, "noengine");
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
                else if (!commands.Whistle(sighting.Locomotive)) { Report(output, "whistle-wait"); return; }
                else result = "whistled";
            }

            memory.Applied(sighting.ConvoyHead, inputSignal);
            Report(output, result);
        }

        private void Report(byte output, string reason)
        {
            if (outputSignal == output && state == reason) return;

            outputSignal = output;
            state = reason ?? "";
            MarkDirty();
        }

        /// <summary>The way the cabinet looks: the orientation variant set when it was placed.</summary>
        private BlockFacing Facing()
        {
            string orientation = Api.World.BlockAccessor.GetBlock(Pos)?.Variant?["orientation"];
            return BlockFacing.FromCode(orientation) ?? BlockFacing.NORTH;
        }

        // ------------------------------------------------------------------ the player

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
        {
            // Looking at a pin: Signals names the pin, nothing else belongs there.
            int pins = (Block as BlockSignalman)?.PinBoxes ?? 0;
            if (forPlayer?.CurrentBlockSelection?.SelectionBoxIndex < pins) return;

            base.GetBlockInfo(forPlayer, sb);

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
        }

        // ------------------------------------------------------------------ lifecycle

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);

            inputSignal = (byte)tree.GetInt("inputSignal", 0);
            outputSignal = (byte)tree.GetInt("outputSignal", 0);
            state = tree.GetString("state", "");
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            tree.SetInt("inputSignal", inputSignal);
            tree.SetInt("outputSignal", outputSignal);
            tree.SetString("state", state ?? "");
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
