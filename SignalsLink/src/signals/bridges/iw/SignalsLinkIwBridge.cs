using SignalsLink.src.signals.managedchute.transporting;
using Vintagestory.API.Common;

namespace SignalsLink.src.signals.bridges.iw
{
    /// <summary>
    /// Lets chutes and dampers feed Immersive Woodworking's chopper: logs onto the bed, a new head
    /// onto the arm once the old one is worn out. Does nothing unless that mod is loaded and its
    /// chopper looks the way this was written against.
    /// </summary>
    public class SignalsLinkIwBridge : ModSystem
    {
        public override bool ShouldLoad(EnumAppSide forSide) => true;

        public override double ExecuteOrder() => 0.2; // after the registry

        public override void Start(ICoreAPI api)
        {
            if (!api.ModLoader.IsModEnabled(IwSurface.ModId)) return;

            TransferEndRegistry registry = api.ModLoader.GetModSystem<TransferEndRegistry>();
            if (registry == null) return;

            IwSurface surface = IwSurface.Probe(api);
            surface.Report(api, api.ModLoader.GetMod(IwSurface.ModId)?.Info?.Version ?? "?");
            if (!surface.CanFeedLogs) return;

            registry.Register(new ChopperEndProvider(new ReflectionChopperAccess(surface)));
            api.Logger.Notification("[SignalsLink.IW] the chopper takes logs" + (surface.CanFitHead ? " and heads" : "") + " from chutes and dampers.");
        }
    }
}
