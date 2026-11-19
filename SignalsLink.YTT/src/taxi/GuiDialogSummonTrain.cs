using System;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace SignalsLink.YTT.src.taxi
{
    /// <summary>
    /// The ticket used off a train: "Call a train to station X?", a switch to have it wait for
    /// the caller, and a button. During a summon the button calls it off instead. A train waiting
    /// for this player somewhere can be let go from here, with or without a station near.
    /// </summary>
    public class GuiDialogSummonTrain : GuiDialog
    {
        private readonly Action<TaxiSummonPacket> send;
        private readonly Action<TaxiReleasePacket> release;
        private TaxiSummonOfferPacket offer = new TaxiSummonOfferPacket();
        private bool waitForMe;

        public GuiDialogSummonTrain(ICoreClientAPI capi, Action<TaxiSummonPacket> send, Action<TaxiReleasePacket> release) : base(capi)
        {
            this.send = send;
            this.release = release;
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
            bool held = offer.HeldLocomotiveId != 0;
            ElementBounds heldText = ElementBounds.Fixed(Pad, top + 144, Width - 2 * Pad, 26);
            ElementBounds heldButton = ElementBounds.Fixed(Pad, top + 174, 180, 30);
            ElementBounds inner = ElementBounds.Fixed(0, 0, Width, held ? top + 218 : top + 144);

            ElementBounds outer = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
            outer.BothSizing = ElementSizing.FitToChildren;
            outer.WithChildren(inner);

            bool station = !string.IsNullOrEmpty(offer.StationName);
            string text = !station ? Lang.Get("signalslinkytt:ticket-error-nostationnear")
                : offer.HasSummon ? Lang.Get("signalslinkytt:summon-running", offer.StationName)
                : Lang.Get("signalslinkytt:summon-question", offer.StationName);

            GuiComposer composer = capi.Gui.CreateCompo("signalslinkytt-summon", outer)
                .AddShadedDialogBG(inner)
                .AddDialogTitleBar(Lang.Get("signalslinkytt:summon-title"), () => TryClose())
                .BeginChildElements(inner)
                    .AddStaticText(text, CairoFont.WhiteSmallText(), question);

            if (!station)
            {
                // Nothing to call a train to; only the held train below.
            }
            else if (offer.HasSummon)
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

            if (held)
            {
                string where = string.IsNullOrEmpty(offer.HeldName)
                    ? Lang.Get("signalslinkytt:summon-held-nowhere")
                    : Lang.Get("signalslinkytt:summon-held", offer.HeldName);

                composer
                    .AddStaticText(where, CairoFont.WhiteSmallText(), heldText)
                    .AddSmallButton(Lang.Get("signalslinkytt:ticket-release"), OnRelease, heldButton);
            }

            SingleComposer = composer.EndChildElements().Compose();
        }

        private bool OnRelease()
        {
            release?.Invoke(new TaxiReleasePacket { LocomotiveId = offer.HeldLocomotiveId });
            TryClose();
            return true;
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
