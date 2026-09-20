using signals.src.hangingwires;
using signals.src.signalNetwork;
using SignalsLink.src.signals.behaviours;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.igniter
{
    /// <summary>
    /// Managed Igniter — on a signal edge it spits a spray of embers out of its barrel and lights
    /// whatever is in front of it. It derives from the Signals <c>BlockConnection</c> for its two
    /// wire anchors (0 = Input, 1 = Output), and is placed on any face like the BlockSensor.
    ///
    /// Clicking the block cycles the <c>aim</c> variant, exactly as clicking a BlockSensor cycles
    /// its scanning direction.
    /// </summary>
    public class BlockIgniter : BlockConnection
    {
        /// <summary>Straight ahead, and diagonally ahead-and-right. Two are enough: together with
        /// free placement and rotation they reach every one of the 26 neighbouring blocks.</summary>
        private static readonly string[] AimModes = { "fwd", "fwdright" };

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            // Charging comes first: a gear in hand means "top this up", never "turn the barrel".
            // TryCharge is a plain method, not an interaction override, so the block has to call it
            // itself — the same thing the EntitySensor does.
            BlockBehaviorTemporalCharge charge = GetBehavior<BlockBehaviorTemporalCharge>();
            if (charge != null && IsHoldingChargeItem(byPlayer, charge))
            {
                // TryCharge is server-only; on the client the click is simply swallowed so the two
                // sides do not disagree about which way the barrel points.
                if (world.Side == EnumAppSide.Server) charge.TryCharge(world, byPlayer, blockSel);
                return true;
            }

            // Wires next, resolved here rather than left to the base class (the same workaround
            // for the Signals BlockConnection that the BlockSensor carries). It has to swallow the
            // click even when no wire is actually attached: a click that landed on an anchor box
            // was aimed at the anchor, and must never fall through and turn the barrel instead.
            PlacingWiresMod wires = api.ModLoader.GetModSystem<PlacingWiresMod>();
            if (wires != null)
            {
                NodePos wireNode = GetNodePosForWire(world, blockSel, wires.GetPendingNode());
                if (wireNode != null && CanAttachWire(world, wireNode, wires.GetPendingNode()))
                {
                    wires.ConnectWire(wireNode, byPlayer, this);
                    return false;
                }
            }

            foreach (BlockBehavior behavior in BlockBehaviors)
            {
                EnumHandling handling = EnumHandling.PassThrough;
                bool result = behavior.OnBlockInteractStart(world, byPlayer, blockSel, ref handling);
                if (handling == EnumHandling.PreventDefault) return result;
            }

            return CycleAim(world, blockSel.Position);
        }

        private static bool IsHoldingChargeItem(IPlayer byPlayer, BlockBehaviorTemporalCharge charge)
        {
            ItemStack held = byPlayer?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
            if (held?.Collectible?.Code == null) return false;

            // Contains, not StartsWith: this must agree with how TryCharge itself looks the item
            // up, or the click could be swallowed here and then refused there.
            return held.Collectible.Code.Path.Contains(charge.ChargeItemCode);
        }

        private bool CycleAim(IWorldAccessor world, BlockPos pos)
        {
            Block current = world.BlockAccessor.GetBlock(pos);
            string aim = current?.Variant?["aim"];
            if (aim == null) return false;

            int next = (System.Array.IndexOf(AimModes, aim) + 1) % AimModes.Length;

            Block turned = world.GetBlock(current.CodeWithVariant("aim", AimModes[next]));
            if (turned == null) return false;

            world.BlockAccessor.ExchangeBlock(turned.BlockId, pos);
            world.BlockAccessor.MarkBlockDirty(pos);
            return true;
        }

        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);
        }
    }
}
