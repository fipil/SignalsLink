using SignalsLink.EP.src.epswitch;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

[assembly: ModDependency("game", "1.21.0")]
[assembly: ModDependency("signals", "0.2.6")]
[assembly: ModDependency("signalslink", "0.1.0")]
[assembly: ModDependency("electricalprogressivebasics", "2.5.0")]
[assembly: ModInfo("Signals Link EP", "signalslinkep",
    Description = "Extends Signals mod with control elements for interacting with the Electrical Progressive mod.",
    Website = "",
    Version = "0.1.0",
    Authors = new[] { "fipil" }
)]

namespace SignalsLink.EP.src
{
    public class SignalsLinkEPMod : ModSystem
    {
        static string MODID = "signalslinkep";
        ICoreAPI api;

        IServerNetworkChannel serverChannel;
        IClientNetworkChannel clientChannel;

        public override void Start(ICoreAPI api)
        {
            this.api = api;
            base.Start(api);

            api.RegisterBlockClass("EPSwitch", typeof(EPSwitch));

            api.RegisterBlockEntityClass("BlockEntityEPSwitch", typeof(BlockEntityEPSwitch));

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

