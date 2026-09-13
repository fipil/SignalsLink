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
        private const string NameKey = "anchorname";
        private string savedName;
        private const string MapKey = "anchormap";
        private const string PriceKey = "price";
        private const string SwitchKey = "switch";
        private const string SlotKey = "gearslot";
        private const string ChargeKey = "charge";
        private const string WakeKey = "wake";
        private const string WindowKey = "window";
        private const string JoinKey = "wakeonjoin";

        /// <summary>
        /// What the player may pick, in in-game hours. Zero is "never sleeps", which has to be
        /// first because it is what an anchor does until somebody decides otherwise.
        /// </summary>
        private static readonly double[] WakeChoices = { 0, 1, 4, 24 };

        /// <summary>And how long it stays up each time, also in in-game hours.</summary>
        private static readonly double[] WindowChoices = { 0.25, 0.5, 1, 2 };

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
        private int shownCharge = -1;
        private bool shownReady;
        private int shownColumnCount = -1;
        private bool repaintWanted;

        private readonly BlockPos pos;
        private readonly int anchorCx;
        private readonly int anchorCz;
        private readonly int mapRadius;
        private readonly int maxColumns;

        private readonly HashSet<long> held;
        private readonly Action<IReadOnlyCollection<long>> save;
        private readonly Action<bool> setSwitch;
        private readonly Action<double, double, bool> setCycle;
        private readonly BEChunkAnchor anchor;

        private bool switchedOn;

        private WorldMapManager mapManager;
        private ChunkMapLayer terrain;
        private GuiElementAnchorMap map;

        private readonly HashSet<FastVec2i> shown = new HashSet<FastVec2i>();

        public override string ToggleKeyCombinationCode => null;

        public GuiDialogChunkAnchor(ICoreClientAPI capi, BlockPos pos, int chunkSize,
            IEnumerable<long> held, int mapRadius, bool switchedOn, int maxColumns,
            Action<IReadOnlyCollection<long>> save, Action<bool> setSwitch,
            Action<double, double, bool> setCycle, BEChunkAnchor anchor) : base(capi)
        {
            this.pos = pos;
            this.anchor = anchor;
            this.mapRadius = mapRadius;
            this.maxColumns = maxColumns;
            this.save = save;
            this.setSwitch = setSwitch;
            this.setCycle = setCycle;
            this.switchedOn = switchedOn;

            anchorCx = (int)Math.Floor((double)pos.X / chunkSize);
            anchorCz = (int)Math.Floor((double)pos.Z / chunkSize);

            this.held = AnchorArea.Sanitise(held, anchorCx, anchorCz, mapRadius, maxColumns);

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

            // Two columns under the title bar: the map on the left, everything that is a SETTING on
            // the right, and the price across the bottom where it reads as the sum of both.
            //
            // Every number below is Pad away from its neighbour. Without that the elements sit
            // flush against the dialog frame, which makes a window look unfinished however right
            // the contents are.
            const int Pad = 12;

            // The dialog's own title bar. Content that starts at zero disappears under it.
            const int TitleBar = 31;

            const int MapSide = 420;

            // Wide enough for German, which says everything in about half as many words again.
            const int SideColumn = 270;

            const int PriceHeight = 74;
            const int SlotSide = 48;

            int nameTop = TitleBar + Pad;
            int top = nameTop + 44;
            int right = Pad + MapSide + Pad;
            int width = right + SideColumn + Pad;

            int mapBottom = top + MapSide;
            int belowMap = mapBottom + Pad;

            ElementBounds mapBounds = ElementBounds.Fixed(Pad, top, MapSide, MapSide);

            ElementBounds switchBounds = ElementBounds.Fixed(right, top, 30, 22);
            ElementBounds switchLabel = ElementBounds.Fixed(right + 38, top + 3, SideColumn - 38, 24);

            ElementBounds wakeTitle = ElementBounds.Fixed(right, top + 44, SideColumn, 24);
            ElementBounds wakeDrop = ElementBounds.Fixed(right, top + 70, SideColumn, 28);
            ElementBounds windowTitle = ElementBounds.Fixed(right, top + 108, SideColumn, 24);
            ElementBounds windowDrop = ElementBounds.Fixed(right, top + 134, SideColumn, 28);
            ElementBounds joinBounds = ElementBounds.Fixed(right, top + 174, 30, 22);
            ElementBounds joinLabel = ElementBounds.Fixed(right + 38, top + 172, SideColumn - 38, 40);

            // The gear sits on the same line the map ends on, which leaves the whole middle of the
            // column free for the wake settings when they arrive.
            int slotTop = mapBottom - SlotSide;

            ElementBounds chargeLabel = ElementBounds.Fixed(right, slotTop - 30, SideColumn, 24);
            ElementBounds slotLabel = ElementBounds.Fixed(right, slotTop + 12, SideColumn - SlotSide - 12, 24);
            ElementBounds slotBounds = ElementStdBounds.SlotGrid(
                EnumDialogArea.None, right + SideColumn - SlotSide, slotTop, 1, 1);

            ElementBounds priceBounds = ElementBounds.Fixed(Pad, belowMap, width - 2 * Pad, PriceHeight);

            ElementBounds inner = ElementBounds.Fixed(0, 0, width, belowMap + PriceHeight + Pad);
            ElementBounds outer = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

            map = new GuiElementAnchorMap(layers, capi, mapBounds, chunkSize,
                anchorCx, anchorCz, mapRadius, maxColumns, held, OnSelectionChanged);

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
                    .AddStaticText(Lang.Get("signalslink:chunkanchor-name"),
                        CairoFont.WhiteSmallText(), ElementBounds.Fixed(Pad, nameTop + 4, 130, 28))
                    .AddTextInput(ElementBounds.Fixed(Pad + 140, nameTop, width - 2 * Pad - 140, 30),
                        null, CairoFont.WhiteSmallText(), NameKey)
                    .AddInteractiveElement(map, MapKey)
                    .AddSwitch(OnSwitch, switchBounds, SwitchKey)
                    .AddStaticText(Lang.Get("signalslink:chunkanchor-switch"),
                        CairoFont.WhiteSmallText(), switchLabel)
                    .AddStaticText(Lang.Get("signalslink:chunkanchor-waketitle"),
                        CairoFont.WhiteSmallText(), wakeTitle)
                    .AddDropDown(WakeCodes(), WakeNames(), WakeIndex(), OnWakePicked, wakeDrop, WakeKey)
                    .AddStaticText(Lang.Get("signalslink:chunkanchor-windowtitle"),
                        CairoFont.WhiteSmallText(), windowTitle)
                    .AddDropDown(WindowCodes(), WindowNames(), WindowIndex(), OnWindowPicked, windowDrop, WindowKey)
                    .AddSwitch(OnJoinToggled, joinBounds, JoinKey)
                    .AddStaticText(Lang.Get("signalslink:chunkanchor-wakeonjoin"),
                        CairoFont.WhiteDetailText(), joinLabel)
                    .AddDynamicText("", CairoFont.WhiteSmallText(), chargeLabel, ChargeKey)
                    .AddStaticText(Lang.Get("signalslink:chunkanchor-slotlabel"),
                        CairoFont.WhiteSmallText(), slotLabel)
                    .AddItemSlotGrid(anchor?.Inventory, SendSlotPacket, 1, slotBounds, SlotKey)
                    .AddDynamicText("", CairoFont.WhiteSmallText(), priceBounds, PriceKey)
                .EndChildElements()
                .Compose();

            savedName = anchor?.AnchorName ?? "";
            SingleComposer.GetTextInput(NameKey).SetValue(savedName);

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
            SingleComposer.GetSwitch(JoinKey)?.SetValue(anchor?.WakeOnPlayerJoin ?? true);

            ShowWakeSettings();

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
        private void SaveName()
        {
            var input = SingleComposer.GetTextInput(NameKey);
            string text = input.GetText();
            if (text == savedName) return;
            string name = AnchorDisplay.CleanName(text);
            if (name != text) input.SetValue(name);
            if (name == savedName) return;
            savedName = name;
            anchor?.SendNameToServer(name);
        }

        private void OnSwitch(bool on)
        {
            SaveName();
            switchedOn = on;
            setSwitch?.Invoke(on);
        }

        private static string[] WakeCodes()
        {
            string[] codes = new string[WakeChoices.Length];
            for (int i = 0; i < codes.Length; i++) codes[i] = WakeChoices[i].ToString("0.##");
            return codes;
        }

        private static string[] WakeNames()
        {
            string[] names = new string[WakeChoices.Length];

            for (int i = 0; i < names.Length; i++)
            {
                names[i] = WakeChoices[i] <= 0
                    ? Lang.Get("signalslink:chunkanchor-wake-never")
                    : Lang.Get("signalslink:chunkanchor-wake-every", WakeChoices[i].ToString("0.##"));
            }

            return names;
        }

        private static string[] WindowCodes()
        {
            string[] codes = new string[WindowChoices.Length];
            for (int i = 0; i < codes.Length; i++) codes[i] = WindowChoices[i].ToString("0.##");
            return codes;
        }

        private static string[] WindowNames()
        {
            string[] names = new string[WindowChoices.Length];

            for (int i = 0; i < names.Length; i++)
            {
                names[i] = Lang.Get("signalslink:chunkanchor-window-for",
                    (WindowChoices[i] * 60).ToString("0"));
            }

            return names;
        }

        /// <summary>Nearest match, so a value set before the choices changed still shows as one.</summary>
        private int WakeIndex() => Nearest(WakeChoices, anchor?.WakeIntervalHours ?? 0);

        private int WindowIndex() => Nearest(WindowChoices, anchor?.WakeWindowHours ?? 0.5);

        private static int Nearest(double[] choices, double value)
        {
            int best = 0;

            for (int i = 1; i < choices.Length; i++)
            {
                if (Math.Abs(choices[i] - value) < Math.Abs(choices[best] - value)) best = i;
            }

            return best;
        }

        private void OnWakePicked(string code, bool selected)
        {
            ShowWakeSettings();
            SendCycle();
        }

        /// <summary>
        /// Greys out the two settings that only mean something once the anchor sleeps at all.
        ///
        /// Leaving them live under "never sleeps" invites the player to set a window and a wake-up
        /// rule and then wonder why neither ever happens.
        /// </summary>
        private void ShowWakeSettings()
        {
            bool sleeps = SelectedWake() > 0;

            GuiElementDropDown window = SingleComposer.GetDropDown(WindowKey);

            if (window != null)
            {
                window.Enabled = sleeps;

                // AFTER the assignment, never before. The game dims the drop-down's font when it
                // composes it disabled and never puts the alpha back - and the Enabled setter
                // composes with the OLD value, so setting the alpha first only gets it dimmed
                // straight back again. Put right here, the repaint below is the first compose that
                // sees both the new state and an undimmed font.
                if (window.Font?.Color?.Length > 3) window.Font.Color[3] = sleeps ? 1.0 : 0.35;
            }

            GuiElementSwitch join = SingleComposer.GetSwitch(JoinKey);
            if (join != null) join.Enabled = sleeps;

            // Enabled decides whether an element responds straight away, but how it LOOKS is drawn
            // in the static pass - so without a repaint the two disagree and the greying is always
            // one change behind what actually works.
            repaintWanted = true;
        }

        private double SelectedWake()
        {
            int? index = SingleComposer.GetDropDown(WakeKey)?.SelectedIndices?[0];

            return index == null ? 0 : WakeChoices[index.Value];
        }

        private void OnWindowPicked(string code, bool selected) => SendCycle();

        private void OnJoinToggled(bool on) => SendCycle();

        private void SendCycle()
        {
            SaveName();
            double interval = SelectedWake();
            double window = WindowChoices[SingleComposer.GetDropDown(WindowKey)?.SelectedIndices?[0] ?? 1];

            bool onJoin = SingleComposer.GetSwitch(JoinKey)?.On ?? true;

            setCycle?.Invoke(interval, window, onJoin);
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

            SingleComposer.GetDynamicText(ChargeKey)?.SetNewText(
                Lang.Get("signalslink:chunkanchor-charge", anchor?.ChargePercent ?? 0));
        }

        /// <summary>
        /// Three lines: what is selected, what it is keeping alive, and what that costs. The census
        /// comes from the server with the block entity, so it is a real count and not an estimate -
        /// but it lags a selection by a few seconds, which the wording admits rather than hides.
        /// </summary>
        private string PriceText()
        {
            string line = Lang.Get("signalslink:chunkanchor-price", held.Count, maxColumns);

            if (anchor == null) return line;
            if (!anchor.CensusReady) return line + "\n�";

            float units = AnchorCensus.AnchorUnits(anchor.ActiveBlocks, anchor.Creatures, held.Count,
                anchor.Settings.AnchorCreatureWeight,
                anchor.Settings.AnchorColumnWeight);

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

            // Next frame, not inside the click that caused it: recomposing an element from its own
            // handler is asking for trouble.
            if (repaintWanted)
            {
                repaintWanted = false;
                SingleComposer.ReCompose();
            }

            // The census arrives from the server a few seconds after a change, so the line is
            // rewritten when it moves - otherwise the player would see the old count and conclude
            // their click did nothing.
            if (SingleComposer.GetTextInput(NameKey)?.HasFocus == false) SaveName();

            if (anchor != null && (anchor.ActiveBlocks != shownBlocks || anchor.Creatures != shownCreatures
                || anchor.ChargePercent != shownCharge || anchor.CensusReady != shownReady || anchor.Columns.Count != shownColumnCount))
            {
                shownBlocks = anchor.ActiveBlocks;
                shownCreatures = anchor.Creatures;
                shownCharge = anchor.ChargePercent;
                shownReady = anchor.CensusReady;
                shownColumnCount = anchor.Columns.Count;
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
            SaveName();
            base.OnGuiClosed();

            // Hand the slot back, the way any container dialog does. Without it the inventory stays
            // open for this player and the next one to touch it gets a rollback.
            SingleComposer.GetSlotGrid(SlotKey)?.OnGuiClosed(capi);

            capi.Network.SendBlockEntityPacket(pos, (int)EnumBlockEntityPacketId.Close);

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
