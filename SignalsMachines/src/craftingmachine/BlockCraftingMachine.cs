using signals.src.signalNetwork;
using SignalsTubes.src.programtube;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;
using Vintagestory.API.Client;

namespace SignalsMachines.src.craftingmachine
{
    /// <summary>
    /// The lower controller renders the complete machine. The vanilla Multiblock behavior
    /// manages its upper proxy, including placement, breaking and creative picking.
    /// Selection boxes: 0-7 the Signals pins (anchors from the blocktype), 8 the tube socket, then the body.
    /// </summary>
    public class BlockCraftingMachine : BlockConnection, IMultiBlockColSelBoxes, IMultiBlockInteract, IMechanicalPowerBlock
    {
        public const int PinCount = BECraftingMachine.PinCount;
        public const int SocketBox = PinCount;
        private Cuboidf[] upperPartBoxes;
        private Cuboidf[] emptySocketBoxes;
        private Cuboidf[] occupiedSocketBoxes;
        public float RotationRadians => RotationDegrees * GameMath.DEG2RAD;
        public int RotationDegrees => Variant["side"] switch { "east" => 270, "south" => 180, "west" => 90, _ => 0 };

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            // These boxes use coordinates local to the upper proxy, not the controller.
            // Rotate the terminal area together with the complete model.
            upperPartBoxes = Attributes["upperPartBoxes"].AsObject<Cuboidf[]>();
            upperPartBoxes = Rotate(upperPartBoxes);
            emptySocketBoxes = Rotate(new[] {
                new Cuboidf(5/16f,1/16f,9/16f,11/16f,2.1f/16,15/16f),
                new Cuboidf(0,0,0,1,1/16f,1),
                new Cuboidf(5/16f,1/16f,3.2f/16,11/16f,13.25f/16,8/16f),
                new Cuboidf(3/16f,1/16f,1.9f/16,13/16f,11.5f/16,4.2f/16),
                new Cuboidf(0,1/16f,1.5f/16,3/16f,12/16f,6.5f/16),
                new Cuboidf(0,1/16f,9.5f/16,3/16f,12/16f,14.5f/16),
                new Cuboidf(13/16f,1/16f,1.5f/16,1,12/16f,6.5f/16),
                new Cuboidf(13/16f,1/16f,9.5f/16,1,12/16f,14.5f/16),
                new Cuboidf(3/16f,10.9f/16,3/16f,13/16f,12.9f/16,13/16f),
                new Cuboidf(3/16f,12.9f/16,5/16f,13/16f,1,11/16f)
            });
            occupiedSocketBoxes = emptySocketBoxes.Select(b => b.Clone()).ToArray();
            occupiedSocketBoxes[0] = Rotate(new[] { new Cuboidf(5/16f,1/16f,9/16f,11/16f,8.5f/16,15/16f) })[0];
        }

