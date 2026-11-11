using System;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace SignalsLink.YTT.src.taxi
{
    /// <summary>
    /// The ticket's dialog: a switch to wait at the destination, the stations to pick from in
    /// alphabetical order, and a button to go; during a ride two more, to stop here with or
    /// without waiting. Escape or the cross closes it and nothing happens.
    /// </summary>
    public class GuiDialogTrainTicket : GuiDialog
    {
        private readonly Action<TaxiOrderPacket> order;
        private readonly Action<TaxiStopPacket> stop;

        private TaxiStationsPacket data = new TaxiStationsPacket();
        private int selected;
        private bool wait;

        public GuiDialogTrainTicket(ICoreClientAPI capi, Action<TaxiOrderPacket> order, Action<TaxiStopPacket> stop) : base(capi)
        {
            this.order = order;
            this.stop = stop;
        }

        public override string ToggleKeyCombinationCode => null;

        /// <summary>A fresh answer from the server: show it, opening the dialog if need be.</summary>
        public void Show(TaxiStationsPacket packet)
        {
            data = packet ?? new TaxiStationsPacket();
            wait = data.ActiveWait;
            selected = Math.Max(0, Array.IndexOf(data.Hashes, data.ActiveHash));

            Compose();
            if (!IsOpened()) TryOpen();
        }

        private void Compose()
        {
            const int Pad = 12;
            const int Width = 440;
            int top = 40;

            ElementBounds status = ElementBounds.Fixed(Pad, top, Width - 2 * Pad, 44);
            ElementBounds switchBounds = ElementBounds.Fixed(Pad, top + 54, 30, 22);
            ElementBounds switchLabel = ElementBounds.Fixed(Pad + 38, top + 57, Width - 2 * Pad - 38, 24);
            ElementBounds listLabel = ElementBounds.Fixed(Pad, top + 92, Width - 2 * Pad, 24);
            ElementBounds list = ElementBounds.Fixed(Pad, top + 118, Width - 2 * Pad, 30);
            ElementBounds go = ElementBounds.Fixed(Pad, top + 166, 120, 30);
            ElementBounds stopHere = ElementBounds.Fixed(Pad + 130, top + 166, 130, 30);
            ElementBounds stopWait = ElementBounds.Fixed(Pad + 270, top + 166, 146, 30);
            ElementBounds inner = ElementBounds.Fixed(0, 0, Width, top + 210);

            ElementBounds outer = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
            outer.BothSizing = ElementSizing.FitToChildren;
            outer.WithChildren(inner);

            string[] values = new string[Math.Max(1, data.Names.Length)];
            string[] names = new string[values.Length];

            if (data.Names.Length == 0)
            {
                values[0] = "";
                names[0] = Lang.Get("signalslinkytt:ticket-nostations");
            }

            for (int i = 0; i < data.Names.Length; i++)
            {
                values[i] = data.Hashes[i] + ":" + data.Dimensions[i];
                names[i] = data.Names[i];
            }

            GuiComposer composer = capi.Gui.CreateCompo("signalslinkytt-ticket", outer)
                .AddShadedDialogBG(inner)
                .AddDialogTitleBar(Lang.Get("signalslinkytt:ticket-title"), () => TryClose())
                .BeginChildElements(inner)
                    .AddStaticText(StatusText(), CairoFont.WhiteSmallText(), status)
                    .AddSwitch(v => wait = v, switchBounds, "wait")
                    .AddStaticText(Lang.Get("signalslinkytt:ticket-wait"), CairoFont.WhiteSmallText(), switchLabel)
                    .AddStaticText(Lang.Get("signalslinkytt:ticket-pick"), CairoFont.WhiteSmallText(), listLabel)
                    .AddDropDown(values, names, Math.Min(selected, values.Length - 1), (code, _) => selected = Math.Max(0, Array.IndexOf(values, code)), list, "stations")
                    .AddSmallButton(Lang.Get("signalslinkytt:ticket-go"), OnGo, go);

            if (data.HasRide)
            {
                composer
                    .AddSmallButton(Lang.Get("signalslinkytt:ticket-stop"), () => OnStop(false), stopHere)
                    .AddSmallButton(Lang.Get("signalslinkytt:ticket-stopwait"), () => OnStop(true), stopWait);
            }

            SingleComposer = composer.EndChildElements().Compose();
            SingleComposer.GetSwitch("wait").SetValue(wait);
        }

        private string StatusText()
        {
            string reach = data.Reach switch
            {
                1 => Lang.Get("signalslinkytt:ticket-status-pending"),
                2 => Lang.Get("signalslinkytt:ticket-status-unavailable"),
                _ => Lang.Get("signalslinkytt:ticket-status-ready"),
            };

            return data.HasRide ? Lang.Get("signalslinkytt:ticket-status-ride", data.ActiveName) + "\n" + reach : reach;
        }

        private bool OnGo()
        {
            if (data.Names.Length == 0 || selected < 0 || selected >= data.Hashes.Length) return true;

            order?.Invoke(new TaxiOrderPacket
            {
                LocomotiveId = data.LocomotiveId,
                Hash = data.Hashes[selected],
                Dimension = data.Dimensions[selected],
                Wait = wait,
            });

            TryClose();
            return true;
        }

        private bool OnStop(bool andWait)
        {
            stop?.Invoke(new TaxiStopPacket { LocomotiveId = data.LocomotiveId, Wait = andWait });
            TryClose();
            return true;
        }
    }
}
