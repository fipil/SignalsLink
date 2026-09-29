using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace SignalsLink.src.signals.behaviours
{
    /// <summary>
    /// Lets a device be placed onto a block that grabs every right-click for itself, sneak or
    /// not: a crate takes the item in hand (or nothing happens), and the device never lands.
    /// The engine asks a sneaking player's held item first when it carries
    /// <see cref="CollectibleObject.HeldPriorityInteract"/>, so with sneak we place ourselves
    /// where the ordinary build step would have. Blocks that do not claim the click keep the
    /// engine's own placement.
    /// </summary>
    public class BlockBehaviorPlaceOnBusyBlock : BlockBehavior
    {
        public BlockBehaviorPlaceOnBusyBlock(Block block) : base(block) { }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            block.HeldPriorityInteract = true;
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling, ref EnumHandling handling)
        {
            if (blockSel == null || byEntity is not EntityPlayer entityPlayer || !entityPlayer.Controls.ShiftKey) return;

            IWorldAccessor world = byEntity.World;
            Block target = world.BlockAccessor.GetBlock(blockSel.Position);
            if (target?.PlacedPriorityInteract != true) return;   // the build step will get its turn

            handling = EnumHandling.PreventDefault;
            handHandling = EnumHandHandling.PreventDefaultAction;
            if (world.Side != EnumAppSide.Server) return;         // the server places, the client only stops the crate

            IPlayer player = world.PlayerByUid(entityPlayer.PlayerUID);
            BlockSelection at = blockSel.Clone();
            if (!target.IsReplacableBy(block))
            {
                at.Position = at.Position.AddCopy(at.Face);
                at.DidOffset = true;
            }

            string failureCode = null;
            if (!world.Claims.TryAccess(player, at.Position, EnumBlockAccessFlags.BuildOrBreak)) failureCode = "claimed";
            else if (!block.TryPlaceBlock(world, player, slot.Itemstack, at, ref failureCode)) failureCode ??= "generic";

            if (failureCode != null)
            {
                (player as IServerPlayer)?.SendIngameError(failureCode, Lang.Get("placefailure-" + failureCode));
                return;
            }

            if (player.WorldData.CurrentGameMode != EnumGameMode.Creative)
            {
                slot.TakeOut(1);
                slot.MarkDirty();
            }

            if (block.Sounds?.Place != null) world.PlaySoundAt(block.Sounds.Place, at.Position, 0);
        }
    }
}
