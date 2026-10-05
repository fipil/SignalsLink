using System;
using System.Collections.Generic;
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

        /// <summary>Loaded machines on this side; each lowers the temporal stability around it (see <see cref="BECraftingMachine.Instability"/>).</summary>
        public readonly HashSet<BECraftingMachine> Machines = new();
        /// <summary>The stability field may go this far below zero in the worst spot: what a rift does to a player standing in it.</summary>
        public const float InstabilityFloor = -20f;

        public override void Start(ICoreAPI api)
        {
            this.api = api;
            base.Start(api);
            api.RegisterBlockClass("CraftingMachine", typeof(BlockCraftingMachine));
            api.RegisterBlockEntityClass("CraftingMachine", typeof(BECraftingMachine));
            api.RegisterBlockEntityBehaviorClass("MPMachineAxle", typeof(BEBehaviorMPMachineAxle));
        }

        // Both sides: the server drains the player's stability from this field, the client turns the gear by it.
        // Hooked once every mod has started: this mod starts before the survival mod that owns the stability system.
        public override void AssetsFinalize(ICoreAPI api)
        {
            var stability = api.ModLoader.GetModSystem<Vintagestory.GameContent.SystemTemporalStability>();
            if (stability == null) { api.Logger.Warning("[signalsmachines] no temporal stability system: machines will not disturb stability"); return; }
            stability.OnGetTemporalStability -= LowerNearMachines;
            stability.OnGetTemporalStability += LowerNearMachines;
            hooked = true;
            api.Logger.Notification("[signalsmachines] hooked into temporal stability ({0})", api.Side);
        }

        private float LowerNearMachines(float stability, double x, double y, double z)
        {
            if (Machines.Count == 0) return stability;
            float pull = 0;
            lock (Machines) foreach (var machine in Machines) pull += machine.Instability(x, y, z);
            // pulled down to a level below 1 (never raised); the floor keeps the worst spot finite
            return pull == 0 ? stability : Math.Max(InstabilityFloor, Math.Min(stability, 1 - pull));
        }

        public void Register(BECraftingMachine machine) { lock (Machines) Machines.Add(machine); }
        public void Unregister(BECraftingMachine machine) { lock (Machines) Machines.Remove(machine); }

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
                .EndSubCommand()
                // Debug: what the stability field looks like where you stand, and why.
                .BeginSubCommand("stability").WithDescription("Temporal stability at your position as the machines shape it").RequiresPlayer()
                .HandleWith(args =>
                {
                    var p = args.Caller.Player.Entity.Pos;
                    var system = sapi.ModLoader.GetModSystem<Vintagestory.GameContent.SystemTemporalStability>();
                    float here = system?.GetTemporalStability(p.X, p.Y, p.Z) ?? float.NaN;
                    var sb = new System.Text.StringBuilder($"stability here {here:0.00} (hooked: {hooked}), machines {Machines.Count}");
                    lock (Machines) foreach (var m in Machines)
                        sb.Append($"\n  {m.Pos}: state {m.State}, inputs {m.Inputs}, doors {m.DoorOpen(4)}/{m.DoorOpen(5)}, pull {m.Instability(p.X, p.Y, p.Z):0.00}, dist {Math.Sqrt(m.Pos.DistanceSqTo(p.X - .5, p.Y - 1.5, p.Z - .5)):0.0}");
                    return TextCommandResult.Success(sb.ToString());
                })
                .EndSubCommand();
        }

        private bool hooked;

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

