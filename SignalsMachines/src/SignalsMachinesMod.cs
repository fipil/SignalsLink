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
            api.RegisterBlockEntityBehaviorClass("MPMachineAxle", typeof(BEBehaviorMPMachineAxle));
        }

        public override void StartServerSide(ICoreServerAPI sapi)
        {
            // Debug: "/machines trace" while aiming at a machine writes everything it does into Logs/signalsmachines-trace-x-y-z.log until toggled off.
            sapi.ChatCommands.Create("machines").WithDescription("Signals Machines").RequiresPrivilege(Privilege.controlserver)
                .BeginSubCommand("trace").WithDescription("Toggle a detailed trace of the machine you are looking at").RequiresPlayer()
                .HandleWith(args =>
                {
                    var sel = args.Caller.Player.CurrentBlockSelection;
                    var be = sel == null ? null : sapi.World.BlockAccessor.GetBlockEntity(sel.Position) as BECraftingMachine;
                    if (be == null) return TextCommandResult.Error("Aim at the lower block of a crafting machine.");
                    string path = be.ToggleTrace();
                    return TextCommandResult.Success(path == null ? "Trace off." : "Trace on: " + path + " (until toggled off or the server stops; " + MachineTrace.MaxBytes / (1024 * 1024) + " MB at most)");
                })
                .EndSubCommand();
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

