using Vintagestory.API.Client;

namespace SignalsLink.src.signals.paperConditions
{
    /// <summary>
    /// A scrollbar that takes the wheel only over its own text or itself.
    ///
    /// The composer offers an unhandled wheel to every element, so with two bars in one dialog
    /// the first one scrolled wherever the mouse was.
    /// </summary>
    public class GuiElementHoverScrollbar : GuiElementScrollbar
    {
        private readonly ElementBounds over;

        public GuiElementHoverScrollbar(ICoreClientAPI capi, System.Action<float> onNewScrollbarValue,
            ElementBounds bounds, ElementBounds over)
            : base(capi, onNewScrollbarValue, bounds)
        {
            this.over = over;
        }

        public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs args)
        {
            int x = api.Input.MouseX, y = api.Input.MouseY;
            if (!over.PointInside(x, y) && !Bounds.PointInside(x, y)) return;

            base.OnMouseWheel(api, args);
        }
    }
}
