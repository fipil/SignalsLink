using System.Text;
using signals.src;
using signals.src.signalNetwork;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SignalsLink.YTT.src.stoker
{
    /// <summary>
    /// The stoker. Watches the track N blocks in front of it and keeps the locomotive's fire the
    /// way the input pin asks: 0 leave it, 1 keep it burning, 2 keep it out. The output pin says
    /// whether the boiler burns.
    ///
    /// Levels, not events: it acts only on a difference between what is wanted and what is, so a
    /// held 1 lights the fire again when the fuel ran out and never pays for a fire that already
    /// burns. Lighting costs wear on a firestarter from its own slots, and only when the boiler
    /// really caught; putting out costs water from the LOCOMOTIVE's own boiler, as the button in
    /// its dialog does.
    /// </summary>
    public class BEStoker : BlockEntityOpenableContainer, IBESignalReceptor
    {
        public const int InputPin = 0;
        public const int OutputPin = 1;
        public const int SlotCount = 4;

        private readonly InventoryGeneric inventory;
        private readonly LocomotiveFinder finder = new LocomotiveFinder();

        private byte inputSignal;
        private byte outputSignal;
        private byte? lastPushedOutput;
        private int reach = StokerRules.DefaultReach;

        /// <summary>What it is doing, as a lang key suffix for the info panel.</summary>
        private string state = "";

        private SignalNetworkMod signalMod;
        private bool unloaded;

        public override InventoryBase Inventory => inventory;

        public override string InventoryClassName => "signalslinkstoker";

        public int Reach => reach;

        public BEStoker()
        {
            // The id must contain a dash: the game splits className/instanceId on it.
            inventory = new InventoryGeneric(SlotCount, "signalslinkstoker-0", null,
                (id, inv) => new ItemSlotIgnitionSource(inv));
        }

        private YttFire Fire => Api?.ModLoader?.GetModSystem<SignalsLinkYttMod>()?.Fire;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            reach = StokerRules.ClampReach(reach == 0
                ? Block?.Attributes?["reachDefault"].AsInt(StokerRules.DefaultReach) ?? StokerRules.DefaultReach
                : reach);

            signalMod = api.ModLoader.GetModSystem<SignalNetworkMod>();
            signalMod?.RegisterSignalTickListener(PushOutput);

            if (api.Side == EnumAppSide.Server)
            {
                inventory.SlotModified += _ => MarkDirty();

                // Level control, not transfer: a second is plenty to notice that the fire went
                // out by itself, or that a train came or left.
                RegisterGameTickListener(Tend, 1000);
            }
        }

        // ------------------------------------------------------------------ pins

        public void OnValueChanged(NodePos pos, byte value)
        {
            if (unloaded || pos.index != InputPin || inputSignal == value) return;

            inputSignal = value;
            MarkDirty();

            if (Api is not ICoreServerAPI) return;

            ShowState();
            Tend(0);
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

        private void Tend(float dt)
        {
            if (Api is not ICoreServerAPI || unloaded) return;

            YttFire fire = Fire;

            if (fire == null || !fire.Available)
            {
                Report(0, "unavailable");
                return;
            }

            LocomotiveSighting sighting = finder.Find(Api.World, Pos, Facing(), reach);

            if (sighting.Locomotive == null)
            {
                Report(0, sighting.Reason);
                return;
            }

            Entity locomotive = sighting.Locomotive;
            bool? burning = fire.IsBurning(locomotive);

            if (burning == null)
            {
                Report(0, "unavailable");
                return;
            }

            byte output = StokerRules.OutputFor(burning.Value);

            if (!sighting.Standing)
            {
                Report(output, "moving");
                return;
            }

            // A train ordered with a ticket is the ticket's business: a stoker told to keep the
            // boiler out would put out every fire the ticket lights, for water, every second.
            if (taxi.TaxiJob.Exists(locomotive))
            {
                Report(output, "taxi");
                return;
            }

            StokerCommand command = StokerRules.CommandFor(inputSignal);
            string reason;

            switch (StokerRules.Decide(command, burning.Value))
            {
                case StokerAction.Ignite:
                    reason = TryIgnite(fire, locomotive);
                    break;

                case StokerAction.Extinguish:
                    reason = TryExtinguish(fire, locomotive);
                    break;

                default:
                    reason = burning.Value ? "burning" : command == StokerCommand.Quench ? "out" : "idle";
                    break;
            }

            // Read again: the output reports what IS, after whatever was just done.
            Report(StokerRules.OutputFor(fire.IsBurning(locomotive) ?? burning.Value), reason);
        }

        /// <summary>
        /// Lights the boiler and pays for it - but only once it really caught. A held 1 at a
        /// locomotive with no fuel tries every second, and paying per attempt would eat a box of
        /// firestarters without lighting anything.
        /// </summary>
        private string TryIgnite(YttFire fire, Entity locomotive)
        {
            string result = FireKeeping.TryIgnite(fire, locomotive, () => FindIgnitionSource(fire), Api.World,
                Pos.ToVec3d().Add(0.5, 0.5, 0.5));

            if (result == "lit") MarkDirty();
            return result;
        }

        /// <summary>Water from the LOCOMOTIVE's boiler, as the button in its dialog takes it. See FireKeeping.</summary>
        private string TryExtinguish(YttFire fire, Entity locomotive) => FireKeeping.TryExtinguish(fire, locomotive);

        private ItemSlot FindIgnitionSource(YttFire fire)
        {
            for (int i = 0; i < inventory.Count; i++)
            {
                ItemSlot slot = inventory[i];
                if (!slot.Empty && fire.IsIgnitionSource(slot.Itemstack)) return slot;
            }

            return null;
        }

        private void Report(byte output, string reason)
        {
            if (outputSignal == output && state == reason) return;

            outputSignal = output;
            state = reason ?? "";
            MarkDirty();
        }

        /// <summary>The way the window looks: the orientation variant set when it was placed.</summary>
        private BlockFacing Facing()
        {
            string orientation = Api.World.BlockAccessor.GetBlock(Pos)?.Variant?["orientation"];
            return BlockFacing.FromCode(orientation) ?? BlockFacing.NORTH;
        }

        /// <summary>
        /// The window glows while the stoker is asked to keep the fire. Done by swapping the
        /// state variant, as the anchor does: ExchangeBlock keeps the entity, the inventory and
        /// the pins where Signals expects them.
        /// </summary>
        private void ShowState()
        {
            if (Api is not ICoreServerAPI) return;

            // From the WORLD, not this entity's Block property, which ExchangeBlock does not refresh.
            Block standing = Api.World.BlockAccessor.GetBlock(Pos);
            if (standing?.Code == null) return;

            string wanted = StokerRules.WindowLit(inputSignal) ? "on" : "off";
            if (standing.Variant["state"] == wanted) return;

            Block swapped = Api.World.GetBlock(standing.CodeWithVariant("state", wanted));
            if (swapped == null) return;

            Api.World.BlockAccessor.ExchangeBlock(swapped.BlockId, Pos);
            Api.World.BlockAccessor.MarkBlockDirty(Pos);
        }

        // ------------------------------------------------------------------ the player

        /// <summary>Wrench: the next reach, 1 to 10 and round again. Server side.</summary>
        public void CycleReach()
        {
            reach = StokerRules.NextReach(reach);
            MarkDirty();
            Tend(0);
        }

        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (Api.Side == EnumAppSide.Server)
            {
                byPlayer.InventoryManager?.OpenInventory(Inventory);
                return true;
            }

            if (Api is not ICoreClientAPI capi) return true;

            if (invDialog == null)
            {
                invDialog = new GuiDialogBlockEntityInventory(Lang.Get("signalslinkytt:stoker-title"), Inventory, Pos, SlotCount, capi);

                invDialog.OnClosed += () =>
                {
                    invDialog = null;
                    capi.Network.SendBlockEntityPacket(Pos, (int)EnumBlockEntityPacketId.Close);
                    byPlayer.InventoryManager?.CloseInventory(Inventory);
                };

                invDialog.TryOpen();
                capi.Network.SendPacketClient(Inventory.Open(byPlayer));
                byPlayer.InventoryManager?.OpenInventory(Inventory);
            }
            else
            {
                invDialog.TryClose();
            }

            return true;
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
        {
            // Looking at a pin: Signals names the pin, nothing else belongs there - not even
            // what the base class would add.
            int pins = (Block as BlockStoker)?.PinBoxes ?? 0;
            if (forPlayer?.CurrentBlockSelection?.SelectionBoxIndex < pins) return;

            base.GetBlockInfo(forPlayer, sb);

            sb.AppendLine(Lang.Get("signalslinkytt:stoker-reach", reach, Lang.Get("signalslinkytt:direction-" + Facing().Code)));

            // What the wire asks for, in words - a lever gives 15, which is not an order here,
            // and a player who wired one up has to be told so rather than left guessing.
            switch (StokerRules.CommandFor(inputSignal))
            {
                case StokerCommand.Burn:
                    sb.AppendLine(Lang.Get("signalslinkytt:stoker-doing-burn"));
                    break;
                case StokerCommand.Quench:
                    sb.AppendLine(Lang.Get("signalslinkytt:stoker-doing-quench"));
                    break;
                default:
                    sb.AppendLine(inputSignal == 0
                        ? Lang.Get("signalslinkytt:stoker-doing-none")
                        : Lang.Get("signalslinkytt:stoker-doing-ignored", inputSignal));
                    break;
            }

            if (!string.IsNullOrEmpty(state)) sb.AppendLine(Lang.Get("signalslinkytt:stoker-state-" + state));

            int lighters = 0;
            for (int i = 0; i < inventory.Count; i++)
            {
                if (!inventory[i].Empty) lighters += inventory[i].StackSize;
            }

            sb.AppendLine(Lang.Get("signalslinkytt:stoker-igniters", lighters));
        }

        // ------------------------------------------------------------------ lifecycle

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);

            inputSignal = (byte)tree.GetInt("inputSignal", 0);
            outputSignal = (byte)tree.GetInt("outputSignal", 0);
            reach = tree.GetInt("reach", reach);
            state = tree.GetString("state", "");
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            tree.SetInt("inputSignal", inputSignal);
            tree.SetInt("outputSignal", outputSignal);
            tree.SetInt("reach", reach);
            tree.SetString("state", state ?? "");
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            unloaded = true;
            signalMod?.DisposeSignalTickListener(PushOutput);
            invDialog?.TryClose();
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            unloaded = true;
            signalMod?.DisposeSignalTickListener(PushOutput);
            invDialog?.TryClose();
        }
    }

    /// <summary>
    /// A slot for things that light a fire. The vanilla rule - a firestarter, or anything that
    /// can ignite - because the slot is checked on the client too, where there is no probe. At
    /// lighting time the other mod itself is asked which of them it accepts.
    /// </summary>
    public class ItemSlotIgnitionSource : ItemSlot
    {
        public ItemSlotIgnitionSource(InventoryBase inventory) : base(inventory) { }

        public override bool CanHold(ItemSlot sourceSlot)
        {
            CollectibleObject collectible = sourceSlot?.Itemstack?.Collectible;
            if (collectible == null) return false;

            bool lights = collectible is ItemFirestarter || collectible.HasBehavior<BlockBehaviorCanIgnite>(false);

            return lights && base.CanHold(sourceSlot);
        }
    }
}
