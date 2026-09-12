using System;
using System.Collections.Generic;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.chunkanchor
{
    /// <summary>
    /// The world map, with the anchor's chunk columns drawn on it and clickable.
    ///
    /// Built on the game's own <see cref="GuiElementMap"/>, handed the terrain layer out of
    /// <c>WorldMapManager</c>, so the player picks ground they recognise rather than squares on a
    /// grid. Drawing and the mouse live in one class on purpose: a column is chosen where it is
    /// drawn, and splitting that across a map layer would mean keeping two copies of the same
    /// arithmetic in step.
    /// </summary>
    public class GuiElementAnchorMap : GuiElementMap
    {
        /// <summary>How far the mouse may travel between press and release and still be a click.</summary>
        private const int ClickSlack = 4;

        /// <summary>Alpha of the square over a held column. Faint enough to read the ground through.</summary>
        private const float HeldAlpha = 0.28f;

        private const float HoverAlpha = 0.16f;

        private readonly int chunkSize;
        private readonly int anchorCx;
        private readonly int anchorCz;
        private readonly int mapRadius;

        private readonly HashSet<long> held;
        private readonly Action changed;

        private LoadedTexture white;

        private int pressX, pressY;
        private bool hovering;
        private int hoverCx, hoverCz;

        private Vec3d worldPos = new Vec3d();
        private Vec2f viewPos = new Vec2f();

        public GuiElementAnchorMap(List<MapLayer> mapLayers, ICoreClientAPI capi, ElementBounds bounds,
            int chunkSize, int anchorCx, int anchorCz, int mapRadius, HashSet<long> held, Action changed)
            : base(mapLayers, capi, null, bounds, false)
        {
            this.chunkSize = chunkSize;
            this.anchorCx = anchorCx;
            this.anchorCz = anchorCz;
            this.mapRadius = mapRadius;
            this.held = held;
            this.changed = changed;
        }

        /// <summary>Which columns the player has picked. The dialog sends this to the server.</summary>
        public IReadOnlyCollection<long> Held => held;

        public override void ComposeElements(Context ctxStatic, ImageSurface surface)
        {
            base.ComposeElements(ctxStatic, surface);

            EnsureWhite();
        }

        /// <summary>
        /// One white pixel, stretched over each square. Cheaper than a mesh and it tints with the
        /// colour passed to the draw call, so held and hovered squares share it.
        /// </summary>
        private void EnsureWhite()
        {
            if (white != null) return;

            using ImageSurface surface = new ImageSurface(Format.Argb32, 1, 1);
            using Context ctx = new Context(surface);

            ctx.SetSourceRGBA(1, 1, 1, 1);
            ctx.Paint();

            white = new LoadedTexture(Api);
            Api.Gui.LoadOrUpdateCairoTexture(surface, false, ref white);
        }

        // ------------------------------------------------------------------ drawing

        public override void RenderInteractiveElements(float deltaTime)
        {
            // The terrain underneath, drawn by the layers the game gave us.
            base.RenderInteractiveElements(deltaTime);

            if (white == null) return;

            Api.Render.PushScissor(Bounds, false);

            for (int cx = anchorCx - mapRadius; cx <= anchorCx + mapRadius; cx++)
            for (int cz = anchorCz - mapRadius; cz <= anchorCz + mapRadius; cz++)
            {
                bool isHeld = held.Contains(AnchorArea.Key(cx, cz));
                bool isHovered = hovering && cx == hoverCx && cz == hoverCz;

                if (!isHeld && !isHovered) continue;

                DrawColumn(cx, cz, isHeld, cx == anchorCx && cz == anchorCz);
            }

            Api.Render.PopScissor();
        }

        /// <summary>
        /// Deliberately empty, and it must stay that way.
        ///
        /// The base method reads a private property that dereferences the world map dialog, and we
        /// have none - so calling it throws a NullReferenceException on every frame the map is
        /// open. What is lost with it is the map following the player and panning by keyboard,
        /// neither of which this dialog wants: it is centred on the anchor, not on whoever is
        /// looking at it. YTT's route map does exactly the same for exactly the same reason.
        /// </summary>
        public override void PostRenderInteractiveElements(float deltaTime)
        {
        }

        private void DrawColumn(int cx, int cz, bool isHeld, bool isAnchor)
        {
            if (!Corners(cx, cz, out float x, out float y, out float width, out float height)) return;

            float alpha = isHeld ? HeldAlpha : HoverAlpha;

            Api.Render.Render2DTexture(white.TextureId, x, y, width, height, 60f,
                new Vec4f(1f, 1f, 1f, alpha));

            // A rim, because a pale wash alone is hard to find over snow or sand. The anchor's own
            // column is drawn brighter: it is the one square that can never be turned off.
            int rim = isAnchor ? ColorUtil.ToRgba(255, 120, 220, 255)
                : isHeld ? ColorUtil.ToRgba(200, 255, 255, 255)
                : ColorUtil.ToRgba(120, 255, 255, 255);

            Api.Render.RenderRectangle(x, y, 61f, width, height, rim);
        }

        /// <summary>Where a column sits on screen, or false when it is off the visible map.</summary>
        private bool Corners(int cx, int cz, out float x, out float y, out float width, out float height)
        {
            worldPos.Set(cx * chunkSize, 0, cz * chunkSize);
            TranslateWorldPosToViewPos(worldPos, ref viewPos);

            x = (float)Bounds.renderX + viewPos.X;
            y = (float)Bounds.renderY + viewPos.Y;

            worldPos.Set((cx + 1) * chunkSize, 0, (cz + 1) * chunkSize);
            TranslateWorldPosToViewPos(worldPos, ref viewPos);

            width = (float)Bounds.renderX + viewPos.X - x;
            height = (float)Bounds.renderY + viewPos.Y - y;

            if (width <= 0 || height <= 0) return false;

            return x + width >= Bounds.renderX && y + height >= Bounds.renderY
                && x <= Bounds.renderX + Bounds.InnerWidth && y <= Bounds.renderY + Bounds.InnerHeight;
        }

        // ------------------------------------------------------------------ the mouse

        public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
        {
            pressX = args.X;
            pressY = args.Y;

            base.OnMouseDownOnElement(api, args);
        }

        public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
        {
            // Dragging the map and picking a column are the same button, so they are told apart by
            // whether the mouse actually went anywhere. Without this every drag would also toggle
            // whatever column it started on.
            bool wasClick = Math.Abs(args.X - pressX) <= ClickSlack && Math.Abs(args.Y - pressY) <= ClickSlack;

            base.OnMouseUp(api, args);

            if (!wasClick || !IsInside(args.X, args.Y)) return;
            if (!ColumnAt(args.X, args.Y, out int cx, out int cz)) return;

            if (AnchorArea.Toggle(held, cx, cz, anchorCx, anchorCz, mapRadius)) changed?.Invoke();
        }

        public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
        {
            base.OnMouseMove(api, args);

            hovering = IsInside(args.X, args.Y)
                && ColumnAt(args.X, args.Y, out hoverCx, out hoverCz)
                && AnchorArea.InWindow(hoverCx, hoverCz, anchorCx, anchorCz, mapRadius);
        }

        private bool IsInside(int x, int y)
        {
            return x >= Bounds.renderX && y >= Bounds.renderY
                && x <= Bounds.renderX + Bounds.InnerWidth
                && y <= Bounds.renderY + Bounds.InnerHeight;
        }

        /// <summary>Screen point to chunk column. Floor, not truncation: half the world is negative.</summary>
        private bool ColumnAt(int x, int y, out int cx, out int cz)
        {
            viewPos.X = (float)(x - Bounds.renderX);
            viewPos.Y = (float)(y - Bounds.renderY);
            TranslateViewPosToWorldPos(viewPos, ref worldPos);

            cx = (int)Math.Floor(worldPos.X / chunkSize);
            cz = (int)Math.Floor(worldPos.Z / chunkSize);

            return true;
        }

        public override void Dispose()
        {
            base.Dispose();

            white?.Dispose();
            white = null;
        }
    }
}
