using signals.src;
using signals.src.signalNetwork;
using SignalsLink.src.signals.behaviours;
using SignalsLink.src.signals.blocksensor;
using SignalsLink.src.signals.entitysensor;
using SignalsLink.src.signals.hose;
using SignalsLink.src.signals.managedchute;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using SignalsLink.src.signals.link;

[assembly: ModInfo("Signals Link", "signalslink",
    Description = "Extends Signals mod with sensors and control elements for interacting with other mods and vanilla blocks.",
    Website = "",
    Version = "0.2.8",
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
            api.RegisterBlockBehaviorClass("BlockBehaviorLinkCover", typeof(SignalsLink.src.signals.link.BlockBehaviorLinkCover));
            api.RegisterCollectibleBehaviorClass("LinkCutterBehavior", typeof(SignalsLink.src.signals.link.LinkCutterBehavior));
            api.RegisterCollectibleBehaviorClass("WrenchActions", typeof(SignalsLink.src.signals.WrenchActionsBehavior));

            api.RegisterBlockClass("BlockSensor", typeof(BlockSensor));
            api.RegisterBlockClass("EntitySensor", typeof(EntitySensor));
            api.RegisterBlockClass("ManagedChute", typeof(ManagedChute));
            api.RegisterBlockClass("ManagedWallChute", typeof(ManagedWallChute));

            // ManagedHose — řízená hadice (kapaliny)
            api.RegisterBlockClass("HoseValve", typeof(BlockHoseValve));
            api.RegisterBlockClass("HoseCoupling", typeof(BlockHoseCoupling));
            api.RegisterBlockClass("HoseIntake", typeof(BlockHoseIntake));

            // Managed Igniter - zapalovac
            api.RegisterBlockClass("Igniter", typeof(SignalsLink.src.signals.igniter.BlockIgniter));
            api.RegisterBlockEntityClass("BlockEntityIgniter", typeof(SignalsLink.src.signals.igniter.BEIgniter));

            // ManagedSleeve - rukav (predmety a bloky)
            api.RegisterBlockClass("SleeveDamper", typeof(SignalsLink.src.signals.sleeve.BlockSleeveDamper));
            api.RegisterBlockClass("SleeveCoupling", typeof(SignalsLink.src.signals.sleeve.BlockSleeveCoupling));
            api.RegisterBlockClass("SleeveWallCoupling", typeof(SignalsLink.src.signals.sleeve.BlockSleeveWallCoupling));
            api.RegisterBlockEntityClass("SleeveWallCoupling", typeof(SignalsLink.src.signals.sleeve.BESleeveWallCoupling));


            // Skladova plocha - dlazdice a cedule se jmenem
            api.RegisterBlockClass("ManagedDock", typeof(SignalsLink.src.signals.manageddock.BlockManagedDock));
            api.RegisterBlockEntityClass("BlockEntityManagedDock", typeof(SignalsLink.src.signals.manageddock.BEManagedDock));
            api.RegisterBlockEntityClass("BlockEntityChunkAnchor", typeof(SignalsLink.src.signals.chunkanchor.BEChunkAnchor));
            api.RegisterBlockClass("ChunkAnchor", typeof(SignalsLink.src.signals.chunkanchor.BlockChunkAnchor));
            api.RegisterBlockClass("YardTile", typeof(SignalsLink.src.signals.yard.BlockYardTile));
            api.RegisterBlockClass("YardSign", typeof(SignalsLink.src.signals.yard.BlockYardSign));
            api.RegisterBlockEntityClass("YardSign", typeof(SignalsLink.src.signals.yard.BEYardSign));

            api.RegisterBlockEntityClass("BlockEntitySleeveDamper", typeof(SignalsLink.src.signals.sleeve.BlockEntitySleeveDamper));
            api.RegisterBlockEntityClass("BlockEntityHoseValve", typeof(BlockEntityHoseValve));
            api.RegisterBlockEntityClass("BlockEntityBlockSensor", typeof(BEBlockSensor));
            api.RegisterBlockEntityClass("BlockEntityEntitySensor", typeof(BEEntitySensor));
            api.RegisterBlockEntityClass("BlockEntityManagedChute", typeof(BEManagedChute));

            // The kinds of the other party a device can exchange goods with. Registered rather
            // than switched on, so that another mod adds a vehicle without either side knowing
            // about the other.
            api.ModLoader.GetModSystem<SignalsLink.src.signals.cargo.CargoHolderRegistry>()
                ?.Register(new SignalsLink.src.signals.yard.YardCargoHolderFinder());
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

