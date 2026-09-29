using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.chunkanchor
{
    /// <summary>
    /// What an anchor shows when anchors are switched off on the server: the reason, where the
    /// controls would be. The gear slot stays, so a gear left inside can be taken out.
    /// </summary>
    public class GuiDialogAnchorDisabled : GuiDialog
    {
        private const string SlotKey = "gearslot";
        private const double TextWidth = 380;

        private readonly BlockPos pos;
        private readonly IInventory gearSlot;

        public GuiDialogAnchorDisabled(ICoreClientAPI capi, BlockPos pos, string message, IInventory gearSlot)
            : base(capi)
        {
            this.pos = pos;
            this.gearSlot = gearSlot;

            Compose(message);
        }

        public override string ToggleKeyCombinationCode => null;

        private void Compose(string message)
        {
            CairoFont font = CairoFont.WhiteSmallText();

            // Measured, because the two reasons are not the same length in any language.
            double textHeight = capi.Gui.Text.GetMultilineTextHeight(font, message, TextWidth) / RuntimeEnv.GUIScale + 6;

            ElementBounds text = ElementBounds.Fixed(0, 30, TextWidth, textHeight);
            ElementBounds slotLabel = ElementBounds.Fixed(0, 30 + textHeight + 18, 200, 20);
            ElementBounds slot = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 30 + textHeight + 40, 1, 1);

            ElementBounds inner = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            inner.BothSizing = ElementSizing.FitToChildren;
            inner.WithChildren(text, slotLabel, slot);

            ElementBounds outer = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            SingleComposer = capi.Gui
                .CreateCompo("signalslink-chunkanchor-disabled-" + pos, outer)
                .AddShadedDialogBG(inner)
                .AddDialogTitleBar(Lang.Get("signalslink:chunkanchor-dialogtitle"), () => TryClose())
                .BeginChildElements(inner)
                    .AddStaticText(message, font, text)
                    .AddStaticText(Lang.Get("signalslink:chunkanchor-slotlabel"), font, slotLabel)
                    .AddItemSlotGrid(gearSlot, SendSlotPacket, 1, slot, SlotKey)
                .EndChildElements()
                .Compose();
        }

        private void SendSlotPacket(object packet)
        {
            capi.Network.SendBlockEntityPacket(pos.X, pos.Y, pos.Z, packet);
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();

            // Hand the slot back, the way any container dialog does.
            SingleComposer.GetSlotGrid(SlotKey)?.OnGuiClosed(capi);
            capi.Network.SendBlockEntityPacket(pos, (int)EnumBlockEntityPacketId.Close);
        }
    }
}
