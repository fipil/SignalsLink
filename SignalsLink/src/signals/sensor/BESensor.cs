using signals.src.signalNetwork;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace SignalsLink.src.signals.sensor
{
    class BESensor : BlockEntity, IBESignalReceptor
    {
        public byte state;

        public bool IsPowered;
        public string ScanningDirection = "fwd";

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            BEBehaviorSignalSensor sensor = GetBehavior<BEBehaviorSignalSensor>();
            sensor?.commute(5);

            SetPowered(state != 0);
        }

        public void OnServerGameTick(float dt)
        {
        }

        public void OnValueChanged(NodePos pos, byte value)
        {
            if (pos.index != 0) return;
            if( state == value) return;

            state = value;
            BEBehaviorSignalSensor sensor = GetBehavior<BEBehaviorSignalSensor>();
            sensor?.commute(5);

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
            }
        }

        public void UpdateBlockState()
        {
            Block currentBlock = Api.World.BlockAccessor.GetBlock(Pos);

            // Sestav nový kód varianty
            string newCode = currentBlock.Code.Domain + ":sensor-" +
                             (IsPowered ? "on" : "off") + "-" +
                             ScanningDirection + "-" +
                             currentBlock.Variant["orientation"] + "-" +
                             currentBlock.Variant["side"];

            // Zkontroluj, jestli už není správná varianta
            if (currentBlock.Code.Path == newCode.Split(':')[1])
            {
                return; // Už je správný blok, nic nedělej
            }

            Block newBlock = Api.World.GetBlock(new AssetLocation(newCode));

            if (newBlock != null)
            {
                Api.World.BlockAccessor.ExchangeBlock(newBlock.Id, Pos);
                Api.World.BlockAccessor.MarkBlockDirty(Pos);
            }
        }
    }
}
