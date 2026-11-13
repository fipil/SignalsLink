using SignalsLink.src.signals.cargo;
using SignalsLink.src.signals.chunkanchor;
using SignalsLink.YTT.src.dump;
using SignalsLink.YTT.src.probe;
using SignalsLink.YTT.src.signalman;
using SignalsLink.YTT.src.stoker;
using SignalsLink.YTT.src.taxi;
using SignalsLink.YTT.src.train;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

// A loose DLL has no modinfo.json beside it, so the game reads this instead. Must say the same
// as the modinfo.json the zip carries.
[assembly: ModInfo("Signals Link YTT", "signalslinkytt",
    Description = "Lets the Signals Link freight dock load and unload the trains of Yang's Transport Tycoon.",
    Website = "",
    Version = "1.1.0",
    Authors = new[] { "fipil" }
)]

namespace SignalsLink.YTT.src
{
    /// <summary>
    /// The bridge to Yang's Transport Tycoon. A mod of its own so that everything volatile lives
    /// here and only this has to be released when YTT changes.
    ///
    /// <b>There is no reference to yangtransport.dll</b>, here or in the project file, and there
    /// must never be one: reflection only, behind a probe.
    /// </summary>
    public class SignalsLinkYttMod : ModSystem
    {
        /// <summary>The mod this bridges to. Asked for by name; never linked against.</summary>
        public const string YttModId = "yangtransport";

        public override bool ShouldLoad(EnumAppSide forSide) => true;

        /// <summary>Until this is true the bridge does nothing at all.</summary>
        public bool Enabled { get; private set; }

        public YttSurface Surface { get; private set; }

        /// <summary>A locomotive's fire, for the stoker. Null until the probe has passed; server only.</summary>
        public YttFire Fire { get; private set; }

        /// <summary>A locomotive's power and whistle, for the signalman. Null until the probe has passed; server only.</summary>
        public YttCommands Commands { get; private set; }

        /// <summary>The other mod's stations, for the train ticket. Server only.</summary>
        public YttStations Stations { get; private set; }

        public const string TaxiChannel = "signalslinkytt-taxi";

        private TrainApproachWatch approach;
        private TaxiDispatcher taxi;
        private IClientNetworkChannel taxiClient;
        private GuiDialogTrainTicket ticketDialog;

        /// <summary>
        /// The header vocabulary, on both sides. The client checks a paper's headers for the block
        /// info, and a <c>train</c> it does not know reads as a mistake there - which it did the
        /// moment the registry had more than one kind of holder to choose from. No probe here:
        /// reading a header needs no game and no other mod. The server replaces this with the
        /// probed finder below.
        /// </summary>
        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            // The block exists whether or not YTT does: its JSON is loaded regardless, and a
            // class the game cannot instantiate is a crash on startup. Without YTT it just idles.
            api.RegisterBlockClass("Stoker", typeof(BlockStoker));
            api.RegisterBlockEntityClass("BlockEntityStoker", typeof(BEStoker));
            api.RegisterBlockClass("Signalman", typeof(BlockSignalman));
            api.RegisterBlockEntityClass("BlockEntitySignalman", typeof(BESignalman));
            api.RegisterItemClass("ItemTrainTicket", typeof(ItemTrainTicket));

            if (!api.ModLoader.IsModEnabled(YttModId)) return;

            api.ModLoader.GetModSystem<CargoHolderRegistry>()?.Register(new TrainCargoHolderFinder());
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);

