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

            if (byPlayer?.Entity != null && world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEManagedDock dock)
            {
                dock.MeshAngle = AngleTowards(
                    byPlayer.Entity.Pos.X - (blockSel.Position.X + 0.5),
                    byPlayer.Entity.Pos.Z - (blockSel.Position.Z + 0.5));
                dock.MarkDirty(true);
            }

            return true;
        }

        /// <summary>
        /// How far to turn the crate so its working face - north as the shape is drawn, the one
        /// carrying the anchors - looks at somebody standing (dx, dz) from its middle.
        ///
        /// From where they stand rather than which way they look, as the vanilla chest does it.
        /// Snapped to <see cref="Steps"/> positions: free enough to stand at an angle on a
        /// platform, tidy enough not to look dropped.
        /// </summary>
        public static float AngleTowards(double dx, double dz)
        {
            return Snap((float)Math.Atan2(dx, dz) + GameMath.PI);
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
            Cuboidf[] boxes = base.GetSelectionBoxes(blockAccessor, pos);

            if (boxes == null || boxes.Length == 0) return boxes;
            if (blockAccessor.GetBlockEntity(pos) is not BEManagedDock dock || dock.MeshAngle == 0) return boxes;

            return TurnedAnchors(boxes, wireAnchors?.Length ?? 0, dock.MeshAngle);
        }

        /// <summary>
        /// Only the anchors turn; they come first in the list. The body stays as the blocktype
        /// says, and so does collision: turned at an odd angle the body box swells out of the
        /// block, and the game then lets you walk through it.
        /// </summary>
        public static Cuboidf[] TurnedAnchors(Cuboidf[] boxes, int anchors, float radians)
        {
            Cuboidf[] turned = new Cuboidf[boxes.Length];

            for (int i = 0; i < boxes.Length; i++)
            {
                turned[i] = i < anchors ? TurnedBox(boxes[i], radians) : boxes[i];
            }

            return turned;
        }

        /// <summary>One box turned about the middle of the block, level - the way the mesh is.</summary>
        public static Cuboidf TurnedBox(Cuboidf box, float radians)
        {
            return box.RotatedCopy(0, radians * GameMath.RAD2DEG, 0, Centre);
        }

        /// <summary>
        /// Where a wire ends. Signals asks the block, which knows nothing of an angle, so the
        /// crate standing there is asked for it. Needs a Signals where this is virtual: the
        /// released 0.3.1 is not, and this class will not even load against it.
        /// </summary>
        public override Vec3f GetAnchorPosInBlock(NodePos pos)
        {
            Vec3f unturned = base.GetAnchorPosInBlock(pos);

            if (pos?.blockPos == null) return unturned;
            if (api?.World?.BlockAccessor.GetBlockEntity(pos.blockPos) is not BEManagedDock dock || dock.MeshAngle == 0) return unturned;

            return TurnedAround(unturned, dock.MeshAngle);
        }

        /// <summary>
        /// One point turned exactly as <see cref="TurnedBox"/> turns a box, because it IS one: the
        /// end of the wire and the box that was clicked must not be able to part company.
        /// </summary>
        public static Vec3f TurnedAround(Vec3f point, float radians)
        {
            Cuboidf turned = TurnedBox(new Cuboidf(point.X, point.Y, point.Z, point.X, point.Y, point.Z), radians);

            return new Vec3f(turned.MidX, turned.MidY, turned.MidZ);
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
