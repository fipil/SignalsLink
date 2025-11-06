using SignalsLink.EP.src.epswitch;
using SignalsLink.EP.src.messages;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.Client.NoObf;

[assembly: ModDependency("game", "1.21.0")]
[assembly: ModDependency("signals", "0.2.6")]
[assembly: ModDependency("electricalprogressivebasics", "2.6.0")]
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
        public static string MODID = "signalslinkep";
        ICoreAPI api;
        ICoreClientAPI clientApi;

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
            this.clientApi = api;
            api.World.Logger.EntryAdded += OnClientLogEntry;

            clientChannel =
                api.Network.RegisterChannel($"{MODID}")
                .RegisterMessageType<EpSwitchSwitchedMessage>()
                .SetMessageHandler<EpSwitchSwitchedMessage>(OnEpSwitchedMessage);
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);

            serverChannel =
                api.Network.RegisterChannel($"{MODID}")
                .RegisterMessageType<EpSwitchSwitchedMessage>();
        }

        private void OnEpSwitchedMessage(EpSwitchSwitchedMessage packet)
        {
            clientApi.World.PlaySoundAt(new AssetLocation($"signalslinkep:sounds/effect/epswitch{(packet.IsOn?"on":"off")}"), packet.Pos.X, packet.Pos.Y, packet.Pos.Z);
        }

        private void OnClientLogEntry(EnumLogType logType, string message, params object[] args)
        {
            if (logType == EnumLogType.VerboseDebug) return;
            System.Diagnostics.Debug.WriteLine("[Client " + logType + "] " + message, args);
        }
    }
}

