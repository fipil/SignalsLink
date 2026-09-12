using System;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace SignalsLink.src.signals.paperConditions
{
    /// <summary>
    /// Both sets of orders side by side, so the player can see what they are about to lose before
    /// they lose it.
    /// </summary>
    public class GuiDialogConfirmPaper : GuiDialog
    {
        private readonly string current;
        private readonly string wanted;
        private readonly Action onConfirm;

        public GuiDialogConfirmPaper(ICoreClientAPI capi, string current, string wanted, Action onConfirm)
            : base(capi)
        {
            this.current = current ?? "";
            this.wanted = wanted;
            this.onConfirm = onConfirm;

            Compose();
        }

        public override string ToggleKeyCombinationCode => null;

        /// <summary>Clearing shows one column and asks a different question.</summary>
        private bool Clearing => string.IsNullOrWhiteSpace(wanted);

        private const double ColumnWidth = 250;
        private const double TextHeight = 300;
        private const double ScrollbarWidth = 20;

        private void Compose()
        {
            CairoFont font = CairoFont.WhiteSmallText();

            double rightColumn = ColumnWidth + ScrollbarWidth + 25;

            ElementBounds leftLabel = ElementBounds.Fixed(0, 30, ColumnWidth, 20);
            ElementBounds leftClip = ElementBounds.Fixed(0, 54, ColumnWidth, TextHeight);
            ElementBounds leftText = leftClip.ForkContainingChild(0, 0, 0, -3);
            ElementBounds leftBar = leftClip.CopyOffsetedSibling(ColumnWidth + 3, 0).WithFixedWidth(ScrollbarWidth);

            ElementBounds rightLabel = ElementBounds.Fixed(rightColumn, 30, ColumnWidth, 20);
            ElementBounds rightClip = ElementBounds.Fixed(rightColumn, 54, ColumnWidth, TextHeight);
            ElementBounds rightText = rightClip.ForkContainingChild(0, 0, 0, -3);
            ElementBounds rightBar = rightClip.CopyOffsetedSibling(ColumnWidth + 3, 0).WithFixedWidth(ScrollbarWidth);

            double buttonRow = 54 + TextHeight + 15;
            ElementBounds cancel = ElementBounds.Fixed(0, buttonRow, 130, 24);
            ElementBounds confirm = ElementBounds.Fixed(145, buttonRow, 200, 24);

            ElementBounds body = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            body.BothSizing = ElementSizing.FitToChildren;
            body.WithChildren(leftLabel, leftClip, leftBar, rightLabel, rightClip, rightBar, cancel, confirm);

            GuiComposer composer = capi.Gui
                .CreateCompo("signalslink-confirmpaper", ElementStdBounds.AutosizedMainDialog)
                .AddShadedDialogBG(body)
                .AddDialogTitleBar(Lang.Get(Clearing
                    ? "signalslink:confirmpaper-clear-title"
                    : "signalslink:confirmpaper-title"), () => TryClose())
                .BeginChildElements(body)
                    .AddStaticText(Lang.Get("signalslink:confirmpaper-current"), font, leftLabel)
                    .BeginClip(leftClip)
                        .AddRichtext(current, font, leftText, "currentText")
                    .EndClip()
                    .AddVerticalScrollbar(value => Scroll("currentText", leftClip, value), leftBar, "currentBar");

            if (!Clearing)
            {
                composer
                    .AddStaticText(Lang.Get("signalslink:confirmpaper-new"), font, rightLabel)
                    .BeginClip(rightClip)
                        .AddRichtext(wanted, font, rightText, "newText")
                    .EndClip()
                    .AddVerticalScrollbar(value => Scroll("newText", rightClip, value), rightBar, "newBar");
            }

            SingleComposer = composer
                    .AddSmallButton(Lang.Get("signalslink:confirmpaper-cancel"), OnCancel, cancel)
                    .AddSmallButton(Lang.Get(Clearing
                        ? "signalslink:confirmpaper-clear-yes"
                        : "signalslink:confirmpaper-yes"), OnConfirm, confirm)
                .EndChildElements()
                .Compose();

            SetScrollHeight("currentText", "currentBar");
            if (!Clearing) SetScrollHeight("newText", "newBar");
        }

        /// <summary>
        /// The bar has to be told how much there is to scroll through, or it stays a stub and the
        /// end of a long paper - which is exactly where the change usually is - cannot be reached.
        /// </summary>
        private void SetScrollHeight(string textKey, string barKey)
        {
            GuiElementRichtext text = SingleComposer.GetRichtext(textKey);
            GuiElementScrollbar bar = SingleComposer.GetScrollbar(barKey);

            if (text == null || bar == null) return;

            bar.SetHeights((float)TextHeight, (float)Math.Max(text.Bounds.fixedHeight, TextHeight));
        }

        private void Scroll(string textKey, ElementBounds clip, float value)
        {
            GuiElementRichtext text = SingleComposer.GetRichtext(textKey);
            if (text == null) return;

            text.Bounds.fixedY = 0 - value;
            text.Bounds.CalcWorldBounds();
        }

        private bool OnCancel()
        {
            TryClose();
            return true;
        }

        private bool OnConfirm()
        {
            onConfirm?.Invoke();
            TryClose();
            return true;
        }
    }
}
