using SignalsTubes.src.programtube;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.API.Client;

namespace SignalsMachines.src.craftingmachine
{
    /// <summary>
    /// The lower controller renders the complete machine. The vanilla Multiblock behavior
    /// manages its upper proxy, including placement, breaking and creative picking.
    /// </summary>
    public class BlockCraftingMachine : Block, IMultiBlockColSelBoxes, IMultiBlockInteract
    {
        private Cuboidf[] upperPartBoxes;
        private Cuboidf[] emptySocketBoxes;
        private Cuboidf[] occupiedSocketBoxes;
        public float RotationRadians => RotationDegrees * GameMath.DEG2RAD;
        private int RotationDegrees => Variant["side"] switch { "east" => 270, "south" => 180, "west" => 90, _ => 0 };

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
                new Cuboidf(3/16f,10.9f/16,3/16f,13/16f,13.5f/16,13/16f),
                new Cuboidf(0,13.5f/16,0,1,1,1)
            });
            occupiedSocketBoxes = emptySocketBoxes.Select(b => b.Clone()).ToArray();
            occupiedSocketBoxes[0] = Rotate(new[] { new Cuboidf(5/16f,1/16f,9/16f,11/16f,8.5f/16,15/16f) })[0];
        }

        private Cuboidf[] Rotate(Cuboidf[] boxes) => boxes.Select(b => b.RotatedCopy(0, RotationDegrees, 0, new Vec3d(.5,.5,.5))).ToArray();

        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor accessor, BlockPos pos) =>
            accessor.GetBlockEntity(pos) is BECraftingMachine be && be.HasTube ? occupiedSocketBoxes : emptySocketBoxes;

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

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer player, BlockSelection selection)
        {
            if (selection.SelectionBoxIndex != 0)
                return base.OnBlockInteractStart(world, player, selection);
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
            return be.Interact(player);
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer player)
        {
            if (selection.SelectionBoxIndex != 0)
                return base.GetPlacedBlockInteractionHelp(world, selection, player);
            var be = world.BlockAccessor.GetBlockEntity(selection.Position) as BECraftingMachine;
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
