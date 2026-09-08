using signals.src;
using signals.src.signalNetwork;
using SignalsLink.src.signals;
using SignalsLink.src.signals.managedchute.transporting;
using SignalsLink.src.signals.paperConditions;
using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.managedchute
{
    public class BEManagedChute : BlockEntity, IBESignalReceptor, IPaperConditionsHost, ISignalBuffer, IConditionOutputSink
    {
        private int checkRateMs;

        public byte signalState;
        private int remaining;
        private bool unlimited;

        private byte sourceSlot;
        private byte targetSlot;

        private const byte SOURCE_SLOT = 2;
        private const byte TARGET_SLOT = 1;
        private const byte OUTPUT = 3;
        private const byte UNLIMITED_TRANSFER = 15;

        // Output anchor (index 3), driven by `output` blocks on paper and pushed on the signal
        // tick, the same way the valve and the damper do it. It is what lets a chute report on its
        // own two ends instead of only carrying things between them.
        public byte outputState;
        private byte? lastPushedOutput;
        private SignalNetworkMod signalMod;

        private float itemFlowRate = 1f;
        private float itemFlowAccum;

        private static AssetLocation hopperTumble = new AssetLocation("sounds/block/hoppertumble");

        private IItemTransfer transfer;

        private BlockPos lastInputPos;
        private BlockPos lastOutputPos;

        // BlockEntity instances the cached transfer was built from. A neighbour's block entity can
        // be recreated under us (world load, chunk reload, block exchange) — the container's
        // inventory object is then replaced and the cached transfer keeps operating on a dead one,
        // silently moving nothing. Comparing identities each tick catches that.
        private BlockEntity lastInputBE;
        private BlockEntity lastOutputBE;

        private string conditionsText = null;
        private PaperConditionsEvaluator conditionsEvaluator;

        public int SignalInputsCount => 4; // Input, Target, Source, Output (selection boxes 0..3)

        public string ConditionsText
        {
            get
            {
                return conditionsText;
            }
            set
            {
                conditionsText = value;
                conditionsEvaluator?.SetConditionsText(conditionsText);
                remaining = 0; // reconfiguring clears the pending batch (it may no longer be valid)
                MarkDirty();
            }
        }

        /// <summary>Wrench (sneak) clears the pending buffer and stops continuous mode.</summary>
        public void ClearBuffer()
        {
            if (remaining == 0 && !unlimited) return;
            remaining = 0;
            unlimited = false;
            MarkDirty();
        }

        public override void Initialize(ICoreAPI api)
        {
            this.parseBlockProperties();

            base.Initialize(api);

            signalMod = api.ModLoader.GetModSystem<SignalNetworkMod>();
            signalMod.RegisterSignalTickListener(OnSignalNetworkTick);

            if (!(api is ICoreServerAPI))
                return;
            this.RegisterDelayedCallback(dt => this.RegisterGameTickListener(this.MoveItem, this.checkRateMs), 10 + api.World.Rand.Next(200));
        }

        private void parseBlockProperties()
        {
            if (this.Block?.Attributes == null)
                return;
            this.checkRateMs = this.Block.Attributes["item-checkrateMs"].AsInt(200);
        }

        public void MoveItem(float dt)
        {
            if (Api?.World == null || !(Api is ICoreServerAPI)) return;

            bool hasCredit = unlimited || remaining > 0;

            // Blocks with an `output` action are evaluated on EVERY tick, whatever the Input pin
            // says - that is what makes the pin a reading of the current state rather than a
            // memory of the last thing that moved it. With nothing to carry the pass still runs,
            // only with its action rail closed.
            //
            // The one exception is the cheap one: with no credit AND no output block there is
            // nothing to compute and nothing to do, so an idle chute costs a tick of nothing.
            if (!hasCredit && !ConditionsEvaluator.HasAnyOutput) { SetOutput(0); return; }

            EnsureTransfer();

            if (transfer == null) { SetOutput(0); return; }

            if (!hasCredit)
            {
                transfer.EvaluateOutputs();
                return;
            }

            itemFlowAccum = Math.Min(itemFlowAccum + itemFlowRate, Math.Max(1f, itemFlowRate * 2f));
            if (itemFlowAccum < 1f) return;

            int canByRate = (int)itemFlowAccum;
            int allowedNow = unlimited ? canByRate : Math.Min(canByRate, remaining);
            if (allowedNow <= 0) return;

            // Šablona pro operaci – 1 kus, direct merge
            ItemStackMoveOperation opTemplate = new ItemStackMoveOperation(Api.World, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, 1);

            bool remainingChanged = false;
            decimal movedTotal = 0;
            while (movedTotal < allowedNow)
            {
                TransferOperationResult moveResult = transfer.TryMove(opTemplate);
                if (!moveResult.Success) break;

                int triggerCost = moveResult.TriggerCost;

                try
                {
                    if (!(this.Api.World.Rand.NextDouble() >= 0.2))
                        this.Api.World.PlaySoundAt(hopperTumble, this.Pos, 0.0, range: 8f, volume: 0.5f);
                }
                catch (Exception) { }

                movedTotal += moveResult.MovedAmount;
                // A batched transfer (the 'amount N' directive) is ONE action, not N ticks worth of
                // flow. Charging the rate limiter the full batch would drive the accumulator deep
                // negative and stall the chute for ~N ticks; cap it at this tick's whole allowance
                // so a batch costs at most one tick. Unbatched moves (1 piece) are unaffected.
                itemFlowAccum -= System.Math.Min((float)moveResult.MovedAmount, System.Math.Max(1f, (float)canByRate));

                if (!unlimited)
                {
                    remaining -= triggerCost;
                    if (remaining < 0) remaining = 0; // an atomic amount-op may overshoot the last bit
                    remainingChanged = true;
                    if (remaining <= 0) break;
                }
            }

            if (remainingChanged)
            {
                this.MarkDirty();
            }

        }

        public void OnValueChanged(NodePos pos, byte value)
        {
            switch(pos.index)
            {
                case 0:
                    processInput(pos, value);
                    break;
                case SOURCE_SLOT:
                    ProcessSourceSlot(pos, value);
                    break;
                case TARGET_SLOT:
                    ProcessTargetSlot(pos, value);
                    break;
            }
        }

        private void ProcessTargetSlot(NodePos pos, byte value)
        {
            if (pos.index != TARGET_SLOT) return;
            if (targetSlot == value) return;

            targetSlot = value;
            transfer = null;

            this.MarkDirty();
        }

        private void ProcessSourceSlot(NodePos pos, byte value)
        {
            if (pos.index != SOURCE_SLOT) return;
            if (sourceSlot == value) return;

            sourceSlot = value;
            transfer = null;

            this.MarkDirty();
        }

        public void processInput(NodePos pos, byte value)
        {
            if (pos.index != 0) return;
            if (signalState == value) return;

            if (value >= 1 && value <= 7)
            {
                remaining += 1 << (value - 1);
                // volitelně omez max, aby ti to nepřeteklo při blbnutí signálem
                // remaining = Math.Min(remaining, 1000000);
            }

            // 15 = trvale otevřeno
            if (value == UNLIMITED_TRANSFER)
            {
                unlimited = true;
            }
            else if (signalState == UNLIMITED_TRANSFER && value != UNLIMITED_TRANSFER)
            {
                // odchod z 15: zavři "unlimited", ale kredit nech jak byl
                unlimited = false;
            }

            signalState = value;

            this.MarkDirty();

        }

        public BlockFacing GetInputFace()
        {
            Block currentBlock = Api.World.BlockAccessor.GetBlock(Pos);
            string side = currentBlock.Variant?["side"];

            // fallback, kdyby varianta chyběla
            if (side == null) return BlockFacing.DOWN;

            return BlockFacing.FromCode(side);
        }

        public BlockFacing GetOutputFace()
        {
            return GetInputFace().Opposite;
        }

        public BlockPos GetInputBlockPos()
        {
            BlockFacing input = GetInputFace();
            return Pos.AddCopy(input.Normali.X, input.Normali.Y, input.Normali.Z);
        }

        public BlockPos GetOutputBlockPos()
        {
            BlockFacing output = GetOutputFace();
            return Pos.AddCopy(output.Normali.X, output.Normali.Y, output.Normali.Z);
        }

        private void EnsureTransfer()
        {
            BlockPos inputPos = GetInputBlockPos();
            BlockPos outputPos = GetOutputBlockPos();

            BlockEntity inputBE = Api.World.BlockAccessor.GetBlockEntity(inputPos);
            BlockEntity outputBE = Api.World.BlockAccessor.GetBlockEntity(outputPos);

            // Starý transfer zahodíme, když se změnilo napojení, nebo když se sousední
            // BlockEntity mezitím vytvořila znovu (load světa, reload chunku, výměna bloku) –
            // transfer si drží referenci na inventář, který by už byl mrtvý.
            if (transfer != null &&
                (!inputPos.Equals(lastInputPos) || !outputPos.Equals(lastOutputPos)
                 || !ReferenceEquals(inputBE, lastInputBE) || !ReferenceEquals(outputBE, lastOutputBE)))
            {
                transfer = null;
            }

            if (transfer != null) return;

            // Zatím InputSlot/OutputSlot signál = 0 (default sloty)
            transfer = ItemTransferFactory.CreateTransfer(Api, inputPos, outputPos, sourceSlot, targetSlot, ConditionsEvaluator, this);

            lastInputPos = inputPos;
            lastOutputPos = outputPos;
            lastInputBE = inputBE;
            lastOutputBE = outputBE;
        }

        private PaperConditionsEvaluator ConditionsEvaluator
        {
            get
            {
                if(conditionsEvaluator == null)
                {
                    conditionsEvaluator = new PaperConditionsEvaluator();
                    conditionsEvaluator.SetConditionsText(ConditionsText);
                }   
                return conditionsEvaluator;
            }
        }

        /// <summary>Sets the chute's Output anchor.</summary>
        public void SetOutput(byte value)
        {
            if (outputState == value) return;
            outputState = value;
            MarkDirty();
        }

        /// <summary>
        /// What one evaluation pass computed for the pin, the 0 of a pass in which no `output`
        /// block held included. See <see cref="IConditionOutputSink"/>.
        /// </summary>
        public void ApplyOutput(int pin, byte value)
        {
            SetOutput(value);
        }

        private void OnSignalNetworkTick()
        {
            BEBehaviorSignalConnector beb = GetBehavior<BEBehaviorSignalConnector>();
            if (beb == null) return;
            if (lastPushedOutput == outputState) return;

            ISignalNode node = beb.GetNodeAt(new NodePos(this.Pos, OUTPUT));
            if (node == null) return;

            signalMod.netManager.UpdateSource(node, outputState);
            lastPushedOutput = outputState;
            MarkDirty();
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            signalMod?.DisposeSignalTickListener(OnSignalNetworkTick);
            transfer = null;
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            signalMod?.DisposeSignalTickListener(OnSignalNetworkTick);
            transfer = null;
        }

        public override void OnBlockBroken(IPlayer byPlayer)
        {
            base.OnBlockBroken(byPlayer);
            transfer = null;
        }

        // pokud používáš OnNeighbourBlockChange:
        public void OnNeighbourBlockChange(BlockPos neibpos)
        {
            transfer = null;
        }

        public override void OnExchanged(Block block)
        {
            base.OnExchanged(block);
            // orientace / varianta se mohla změnit -> nové pozice
            transfer = null;
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);

            ConditionsText = tree.GetString("conditionsText", null);

            // sync transfer state to client
            unlimited = tree.GetBool("unlimited", false);
            remaining = tree.GetInt("remaining", 0);

            // Edge detector of the Input pin. Persisted together with the buffer above: without it
            // the pin looks like it went 0 -> N after every load, which credits a phantom batch.
            signalState = (byte)tree.GetInt("signalState", 0);
            outputState = (byte)tree.GetInt("outputState", 0);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            tree.SetString("conditionsText", ConditionsText);

            // sync transfer state to client
            tree.SetBool("unlimited", unlimited);
            tree.SetInt("remaining", remaining);
            tree.SetInt("signalState", signalState);
            tree.SetInt("outputState", outputState);
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            var sel = forPlayer?.CurrentBlockSelection;

            if (sel?.SelectionBoxIndex < SignalInputsCount)
            {
                base.GetBlockInfo(forPlayer, dsc);
                return;
            }

            if (unlimited)
            {
                dsc.AppendLine(Lang.Get("signalslink:managedchute-info-unlimited"));
            }
            else if (remaining > 0)
            {
                dsc.AppendLine(Lang.Get("signalslink:managedchute-info-remaining", remaining));
            }

            base.GetBlockInfo(forPlayer, dsc);
        }

    }
}