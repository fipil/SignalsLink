using signals.src.signalNetwork;
using SignalsLink.src.signals.managedchute.transporting;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.managedchute
{
    public class BEManagedChute : BlockEntity, IBESignalReceptor
    {
        private int checkRateMs;

        public byte signalState;
        private int remaining;
        private bool unlimited;
        private bool placing;

        private const int PLACE_SIGNAL = 10;

        private float itemFlowRate = 1f;
        private float itemFlowAccum;

        private IItemTransfer transfer;

        private BlockPos lastInputPos;
        private BlockPos lastOutputPos;

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

            int movedTotal = 0;
            while (movedTotal < allowedNow)
            {
                int movedNow = transfer.TryMoveOneItem(opTemplate);
                if (movedNow <= 0) break;

                movedTotal += movedNow;
                itemFlowAccum -= movedNow;

                if (!unlimited)
                {
                    remaining -= movedNow;
                    if (remaining <= 0) break;
                }
            }
        }

        public void OnValueChanged(NodePos pos, byte value)
        {
            if (pos.index != 0) return;
            if (signalState == value) return;

            if (value >= 1 && value <= 7)
            {
                remaining += 1 << (value - 1);
                // volitelně omez max, aby ti to nepřeteklo při blbnutí signálem
                // remaining = Math.Min(remaining, 1000000);
                placing = false;
            }
            else if (value == PLACE_SIGNAL)
            {
                remaining += 1;
                placing = true;
            }

            // 8 = trvale otevřeno
            if (value == 8)
            {
                unlimited = true;
                placing = false;
            }
            else if (signalState == 8 && value != 8)
            {
                // odchod z 8: zavři "unlimited", ale kredit nech jak byl
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
            transfer = ItemTransferFactory.CreateTransfer(Api, inputPos, outputPos, 0, 0);

            lastInputPos = inputPos;
            lastOutputPos = outputPos;
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
            if (neibpos.Equals(lastInputPos) || neibpos.Equals(lastOutputPos))
            {
                transfer = null;
            }
        }

        public override void OnExchanged(Block block)
        {
            base.OnExchanged(block);
            // orientace / varianta se mohla změnit -> nové pozice
            transfer = null;
        }
    }
}