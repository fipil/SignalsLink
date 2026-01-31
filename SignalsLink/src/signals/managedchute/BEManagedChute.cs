using signals.src.signalNetwork;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

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

        public override void Initialize(ICoreAPI api)
        {
            this.parseBlockProperties();

            base.Initialize(api);

            if (!(api is ICoreServerAPI))
                return;
            this.RegisterDelayedCallback((Action<float>)(dt => this.RegisterGameTickListener(new Action<float>(this.MoveItem), this.checkRateMs)), 10 + api.World.Rand.Next(200));
        }

        private void parseBlockProperties()
        {
            if (this.Block?.Attributes == null)
                return;
            this.checkRateMs = this.Block.Attributes["item-checkrateMs"].AsInt(200);
        }

        public void MoveItem(float dt)
        {
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
    }
}
