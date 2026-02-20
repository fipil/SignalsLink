using signals.src;
using signals.src.signalNetwork;
using SignalsLink.src.signals.behaviours;
using SignalsLink.src.signals.blocksensor;
using SignalsLink.src.signals.entitysensor;
using SignalsLink.src.signals.managedchute;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

[assembly: ModInfo("Signals Link", "signalslink",
    Description = "Extends Signals mod with sensors and control elements for interacting with other mods and vanilla blocks.",
    Website = "",
    Version = "0.1.5",
    Authors = new[] { "fipil" }
)]

namespace SignalsLink.src
{
    public class SignalsLinkMod : ModSystem
    {
        ICoreAPI api;

        public override void Start(ICoreAPI api)
        {
            this.api = api;
            base.Start(api);

            api.RegisterBlockBehaviorClass("BlockBehaviorTemporalCharge", typeof(BlockBehaviorTemporalCharge));
            api.RegisterBlockBehaviorClass("BlockBehaviorPaperConditions", typeof(BlockBehaviorPaperConditions));

            api.RegisterBlockClass("BlockSensor", typeof(BlockSensor));
            api.RegisterBlockClass("EntitySensor", typeof(EntitySensor));
            api.RegisterBlockClass("ManagedChute", typeof(ManagedChute));
            api.RegisterBlockClass("ManagedWallChute", typeof(ManagedWallChute));

            api.RegisterBlockEntityClass("BlockEntityBlockSensor", typeof(BEBlockSensor));
            api.RegisterBlockEntityClass("BlockEntityEntitySensor", typeof(BEEntitySensor));
            api.RegisterBlockEntityClass("BlockEntityManagedChute", typeof(BEManagedChute));
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

