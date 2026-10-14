using Vintagestory.API.Client;

namespace SignalsTubes.src.imprint;

/// <summary>Preserves the insertion point when vanilla 1.22 wraps a typed word.</summary>
public sealed class GuiElementImprinterDescription : GuiElementTextArea
{
    private bool typing;

    public GuiElementImprinterDescription(ICoreClientAPI api, ElementBounds bounds, CairoFont font)
        : base(api, bounds, null, font) { }

    public override void OnKeyPress(ICoreClientAPI api, KeyEvent args)
    {
        typing = true;
        try { base.OnKeyPress(api, args); }
        finally { typing = false; }
    }

    public override void LoadValue(List<string> newLines)
    {
        // Capture here, after vanilla has removed any selected text, but before
        // LoadValue clamps the column to the length of the newly wrapped line.
        // OnKeyPress then advances this restored absolute position by one character.
        int caret = CaretPosWithoutLineBreaks;
        base.LoadValue(newLines);
        if (typing) CaretPosWithoutLineBreaks = caret;
    }
}
