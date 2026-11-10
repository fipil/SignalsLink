using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.managedchute.transporting
{
    public sealed class FruitPressDrive : BlockEntityBehavior
    {
        private const string OwnerKey = "signalslinkAutomaticPress";
        private bool automatic;
        private long listener;

        public FruitPressDrive(BlockEntity blockEntity) : base(blockEntity) { }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor world)
        {
            automatic = tree.GetBool(OwnerKey);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            tree.SetBool(OwnerKey, automatic);
        }

        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            base.Initialize(api, properties);
            if (!automatic || api.Side != EnumAppSide.Server || !FruitPressMash.CanDrive) return;

            // Vanilla restores its animator after initializing the behaviors. A save on the
            // first animation frame can have ownership but no active animation to restore.
            Blockentity.RegisterDelayedCallback(dt =>
            {
                if (!automatic) return;
                var press = (BlockEntityFruitPress)Blockentity;
                if (FruitPressMash.IsPressing(press)) Start();
                else FruitPressMash.Press(press);
            }, 0);
        }

        internal void Start()
        {
            automatic = true;
            if (listener == 0) listener = Blockentity.RegisterGameTickListener(Tick, 50);
        }

        private void Tick(float dt)
        {
            var press = (BlockEntityFruitPress)Blockentity;
            if (!FruitPressMash.IsPressing(press) || press.MashSlot.Empty) Stop();
            else FruitPressMash.DriveSqueeze(press);
        }

        internal void Stop()
        {
            if (!automatic) return;
            automatic = false;
            if (listener != 0) Blockentity.UnregisterGameTickListener(listener);
            listener = 0;
            Blockentity.MarkDirty();
        }

        public override void OnReceivedClientPacket(IPlayer player, int packetId, byte[] data)
        {
            // Vanilla has handled the packet by now, or refused it (no permission, out of reach).
            // Only an Unscrew that really let the screw up ends the drive; stopping on the packet
            // alone would let a refused click leave the screw down with nothing squeezing.
            if (packetId == 1003 && !FruitPressMash.IsPressing((BlockEntityFruitPress)Blockentity)) Stop();
        }
    }
}
