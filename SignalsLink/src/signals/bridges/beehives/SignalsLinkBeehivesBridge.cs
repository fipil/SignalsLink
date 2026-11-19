using SignalsLink.src.signals.managedchute.transporting;
using Vintagestory.API.Common;

namespace SignalsLink.src.signals.bridges.beehives
{
    /// <summary>
    /// Lets chutes and dampers work OrekiWoof's beehive: frames in, one per empty slot, and
    /// frames out. Does nothing unless that mod is loaded and its hive looks the way this was
    /// written against.
    /// </summary>
    public class SignalsLinkBeehivesBridge : ModSystem
    {
        public override bool ShouldLoad(EnumAppSide forSide) => true;

        public override double ExecuteOrder() => 0.2; // after the registry

        public override void Start(ICoreAPI api)
        {
            if (!api.ModLoader.IsModEnabled(BeehiveSurface.ModId)) return;

            TransferEndRegistry registry = api.ModLoader.GetModSystem<TransferEndRegistry>();
            if (registry == null) return;

            BeehiveSurface surface = BeehiveSurface.Probe(api);
            surface.Report(api, api.ModLoader.GetMod(BeehiveSurface.ModId)?.Info?.Version ?? "?");
            if (!surface.CanReachFrames) return;

            registry.Register(new BeehiveEndProvider(new TypeBeehiveAccess(surface)));
            api.Logger.Notification("[SignalsLink.Beehives] the hive takes frames from chutes and dampers and gives them back.");
        }
    }
}
