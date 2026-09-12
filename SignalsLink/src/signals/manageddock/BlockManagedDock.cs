using signals.src.hangingwires;
using SignalsLink.src.signals.paperConditions;
using signals.src.signalNetwork;
using signals.src.transmission;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.manageddock
{
    /// <summary>
    /// The freight dock: a crate with two signal anchors, which loads and unloads whatever is
    /// standing around it.
    ///
    /// <b>It is a BlockConnection</b>, like every other device here with anchors on it. That is what
    /// turns the <c>signalNodes</c> in the block's attributes into selection boxes a wire can be
    /// hooked to - as a plain Block the anchors were drawn and could not be clicked, and the
    /// tooltips landed on the wrong boxes, because the node boxes were never in the list at all.
    /// The node boxes come first, which is what <c>SignalInputsCount</c> counts.
    ///
    /// It is set down turned, the way a crate or a chest is, and it turns its working corner - the
    /// one carrying the anchors - towards whoever put it there. Reaching round the back of a crate
    /// to plug a wire in would be a poor joke.
    /// </summary>
    public class BlockManagedDock : BlockConnection
    {
        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            if (!base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode)) return false;

            if (Turning && world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEManagedDock dock)
            {
                dock.MeshAngle = AngleTowardsPlayer(byPlayer);
                dock.MarkDirty(true);
            }

            return true;
        }

        /// <summary>
        /// TODO: turn this back on once Signals is fixed.
        ///
        /// The crate itself turns fine, and so do its selection boxes - but a wire still attaches
        /// where the anchor would be on an UNTURNED crate, and it is wrong even at a quarter turn.
        /// Signals does not take the anchor position from what
        /// <see cref="GetSelectionBoxes"/> returns; it reads its own from the block's attributes,
        /// which is why turning the boxes here only looks like a fix.
        ///
        /// Two things are needed upstream: <c>GetAnchorPosInBlock(NodePos)</c> has to stop being
        /// sealed, and the node positions have to come from the same place as the boxes. Until
        /// then the crate is set down facing north and cannot be turned - which is ugly, but a
        /// wire that ends in mid-air is worse.
        /// </summary>
        public const bool Turning = false;

        /// <summary>
        /// How far to turn the crate so its working corner - the one carrying the anchors - faces
        /// whoever put it down. Reaching round the back of a crate to plug a wire in is a poor joke.
        ///
        /// Snapped to <see cref="Steps"/> positions round the circle, the way a vanilla crate is:
        /// free enough to stand at an angle on a platform, tidy enough not to look dropped.
        /// </summary>
        public static float AngleTowardsPlayer(IPlayer byPlayer)
        {
            float yaw = byPlayer?.Entity?.Pos.Yaw ?? 0;

            return Snap(yaw);
        }

        /// <summary>Positions round the full circle. 16 is one every 22.5 degrees.</summary>
        public const int Steps = 16;

        public static float Snap(float radians)
        {
            const float circle = GameMath.TWOPI;
            float step = circle / Steps;

            float snapped = (float)Math.Round(radians / step) * step;

            return snapped % circle;
        }

        /// <summary>
        /// The anchors turn with the crate. Signals builds these boxes from the block's attributes,
        /// which knew how to rotate per variant and knows nothing about an angle - so the turning
        /// is done here, after the fact.
        /// </summary>
        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            return Turned(base.GetSelectionBoxes(blockAccessor, pos), blockAccessor, pos);
        }

        public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            return Turned(base.GetCollisionBoxes(blockAccessor, pos), blockAccessor, pos);
        }

        private static Cuboidf[] Turned(Cuboidf[] boxes, IBlockAccessor blockAccessor, BlockPos pos)
        {
            if (!Turning || boxes == null || boxes.Length == 0) return boxes;
            if (blockAccessor.GetBlockEntity(pos) is not BEManagedDock dock || dock.MeshAngle == 0) return boxes;

            float degrees = dock.MeshAngle * GameMath.RAD2DEG;
            Cuboidf[] turned = new Cuboidf[boxes.Length];

            for (int i = 0; i < boxes.Length; i++)
            {
                turned[i] = boxes[i].RotatedCopy(0, degrees, 0, Centre);
            }

            return turned;
        }

        /// <summary>
        /// One point turned about the middle of the block, level.
        ///
        /// Not wired up to where a wire ENDS: <c>BlockConnection.GetAnchorPosInBlock(NodePos)</c> is
        /// sealed in Signals, so hiding it with `new` would compile and never be called. Until that
        /// one word changes upstream, a dock standing at an angle draws its wire to the unturned
        /// spot - the box you click is right, the line is a little off.
        /// </summary>
        public static Vec3f TurnedAround(Vec3f point, float radians)
        {
            float sin = GameMath.Sin(radians);
            float cos = GameMath.Cos(radians);

            float x = point.X - 0.5f;
            float z = point.Z - 0.5f;

            return new Vec3f(0.5f + x * cos - z * sin, point.Y, 0.5f + x * sin + z * cos);
        }

        private static readonly Vec3d Centre = new Vec3d(0.5, 0.5, 0.5);

        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);

            // Somebody laid or broke a block next door - it may well be the paving.
            (world.BlockAccessor.GetBlockEntity(pos) as BEManagedDock)?.OnNeighbourBlockChange(neibpos);
        }

        /// <summary>
        /// A click on the dock can mean three things, and they have to be tried in this order. The
        /// first two are the workaround for the Signals BlockConnection that the sensor and the
        /// igniter carry as well.
        ///
        /// <b>A wire first</b>, when one is being drawn: a click that landed on an anchor was aimed
        /// at the anchor, and it has to be swallowed even when no wire ends up attached. Then the
        /// behaviors, where paper conditions live. The crate last, which is what an empty hand means.
        /// </summary>
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            PlacingWiresMod wires = api.ModLoader.GetModSystem<PlacingWiresMod>();

            if (wires != null)
            {
                NodePos node = GetNodePosForWire(world, blockSel, wires.GetPendingNode());

                if (node != null && CanAttachWire(world, node, wires.GetPendingNode()))
                {
                    wires.ConnectWire(node, byPlayer, this);
                    return false;
                }
            }

            foreach (BlockBehavior behavior in BlockBehaviors)
            {
                EnumHandling handling = EnumHandling.PassThrough;
                bool result = behavior.OnBlockInteractStart(world, byPlayer, blockSel, ref handling);

                if (handling == EnumHandling.PreventDefault) return result;
            }

            // Paper in hand means the click is about the paper even when the behavior declined it
            // - otherwise opening the crate swallows the click.
            if (IsAboutPaper(byPlayer)) return true;

            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEManagedDock dock)
            {
                return dock.OnPlayerRightClick(byPlayer, blockSel);
            }

            return false;
        }

        /// <summary>
        /// Is this click the paper's business? Either the player is holding paper, or they are
        /// holding Ctrl - which is how an item's own description is copied onto the device.
        /// </summary>
        private static bool IsAboutPaper(IPlayer byPlayer)
        {
            ItemSlot slot = byPlayer?.InventoryManager?.ActiveHotbarSlot;
            if (slot?.Itemstack == null) return false;

            return BlockBehaviorPaperConditions.IsPaperStack(slot.Itemstack)
                || byPlayer.Entity?.Controls?.CtrlKey == true;
        }
    }
}
