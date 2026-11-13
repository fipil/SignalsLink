using Cairo;
using Vintagestory.API.Client;

namespace SignalsLink.YTT.src.taxi
{
    /// <summary>A flat stripe behind the chosen row of a list. Nothing more.</summary>
    public class GuiElementRowHighlight : GuiElement
    {
        public GuiElementRowHighlight(ICoreClientAPI capi, ElementBounds bounds) : base(capi, bounds) { }

        public override void ComposeElements(Context ctx, ImageSurface surface)
        {
            Bounds.CalcWorldBounds();

            ctx.SetSourceRGBA(1, 1, 1, 0.18);
            ctx.Rectangle(Bounds.drawX, Bounds.drawY, Bounds.OuterWidth, Bounds.OuterHeight);
            ctx.Fill();
        }
    }

    public static class GuiElementRowHighlightHelper
    {
        public static GuiComposer AddRowHighlight(this GuiComposer composer, ElementBounds bounds)
        {
            if (!composer.Composed) composer.AddStaticElement(new GuiElementRowHighlight(composer.Api, bounds));
            return composer;
        }
    }
}
