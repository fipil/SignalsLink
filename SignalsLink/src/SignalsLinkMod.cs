using signals.src.signalNetwork;
using SignalsLink.src.signals.blocksensor;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

[assembly: ModInfo("Signals Link", "signalslink",
    Description = "Extends Signals mod with sensors and control elements for interacting with other mods and vanilla blocks.",
    Website = "",
    Version = "0.0.1",
    Authors = new[] { "fipil" }
)]

namespace SignalsLink.src
{
    public class SignalsLinkMod : ModSystem
    {
        static string MODID = "signalslink";
        ICoreAPI api;

        IServerNetworkChannel serverChannel;
        IClientNetworkChannel clientChannel;

        public override void Start(ICoreAPI api)
        {
            this.api = api;
            base.Start(api);

            api.RegisterBlockClass("BlockSensor", typeof(BlockSensor));

            api.RegisterBlockEntityClass("BlockEntityBlockSensor", typeof(BEBlockSensor));

        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            api.World.Logger.EntryAdded += OnClientLogEntry;
        }

        private void OnClientLogEntry(EnumLogType logType, string message, params object[] args)
        {
            if (logType == EnumLogType.VerboseDebug) return;
            System.Diagnostics.Debug.WriteLine("[Client " + logType + "] " + message, args);
        }
    }
}

