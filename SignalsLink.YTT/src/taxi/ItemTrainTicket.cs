using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace SignalsLink.YTT.src.taxi
{
    /// <summary>
    /// The train ticket. Used in a train it asks the server what this train can do; the server
    /// decides, the dialog only shows the answer. It is never used up.
    /// </summary>
    public class ItemTrainTicket : Item
    {
        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel,
            bool firstEvent, ref EnumHandHandling handling)
        {
            if (!firstEvent) return;

            handling = EnumHandHandling.PreventDefault;

            if (api is ICoreClientAPI && byEntity is EntityPlayer)
            {
                api.ModLoader.GetModSystem<SignalsLinkYttMod>()?.RequestTaxi();
            }
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return new[]
            {
                new WorldInteraction { ActionLangCode = "signalslinkytt:ticket-help", MouseButton = EnumMouseButton.Right },
            };
        }
    }
}