        private Cuboidf[] Rotate(Cuboidf[] boxes) => boxes.Select(b => b.RotatedCopy(0, RotationDegrees, 0, new Vec3d(.5,.5,.5))).ToArray();

        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor accessor, BlockPos pos)
        {
            var anchors = base.GetSelectionBoxes(accessor, pos).Take(PinCount);
            var body = accessor.GetBlockEntity(pos) is BECraftingMachine be && be.HasTube ? occupiedSocketBoxes : emptySocketBoxes;
            return anchors.Concat(body).ToArray();
        }

        public override bool DoPartialSelection(IWorldAccessor world, BlockPos pos) => true;

        // Upper proxy selection indices describe the chamber, never the socket below it.
        public bool MBDoPartialSelection(IWorldAccessor world, BlockPos pos, Vec3i offset) => false;
        public bool MBOnBlockInteractStart(IWorldAccessor world, IPlayer player, BlockSelection selection, Vec3i offset) => false;
        public bool MBOnBlockInteractStep(float seconds, IWorldAccessor world, IPlayer player, BlockSelection selection, Vec3i offset) => false;
        public void MBOnBlockInteractStop(float seconds, IWorldAccessor world, IPlayer player, BlockSelection selection, Vec3i offset) { }
        public bool MBOnBlockInteractCancel(float seconds, IWorldAccessor world, IPlayer player, BlockSelection selection, EnumItemUseCancelReason reason, Vec3i offset) => true;
        public ItemStack MBOnPickBlock(IWorldAccessor world, BlockPos pos, Vec3i offset) => OnPickBlock(world, pos);
        public WorldInteraction[] MBGetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer player, Vec3i offset) => Array.Empty<WorldInteraction>();
        public BlockSounds MBGetSounds(IBlockAccessor accessor, BlockSelection selection, ItemStack stack, Vec3i offset) => Sounds;

        // ---- mechanical power: the input axle on the model's north face, turned with the block

        /// <summary>World side the input axle sticks out of.</summary>
        public BlockFacing AxleFacing => SideOf(new Cuboidf(7/16f, .5f, 0, 9/16f, .55f, 1/16f));

        private BlockFacing SideOf(Cuboidf modelBox)
        {
            var box = modelBox.RotatedCopy(0, RotationDegrees, 0, new Vec3d(.5, .5, .5));
            double dx = (box.X1 + box.X2) / 2 - .5, dz = (box.Z1 + box.Z2) / 2 - .5;
            return Math.Abs(dx) > Math.Abs(dz) ? (dx < 0 ? BlockFacing.WEST : BlockFacing.EAST) : (dz < 0 ? BlockFacing.NORTH : BlockFacing.SOUTH);
        }

        public bool HasMechPowerConnectorAt(IWorldAccessor world, BlockPos pos, BlockFacing face, BlockMPBase forBlock) => face == AxleFacing;
        public void DidConnectAt(IWorldAccessor world, BlockPos pos, BlockFacing face) { }
        public MechanicalNetwork GetNetwork(IWorldAccessor world, BlockPos pos) =>
            world.BlockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorMPBase>()?.Network;

        public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
        {
            bool placed = base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack);
            // an axle already waiting at the input: join its network
            if (placed && world.Side == EnumAppSide.Server && world.BlockAccessor.GetBlockEntity(blockSel.Position) is BECraftingMachine be)
            {
                be.SetPlacer(byPlayer);
                be.GetBehavior<BEBehaviorMPBase>()?.tryConnect(AxleFacing);
            }
            return placed;
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer player, BlockSelection selection)
        {
            if (selection.SelectionBoxIndex != SocketBox)
                return base.OnBlockInteractStart(world, player, selection);   // pins: wire placing
            if (!world.Claims.TryAccess(player, selection.Position, EnumBlockAccessFlags.Use)) return false;
            var be = world.BlockAccessor.GetBlockEntity(selection.Position) as BECraftingMachine;
            // Machines placed before the socket update have no block entity in their saved chunk.
            // Let the client initiate use; only the server creates the missing controller.
            if (be == null)
            {
                if (player.InventoryManager.ActiveHotbarSlot.Itemstack?.Collectible is not ItemProgramTube) return false;
                if (world.Side == EnumAppSide.Client) return true;
                world.BlockAccessor.SpawnBlockEntity(EntityClass, selection.Position);
                be = world.BlockAccessor.GetBlockEntity(selection.Position) as BECraftingMachine;
                if (be == null) return false;
            }
            // The imprinter's plug: in while the socket is empty, out again with an empty hand (as in the socket block).
            var hand = player.InventoryManager.ActiveHotbarSlot;
            if (SignalsTubes.src.imprint.ItemImprinterTool.IsPlug(hand.Itemstack))
            {
                if (be.HasTube) return false;
                var imprinterPos = SignalsTubes.src.imprint.ItemImprinterTool.ImprinterOf(hand.Itemstack);
                if (world.Side == EnumAppSide.Server && world.BlockAccessor.GetBlockEntity(imprinterPos) is SignalsTubes.src.imprint.BEImprinter imprinter)
                    imprinter.LinkTo((Vintagestory.API.Server.IServerPlayer)player, selection.Position);
                return true;
            }
            if (be.Imprinter != null)
            {
                if (!hand.Empty) return false;
                if (world.Side == EnumAppSide.Server && world.BlockAccessor.GetBlockEntity(be.Imprinter) is SignalsTubes.src.imprint.BEImprinter imprinter)
                    imprinter.UnplugToHand((Vintagestory.API.Server.IServerPlayer)player);
                return true;
            }
            return be.Interact(player);
        }

        /// <summary>Block light while the crystal is down: temporal teal (VS hue 0-63, saturation 0-7, brightness 0-31),
        /// as far as an oil lamp (brightness 11) reaches.</summary>
        public static readonly byte[] CrystalLight = { 27, 7, 11 };

        public override byte[] GetLightHsv(IBlockAccessor blockAccessor, BlockPos pos, ItemStack stack = null)
        {
            if (pos != null && blockAccessor.GetBlockEntity(pos) is BECraftingMachine { Lit: true }) return CrystalLight;
            return base.GetLightHsv(blockAccessor, pos, stack);
        }

        // Aiming at a pin: just that pin and its current level.
        public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
        {
            var sel = forPlayer?.Entity?.BlockSelection;
            string anchor = sel == null ? null : GetAnchorName(world, sel);
            if (anchor == null || sel.SelectionBoxIndex >= PinCount) return base.GetPlacedBlockInfo(world, pos, forPlayer);
            // The upper proxy reuses index 0 for the chamber; its selection must not read as the state pin.
            if (!sel.Position.Equals(pos))
                return base.GetPlacedBlockInfo(world, pos, forPlayer).Replace(Vintagestory.API.Config.Lang.Get("signals:" + anchor) + "\r\n", "");
            int pin = sel.SelectionBoxIndex;
            var be = world.BlockAccessor.GetBlockEntity(pos) as BECraftingMachine;
            string info = PinName(pin, anchor) + "\n";
            if (be != null && pin <= BECraftingMachine.LastInput)
                info += Vintagestory.API.Config.Lang.Get("signalsmachines:machine-pin-level", pin == BECraftingMachine.StatePin ? be.State : be.Inputs[pin]) + "\n";
            return info;
        }

        /// <summary>Door pins are named by the side the door faces in the world, the rest by their anchor name.</summary>
        public string PinName(int pin, string anchor)
        {
            if (pin != 4 && pin != 5) return Vintagestory.API.Config.Lang.Get("signals:" + anchor);
            return Vintagestory.API.Config.Lang.Get("signalsmachines:pin-door", Vintagestory.API.Config.Lang.Get("signalsmachines:side-" + DoorSide(pin).Code));
        }

        /// <summary>World side of a door: west/east in the model, turned with the block.</summary>
        public BlockFacing DoorSide(int pin) => SideOf(new Cuboidf(pin == 4 ? 0 : 15/16f, .5f, 7/16f, pin == 4 ? 1/16f : 1, .55f, 9/16f));

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer player)
        {
            if (selection.SelectionBoxIndex != SocketBox)
                return base.GetPlacedBlockInteractionHelp(world, selection, player);
            var be = world.BlockAccessor.GetBlockEntity(selection.Position) as BECraftingMachine;
            if (SignalsTubes.src.imprint.ItemImprinterTool.IsPlug(player.InventoryManager.ActiveHotbarSlot.Itemstack))
                return new[] { new WorldInteraction { ActionLangCode = "signalstubes:socket-plug", MouseButton = EnumMouseButton.Right } };
            if (be?.Imprinter != null)
                return new[] { new WorldInteraction { ActionLangCode = "signalstubes:socket-unplug", MouseButton = EnumMouseButton.Right } };
            bool hasTube = be?.HasTube == true;
            var tube = world.GetItem(new AssetLocation("signalstubes:programtube-fire"));
            return new[] { new WorldInteraction {
                ActionLangCode = hasTube ? "signalsmachines:socket-remove" : "signalsmachines:socket-insert",
                MouseButton = EnumMouseButton.Right,
                Itemstacks = hasTube || tube == null ? null : new[] { new ItemStack(tube) }
            } };
        }

        public Cuboidf[] MBGetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset)
        {
            return upperPartBoxes;
        }

        public Cuboidf[] MBGetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset)
        {
            return upperPartBoxes;
        }
    }
}
