using System;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace SignalsLink.YTT.src.taxi
{
    /// <summary>
    /// The ticket's dialog: a switch to wait at the destination, the stations as a scrolling list
    /// in alphabetical order, and a button to go; during a ride two more, to stop here with or
    /// without waiting. Escape or the cross closes it and nothing happens.
    /// </summary>
    public class GuiDialogTrainTicket : GuiDialog
    {
        private const int Pad = 12;
        private const int Width = 460;
        private const int RowHeight = 28;
        private const int VisibleRows = 10;
        private const int ScrollbarWidth = 20;

        private readonly Action<TaxiOrderPacket> order;
        private readonly Action<TaxiStopPacket> stop;

        private TaxiStationsPacket data = new TaxiStationsPacket();
        private int selected = -1;
        private bool wait;
        private ElementBounds rows;

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
            selected = data.HasRide ? Array.IndexOf(data.Hashes, data.ActiveHash) : -1;
            scroll = -1;

            Compose();
            if (!IsOpened()) TryOpen();
        }

        private void Compose()
        {
            int top = 40;
            int listWidth = Width - 2 * Pad - ScrollbarWidth - 4;
            int listHeight = RowHeight * VisibleRows;
            int count = Math.Max(1, data.Names.Length);
            int contentHeight = Math.Max(listHeight, RowHeight * count);

            ElementBounds status = ElementBounds.Fixed(Pad, top, Width - 2 * Pad, 44);
            ElementBounds switchBounds = ElementBounds.Fixed(Pad, top + 54, 30, 22);
            ElementBounds switchLabel = ElementBounds.Fixed(Pad + 38, top + 57, Width - 2 * Pad - 38, 24);
            ElementBounds listLabel = ElementBounds.Fixed(Pad, top + 92, Width - 2 * Pad, 24);
            ElementBounds clip = ElementBounds.Fixed(Pad, top + 118, listWidth, listHeight);
            ElementBounds scrollbar = ElementBounds.Fixed(Pad + listWidth + 4, top + 118, ScrollbarWidth, listHeight);
            int buttons = top + 118 + listHeight + 16;
            ElementBounds go = ElementBounds.Fixed(Pad, buttons, 120, 30);
            ElementBounds stopHere = ElementBounds.Fixed(Pad + 130, buttons, 130, 30);
            ElementBounds stopWait = ElementBounds.Fixed(Pad + 270, buttons, 166, 30);
            ElementBounds inner = ElementBounds.Fixed(0, 0, Width, buttons + 44);

            ElementBounds outer = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
            outer.BothSizing = ElementSizing.FitToChildren;
            outer.WithChildren(inner);

            rows = ElementBounds.Fixed(0, 0, listWidth, contentHeight);

            GuiComposer composer = capi.Gui.CreateCompo("signalslinkytt-ticket", outer)
                .AddShadedDialogBG(inner)
                .AddDialogTitleBar(Lang.Get("signalslinkytt:ticket-title"), () => TryClose())
                .BeginChildElements(inner)
                    .AddStaticText(StatusText(), CairoFont.WhiteSmallText(), status)
                    .AddSwitch(v => wait = v, switchBounds, "wait")
                    .AddStaticText(Lang.Get("signalslinkytt:ticket-wait"), CairoFont.WhiteSmallText(), switchLabel)
                    .AddStaticText(Lang.Get("signalslinkytt:ticket-pick"), CairoFont.WhiteSmallText(), listLabel)
                    .AddInset(clip, 3)
                    .BeginClip(clip)
                        .BeginChildElements(rows);

            if (data.Names.Length == 0)
            {
                composer.AddStaticText(Lang.Get("signalslinkytt:ticket-nostations"), CairoFont.WhiteSmallText(),
                    ElementBounds.Fixed(6, 4, listWidth - 12, RowHeight));
            }

            // A plain list, not a column of buttons: left-aligned text, a stripe behind the chosen
            // row, and an invisible button over each row to take the click.
            CairoFont plain = CairoFont.WhiteSmallText();

            for (int i = 0; i < data.Names.Length; i++)
            {
                int index = i;
                ElementBounds row = ElementBounds.Fixed(0, i * RowHeight, listWidth, RowHeight);

                if (i == selected) composer.AddRowHighlight(ElementBounds.Fixed(0, i * RowHeight, listWidth, RowHeight));

                composer
                    .AddStaticText(data.Names[i], plain, EnumTextOrientation.Left,
                        ElementBounds.Fixed(8, i * RowHeight + 5, listWidth - 16, RowHeight))
                    .AddSmallButton("", () => Select(index), row, EnumButtonStyle.None, EnumTextOrientation.Left, "row" + i);
            }

            composer
                        .EndChildElements()
                    .EndClip()
                    .AddVerticalScrollbar(OnScroll, scrollbar, "scrollbar")
                    .AddSmallButton(Lang.Get("signalslinkytt:ticket-go"), OnGo, go);

            if (data.HasRide)
            {
                composer
                    .AddSmallButton(Lang.Get("signalslinkytt:ticket-stop"), () => OnStop(false), stopHere)
                    .AddSmallButton(Lang.Get("signalslinkytt:ticket-stopwait"), () => OnStop(true), stopWait);
            }

            SingleComposer = composer.EndChildElements().Compose();

            SingleComposer.GetSwitch("wait").SetValue(wait);
            SingleComposer.GetScrollbar("scrollbar").SetHeights(listHeight, contentHeight);

            // Back where the list was, or to the current destination when it would be out of view.
            float scrollTo = scroll;
            if (scrollTo < 0 && selected > VisibleRows - 1) scrollTo = (selected - VisibleRows / 2) * RowHeight;

            if (scrollTo > 0)
            {
                float value = Math.Min(Math.Max(0, contentHeight - listHeight), scrollTo);
                SingleComposer.GetScrollbar("scrollbar").CurrentYPosition = value;
                OnScroll(value);
            }
        }

        /// <summary>Negative until the list has been scrolled: then the first compose picks the place.</summary>
        private float scroll = -1;

        /// <summary>A click on a row: remember it and redraw the list with that row highlighted.</summary>
        private bool Select(int index)
        {
            selected = index;
            scroll = SingleComposer?.GetScrollbar("scrollbar")?.CurrentYPosition ?? 0;
            Compose();
            return true;
        }

        private void OnScroll(float value)
        {
            if (rows == null) return;

            scroll = value;
            rows.fixedY = -value;
            rows.CalcWorldBounds();
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
            if (selected < 0 || selected >= data.Hashes.Length) return true;

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
