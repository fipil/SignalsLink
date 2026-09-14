using System;
using System.Collections;
using SignalsLink.src.signals.chunkanchor;
using SignalsLink.YTT.src.probe;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SignalsLink.YTT.src.train
{
    /// <summary>
    /// Watches YTT's offscreen simulation for automated trains under way, and tells the anchors
    /// where each one is and where it is going - once a second, for as long as it is going.
    ///
    /// Reads only, through the members the surface probe checked. A train that is going is
    /// reported wherever it is; how near is near enough is the anchor's decision, not this one's.
    /// </summary>
    public sealed class TrainApproachWatch : IArrivalSource
    {
        public const string LangKeyText = "signalslink:chunkanchor-wakeon-train";

        private readonly ICoreServerAPI api;
        private readonly YttSurface surface;
        private readonly ArrivalSourceRegistry registry;
        private long listener;
        private bool complained;

        public TrainApproachWatch(ICoreServerAPI api, YttSurface surface, ArrivalSourceRegistry registry)
        {
            this.api = api;
            this.surface = surface;
            this.registry = registry;
        }

        public string Keyword => "train";

        public string LangKey => LangKeyText;

        public void Start()
        {
            if (listener == 0) listener = api.Event.RegisterGameTickListener(Tick, 1000);
        }

        public void Stop()
        {
            if (listener != 0) api.Event.UnregisterGameTickListener(listener);
            listener = 0;
        }

        private void Tick(float dt)
        {
            try
            {
                if (surface.VirtualConvoysField.GetValue(surface.OffscreenSystem) is not IDictionary convoys) return;

                foreach (object convoy in convoys.Values)
                {
                    object automation = surface.ConvoyAutomationField.GetValue(convoy);
                    if (automation == null) continue;
                    if (!Equals(surface.AutomationStatusField.GetValue(automation), surface.GoingStatus)) continue;

                    if (surface.AutomationRouteField.GetValue(automation) is not Array route || route.Length == 0) continue;

                    // The simulation reads an index out of range as the first stop; so does this.
                    int index = (int)surface.AutomationIndexField.GetValue(automation);
                    if (index < 0 || index >= route.Length) index = 0;

                    object stop = route.GetValue(index);
                    if (stop == null) continue;

                    BlockPos target = new BlockPos(
                        (int)surface.RouteXField.GetValue(stop),
                        (int)surface.RouteYField.GetValue(stop),
                        (int)surface.RouteZField.GetValue(stop));

                    if (surface.ConvoyPosesField.GetValue(convoy) is not IList poses || poses.Count == 0) continue;

                    int lead = (int)surface.ConvoyLeadIndexField.GetValue(convoy);
                    if (lead < 0 || lead >= poses.Count) lead = 0;

                    object pose = poses[lead];
                    double x = (double)surface.PoseXField.GetValue(pose);
                    double z = (double)surface.PoseZField.GetValue(pose);

                    registry.Announce(new Vec3d(x, 0, z), target);
                }
            }
            catch (Exception e)
            {
                if (complained) return;

                complained = true;
                api.Logger.Error("[SignalsLink.YTT] could not read the offscreen simulation: " + e
                    + " Anchors will not wake for approaching trains this session.");
                Stop();
            }
        }
    }
}