            taxiClient = api.Network.RegisterChannel(TaxiChannel)
                .RegisterMessageType<TaxiOpenRequest>()
                .RegisterMessageType<TaxiStationsPacket>()
                .RegisterMessageType<TaxiOrderPacket>()
                .RegisterMessageType<TaxiStopPacket>()
                .RegisterMessageType<TaxiSummonOfferPacket>()
                .RegisterMessageType<TaxiSummonPacket>()
                .SetMessageHandler<TaxiStationsPacket>(packet => OnTaxiStations(api, packet))
                .SetMessageHandler<TaxiSummonOfferPacket>(packet => OnSummonOffer(api, packet));
        }

        private GuiDialogSummonTrain summonDialog;

        private void OnSummonOffer(ICoreClientAPI api, TaxiSummonOfferPacket packet)
        {
            if (packet == null) return;

            if (!string.IsNullOrEmpty(packet.Error))
            {
                api.TriggerIngameError(this, "ticket-" + packet.Error, Lang.Get("signalslinkytt:ticket-error-" + packet.Error));
                return;
            }

            summonDialog ??= new GuiDialogSummonTrain(api, summon => taxiClient?.SendPacket(summon));
            summonDialog.Show(packet);
        }

        /// <summary>The ticket was used on this client: ask the server what this train can do.</summary>
        public void RequestTaxi() => taxiClient?.SendPacket(new TaxiOpenRequest());

        private void OnTaxiStations(ICoreClientAPI api, TaxiStationsPacket packet)
        {
            if (packet == null) return;

            if (!string.IsNullOrEmpty(packet.Error))
            {
                api.TriggerIngameError(this, "ticket-" + packet.Error, Lang.Get("signalslinkytt:ticket-error-" + packet.Error));
                return;
            }

            ticketDialog ??= new GuiDialogTrainTicket(api,
                order => taxiClient?.SendPacket(order),
                stop => taxiClient?.SendPacket(stop));

            ticketDialog.Show(packet);
        }

        public override void StartServerSide(Vintagestory.API.Server.ICoreServerAPI api)
        {
            base.StartServerSide(api);

            if (!api.ModLoader.IsModEnabled(YttModId))
            {
                // Not an error: the manifest asks for YTT, so someone took it out from under us.
                api.Logger.Notification("[SignalsLink.YTT] " + YttModId + " is not loaded, standing down.");
                return;
            }

            // Diagnostics first: a bridge that stands down below is exactly when somebody wants
            // to see what YTT is thinking.
            SlyttCommand.Register(api);

            // Needs no reflection, so it is not behind the probe: an anchor must never split a
            // train, whatever this version of YTT lets the bridge do with its cargo.
            api.ModLoader.GetModSystem<KeepTogetherRegistry>()?.Register(new TrainKeepTogether());

            // The last moment at which the answer can still be "do nothing" rather than
            // "lose somebody's cargo".
            Surface = YttSurface.Probe(api);
            Surface.Report(api, VersionOf(api));

            // Independent of cargo too: tending a fire needs the engine, not the wagons.
            Fire = new YttFire(Surface, api.Logger);
            Commands = new YttCommands(Surface, api.Logger);
            Stations = new YttStations(Surface, api.Logger);

            // The ticket writes the conductor's timetable under whatever key YTT names today.
            if (Surface.Taxi != null) Timetable.Key = Surface.Taxi.TimetableKey;

            Vintagestory.API.Server.IServerNetworkChannel taxiServer = api.Network.RegisterChannel(TaxiChannel)
                .RegisterMessageType<TaxiOpenRequest>()
                .RegisterMessageType<TaxiStationsPacket>()
                .RegisterMessageType<TaxiOrderPacket>()
                .RegisterMessageType<TaxiStopPacket>()
                .RegisterMessageType<TaxiSummonOfferPacket>()
                .RegisterMessageType<TaxiSummonPacket>();

            taxi = new TaxiDispatcher(api, Fire, Stations, new VirtualTrains(Surface, api.Logger), taxiServer);
            taxiServer
                .SetMessageHandler<TaxiOpenRequest>(taxi.Open)
                .SetMessageHandler<TaxiOrderPacket>(taxi.Order)
                .SetMessageHandler<TaxiStopPacket>(taxi.StopHere)
                .SetMessageHandler<TaxiSummonPacket>(taxi.SummonTrain);
            taxi.Start();

            // Independent of cargo: seeing a train coming needs the simulation, not the wagons.
            ArrivalSourceRegistry arrivals = api.ModLoader.GetModSystem<ArrivalSourceRegistry>();
            if (Surface.CanWatchApproach && arrivals != null)
            {
                approach = new TrainApproachWatch(api, Surface, arrivals);
                arrivals.Register(approach);
                approach.Start();
            }

            if (!Surface.CanCarry) return;

            CargoHolderRegistry registry = api.ModLoader.GetModSystem<CargoHolderRegistry>();

            if (registry == null)
            {
                api.Logger.Warning("[SignalsLink.YTT] Signals Link has no cargo holder registry;"
                    + " this bridge needs a newer version of it.");
                return;
            }

            YttPersistence persistence = new YttPersistence(api, Surface.StoragePointsField.DeclaringType.Assembly);
            registry.Register(new TrainCargoHolderFinder(Surface, persistence));

            Enabled = true;
            api.Logger.Notification("[SignalsLink.YTT] trains can be loaded and unloaded.");
        }

        public override void Dispose()
        {
            approach?.Stop();
            taxi?.Stop();
            ticketDialog?.TryClose();
            summonDialog?.TryClose();
            base.Dispose();
        }

        /// <summary>The other mod's version, for the log line that matters when something breaks.</summary>
        private static string VersionOf(ICoreAPI api)
        {
            foreach (Mod mod in api.ModLoader.Mods)
            {
                if (mod.Info?.ModID == YttModId) return mod.Info.Version ?? "?";
            }

            return "?";
        }
    }
}
