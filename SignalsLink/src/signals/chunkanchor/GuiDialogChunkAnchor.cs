using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.chunkanchor
{
    /// <summary>
    /// The anchor's map: pick the chunk columns it keeps loaded.
    ///
    /// This is not really a chunk picker, it is a PRICE TAG. What the anchor costs follows from
    /// what it holds, so the number has to move while the player clicks - seeing the consequence
    /// before paying it does more than any handbook page. The price line is written here from the
    /// start, even while there is nothing yet to charge, so that adding the meter later is a change
    /// of one method and not of the dialog.
    /// </summary>
    public class GuiDialogChunkAnchor : GuiDialog
    {
        private const string MapKey = "anchormap";
        private const string PriceKey = "price";
        private const string SwitchKey = "switch";
        private const string SlotKey = "gearslot";

        /// <summary>
        /// Where the player's chosen zoom is kept, in their own client settings.
        ///
        /// A view setting, not a property of any one anchor: it belongs to whoever is looking, so
        /// it has no business in the save and none on the server. Kept across restarts rather than
        /// only for the session, because a setting that has to be found again every evening is not
        /// really remembered.
        /// </summary>
        public const string ZoomSetting = "signalslinkAnchorMapZoom";

        private float shownZoom = -1f;
        private int shownBlocks = -1;
        private int shownCreatures = -1;

        private readonly BlockPos pos;
        private readonly int anchorCx;
        private readonly int anchorCz;
        private readonly int mapRadius;

        private readonly HashSet<long> held;
        private readonly Action<IReadOnlyCollection<long>> save;
        private readonly Action<bool> setSwitch;
        private readonly BEChunkAnchor anchor;

        private bool switchedOn;

        private WorldMapManager mapManager;
        private ChunkMapLayer terrain;
        private GuiElementAnchorMap map;

        private readonly HashSet<FastVec2i> shown = new HashSet<FastVec2i>();

        public override string ToggleKeyCombinationCode => null;

        public GuiDialogChunkAnchor(ICoreClientAPI capi, BlockPos pos, int chunkSize,
            IEnumerable<long> held, int mapRadius, bool switchedOn,
            Action<IReadOnlyCollection<long>> save, Action<bool> setSwitch,
            BEChunkAnchor anchor) : base(capi)
        {
            this.pos = pos;
            this.anchor = anchor;
            this.mapRadius = mapRadius;
            this.save = save;
            this.setSwitch = setSwitch;
            this.switchedOn = switchedOn;

            anchorCx = (int)Math.Floor((double)pos.X / chunkSize);
            anchorCz = (int)Math.Floor((double)pos.Z / chunkSize);

            this.held = AnchorArea.Sanitise(held, anchorCx, anchorCz, mapRadius);

            Compose(chunkSize);
        }

        private void Compose(int chunkSize)
        {
            List<MapLayer> layers = new List<MapLayer>();

            mapManager = capi.ModLoader.GetModSystem<WorldMapManager>();

            if (mapManager != null)
            {
                foreach (MapLayer layer in mapManager.MapLayers)
                {
                    if (layer is not ChunkMapLayer chunkLayer) continue;

                    terrain = chunkLayer;
                    layers.Add(chunkLayer);
                    break;
                }
            }

            ElementBounds mapBounds = ElementBounds.Fixed(0, 30, 420, 420);
            ElementBounds switchBounds = ElementBounds.Fixed(0, 462, 30, 22);
            ElementBounds switchLabel = ElementBounds.Fixed(38, 462, 260, 24);
            ElementBounds slotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 372, 458, 1, 1);
            ElementBounds slotLabel = ElementBounds.Fixed(300, 462, 68, 24);
            ElementBounds priceBounds = ElementBounds.Fixed(0, 500, 420, 76);

            ElementBounds inner = ElementBounds.Fixed(0, 0, 420, 584);
            ElementBounds outer = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            map = new GuiElementAnchorMap(layers, capi, mapBounds, chunkSize,
                anchorCx, anchorCz, mapRadius, held, OnSelectionChanged);

            map.viewChanged = OnViewChanged;

            // Both of these are called without a null check, so leaving either unset crashes the
            // client the first time the view moves. This one is the world map asking the server for
            // ground it has not seen; we want only what is already known, so it does nothing.
            map.viewChangedSync = (x1, z1, x2, z2) => { };

            SingleComposer = capi.Gui
                .CreateCompo("signalslink-chunkanchor-" + pos, outer)
                .AddShadedDialogBG(inner)
                .AddDialogTitleBar(Lang.Get("signalslink:chunkanchor-dialogtitle"), () => TryClose())
                .BeginChildElements(inner)
                    .AddInteractiveElement(map, MapKey)
                    .AddSwitch(OnSwitch, switchBounds, SwitchKey)
                    .AddStaticText(Lang.Get("signalslink:chunkanchor-switch"),
                        CairoFont.WhiteSmallText(), switchLabel)
                    .AddStaticText(Lang.Get("signalslink:chunkanchor-slotlabel"),
                        CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Right), slotLabel)
                    .AddItemSlotGrid(anchor?.Inventory, SendSlotPacket, 1, slotBounds, SlotKey)
                    .AddDynamicText("", CairoFont.WhiteSmallText(), priceBounds, PriceKey)
                .EndChildElements()
                .Compose();

            // Zoom first, then centre: CenterMapTo works out the view from the zoom, so doing it
            // the other way round centres for one zoom and then leaves it at another.
            //
            // The first time, the whole pickable window is made to fit with a little air around
            // it - a number derived from the window rather than one that looked right on one
            // screen. After that the player's own last zoom wins.
            float across = (2 * mapRadius + 1) * chunkSize;
            float fitted = (float)mapBounds.fixedWidth / across * 0.95f;

            map.ZoomLevel = capi.Settings.Float.Get(ZoomSetting, fitted);

            SingleComposer.GetSwitch(SwitchKey)?.SetValue(switchedOn);

            map.CenterMapTo(pos);

            // Fetch the ground for the WHOLE view straight away. The game calls this itself only
            // when the view moves, so without it the dialog opens showing a patch in the middle and
            // fills the rest in the first time the player touches the wheel - with the dialog
            // background showing through the gaps meanwhile.
            map.EnsureMapFullyLoaded();

            UpdatePrice();
        }

        /// <summary>
        /// Off means the anchor lets everything go and stops paying, but keeps the selection - so
        /// there is a way to stop the bill that does not involve breaking the block and picking
        /// every column again afterwards.
        /// </summary>
        private void OnSwitch(bool on)
        {
            switchedOn = on;
            setSwitch?.Invoke(on);
        }

        private void OnSelectionChanged()
        {
            UpdatePrice();

            save?.Invoke(map.Held);
        }

        /// <summary>
        /// What the selection costs. Two numbers, not one: the anchor stays awake for as long as
        /// its signal holds, so the real drain is a range and showing a single figure would be a
        /// guess dressed as a fact. The floor and the ceiling are both the player's own doing.
        /// </summary>
        private void UpdatePrice()
        {
            SingleComposer.GetDynamicText(PriceKey)?.SetNewText(PriceText());
        }

        /// <summary>
        /// Three lines: what is selected, what it is keeping alive, and what that costs. The census
        /// comes from the server with the block entity, so it is a real count and not an estimate -
        /// but it lags a selection by a few seconds, which the wording admits rather than hides.
        /// </summary>
        private string PriceText()
        {
            string line = Lang.Get("signalslink:chunkanchor-price", held.Count);

            if (anchor == null) return line;

            float units = AnchorCensus.Units(anchor.ActiveBlocks, anchor.Creatures,
                SignalsLinkConfigLoader.Current.AnchorCreatureWeight);

            line += "\n" + Lang.Get("signalslink:chunkanchor-census",
                anchor.ActiveBlocks, anchor.Creatures, (int)units);

            double days = anchor.DaysPerGear();

            line += "\n" + (double.IsInfinity(days)
                ? Lang.Get("signalslink:chunkanchor-free")
                : Lang.Get("signalslink:chunkanchor-rate", days.ToString("0.#")));

            return line;
        }

        /// <summary>
        /// The grid hands over a finished network packet, not something to be serialised. It has to
        /// go through the overload that takes it as it stands - the generic one tries to write it
        /// out with protobuf and throws "no contract can be inferred: Packet_Client".
        /// </summary>
        private void SendSlotPacket(object packet)
        {
            capi.Network.SendBlockEntityPacket(pos.X, pos.Y, pos.Z, packet);
        }

        /// <summary>
        /// Remembers the zoom as soon as it stops changing shape on screen. Written here rather
        /// than from the wheel, because the wheel is not the only thing that moves it.
        /// </summary>
        public override void OnRenderGUI(float deltaTime)
        {
            base.OnRenderGUI(deltaTime);

            // The census arrives from the server a few seconds after a change, so the line is
            // rewritten when it moves - otherwise the player would see the old count and conclude
            // their click did nothing.
            if (anchor != null && (anchor.ActiveBlocks != shownBlocks || anchor.Creatures != shownCreatures))
            {
                shownBlocks = anchor.ActiveBlocks;
                shownCreatures = anchor.Creatures;
                UpdatePrice();
            }

            if (map == null || Math.Abs(map.ZoomLevel - shownZoom) < 0.001f) return;

            shownZoom = map.ZoomLevel;
            capi.Settings.Float.Set(ZoomSetting, shownZoom, false);
        }

        private void OnViewChanged(List<FastVec2i> nowVisible, List<FastVec2i> nowHidden)
        {
            foreach (FastVec2i one in nowVisible) shown.Add(one);
            foreach (FastVec2i one in nowHidden) shown.Remove(one);

            terrain?.OnViewChangedClient(nowVisible, nowHidden);
        }

        public override void OnGuiClosed()
        {
            base.OnGuiClosed();

            // Hand the slot back, the way any container dialog does. Without it the inventory stays
            // open for this player and the next one to touch it gets a rollback.
            SingleComposer.GetSlotGrid(SlotKey)?.OnGuiClosed(capi);

            capi.Network.SendBlockEntityPacket(pos.X, pos.Y, pos.Z, (int)EnumBlockEntityPacketId.Close);

            // Hand the terrain tiles back, or the real world map keeps redrawing ground nobody is
            // looking at any more. Not while the world map itself is open - they are its tiles then.
            if (terrain != null && mapManager?.IsOpened != true && shown.Count > 0)
            {
                terrain.OnViewChangedClient(new List<FastVec2i>(), new List<FastVec2i>(shown));
            }

            shown.Clear();
        }
    }
}
