using signals.src.signalNetwork;
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
    public class BEManagedChute : BlockEntity, IBESignalReceptor, IPaperConditionsHost
    {
        private int checkRateMs;

        public byte signalState;
        private int remaining;
        private bool unlimited;

        private byte sourceSlot;
        private byte targetSlot;

        private const byte SOURCE_SLOT = 2;
        private const byte TARGET_SLOT = 1;
        private const byte UNLIMITED_TRANSFER = 15;

        private float itemFlowRate = 1f;
        private float itemFlowAccum;

        private static AssetLocation hopperTumble = new AssetLocation("sounds/block/hoppertumble");

        private IItemTransfer transfer;

        private BlockPos lastInputPos;
        private BlockPos lastOutputPos;

        private string conditionsText = null;
        private PaperConditionsEvaluator conditionsEvaluator;

        public int SignalInputsCount => 3;

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
                MarkDirty();
            }
        }

        public override void Initialize(ICoreAPI api)
        {
            this.parseBlockProperties();

            base.Initialize(api);

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
            if (!unlimited && remaining <= 0) return;
            if (Api?.World == null || !(Api is ICoreServerAPI)) return;

            EnsureTransfer();
            if (transfer == null) return;

            itemFlowAccum = Math.Min(itemFlowAccum + itemFlowRate, Math.Max(1f, itemFlowRate * 2f));
            if (itemFlowAccum < 1f) return;

            int canByRate = (int)itemFlowAccum;
            int allowedNow = unlimited ? canByRate : Math.Min(canByRate, remaining);
            if (allowedNow <= 0) return;

            // Šablona pro operaci – 1 kus, direct merge
            ItemStackMoveOperation opTemplate = new ItemStackMoveOperation(Api.World, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, 1);

            bool remainingChanged = false;
            int movedTotal = 0;
            while (movedTotal < allowedNow)
            {
                int movedNow = transfer.TryMoveOneItem(opTemplate);
                if (movedNow <= 0) break;

                try
                {
                    if (!(this.Api.World.Rand.NextDouble() >= 0.2))
                        this.Api.World.PlaySoundAt(hopperTumble, this.Pos, 0.0, range: 8f, volume: 0.5f);
                }
                catch (Exception) { }

                movedTotal += movedNow;
                itemFlowAccum -= movedNow;

                if (!unlimited)
                {
                    remaining -= movedNow;
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

            // Když se změnilo napojení, starý transfer zahodíme
            if (transfer != null &&
                (!inputPos.Equals(lastInputPos) || !outputPos.Equals(lastOutputPos)))
            {
                transfer = null;
            }

            if (transfer != null) return;

            // Zatím InputSlot/OutputSlot signál = 0 (default sloty)
            transfer = ItemTransferFactory.CreateTransfer(Api, inputPos, outputPos, sourceSlot, targetSlot, ConditionsEvaluator);

            lastInputPos = inputPos;
            lastOutputPos = outputPos;
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

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
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
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            tree.SetString("conditionsText", ConditionsText);

            // sync transfer state to client
            tree.SetBool("unlimited", unlimited);
            tree.SetInt("remaining", remaining);
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            var sel = forPlayer?.CurrentBlockSelection;

            if(sel?.SelectionBoxIndex<3)
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