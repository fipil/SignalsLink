using System;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace SignalsLink.YTT.src.taxi
{
    /// <summary>
    /// The ticket used off a train: "Call a train to station X?", a switch to have it wait for
    /// the caller however long, and a button. During a summon the button calls it off instead.
    /// </summary>
    public class GuiDialogSummonTrain : GuiDialog
    {
        private readonly Action<TaxiSummonPacket> send;
        private TaxiSummonOfferPacket offer = new TaxiSummonOfferPacket();
        private bool waitForMe;

        public GuiDialogSummonTrain(ICoreClientAPI capi, Action<TaxiSummonPacket> send) : base(capi)
        {
            this.send = send;
        }

        public override string ToggleKeyCombinationCode => null;

        public void Show(TaxiSummonOfferPacket packet)
        {
            offer = packet ?? new TaxiSummonOfferPacket();
            waitForMe = false;

            Compose();
            if (!IsOpened()) TryOpen();
        }

        private void Compose()
        {
            const int Pad = 12;
            const int Width = 420;
            int top = 40;

            ElementBounds question = ElementBounds.Fixed(Pad, top, Width - 2 * Pad, 48);
            ElementBounds switchBounds = ElementBounds.Fixed(Pad, top + 58, 30, 22);
            ElementBounds switchLabel = ElementBounds.Fixed(Pad + 38, top + 61, Width - 2 * Pad - 38, 24);
            ElementBounds button = ElementBounds.Fixed(Pad, top + 100, 180, 30);
            ElementBounds inner = ElementBounds.Fixed(0, 0, Width, top + 144);

            ElementBounds outer = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
            outer.BothSizing = ElementSizing.FitToChildren;
            outer.WithChildren(inner);

            string text = offer.HasSummon
                ? Lang.Get("signalslinkytt:summon-running", offer.StationName)
                : Lang.Get("signalslinkytt:summon-question", offer.StationName);

            GuiComposer composer = capi.Gui.CreateCompo("signalslinkytt-summon", outer)
                .AddShadedDialogBG(inner)
                .AddDialogTitleBar(Lang.Get("signalslinkytt:summon-title"), () => TryClose())
                .BeginChildElements(inner)
                    .AddStaticText(text, CairoFont.WhiteSmallText(), question);

            if (offer.HasSummon)
            {
                composer.AddSmallButton(Lang.Get("signalslinkytt:summon-cancel"), () => OnSend(true), button);
            }
            else
            {
                composer
                    .AddSwitch(v => waitForMe = v, switchBounds, "waitforme")
                    .AddStaticText(Lang.Get("signalslinkytt:summon-waitforme"), CairoFont.WhiteSmallText(), switchLabel)
                    .AddSmallButton(Lang.Get("signalslinkytt:summon-button"), () => OnSend(false), button);
            }

            SingleComposer = composer.EndChildElements().Compose();
        }

        private bool OnSend(bool cancel)
        {
            send?.Invoke(new TaxiSummonPacket
            {
                Hash = offer.Hash,
                Dimension = offer.Dimension,
                Gauge = offer.Gauge,
                WaitForMe = waitForMe,
                Cancel = cancel,
            });

            TryClose();
            return true;
        }
    }
}
