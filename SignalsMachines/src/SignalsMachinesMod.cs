using signals.src;
using signals.src.signalNetwork;
using SignalsMachines.src.craftingmachine;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

[assembly: ModInfo("Signals Machines", "signalsmachines",
    Description = "Machines controlled by signals.",
    Website = "",
    Version = "0.1.0",
    Authors = new[] { "fipil" }
)]

namespace SignalsMachines.src
{
    public class SignalsMachinesMod : ModSystem
    {
        ICoreAPI api;

        public override void Start(ICoreAPI api)
        {
            this.api = api;
            base.Start(api);
            api.RegisterBlockClass("CraftingMachine", typeof(BlockCraftingMachine));
            api.RegisterBlockEntityClass("CraftingMachine", typeof(BECraftingMachine));
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            api.World.Logger.EntryAdded += OnClientLogEntry;
        }

        private void OnClientLogEntry(EnumLogType logType, string message, params object[] args)
        {
            if (logType == EnumLogType.VerboseDebug) return;

            // Use a preformatted single string to avoid format parsing on arbitrary log messages.
            System.Diagnostics.Debug.WriteLine($"[Client {logType}] {message}");
        }
    }
}

