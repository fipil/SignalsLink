using System;
using SignalsLink.YTT.src.probe;
using SignalsLink.YTT.src.train;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SignalsLink.YTT.src.signalman
{
    public enum ThrottleResult
    {
        /// <summary>Set, or already there.</summary>
        Applied,

        /// <summary>The locomotive's thermostat is locked to its maximum; the dialog refuses too.</summary>
        Locked,

        /// <summary>This version does not allow it, or the engine could not be reached.</summary>
        Unavailable,
    }

    /// <summary>
    /// The orders a signalman gives a locomotive: its power, and a whistle. Each behind its own
    /// part of the probe, each wrapped, each standing down on its own for the session after a
    /// failure - cargo and the fire are none of their business.
    /// </summary>
    public class YttCommands
    {
        private readonly YttSurface surface;
        private readonly YttEngine engine;
        private readonly ILogger logger;
        private bool throttleFailed;
        private bool whistleFailed;

        public YttCommands(YttSurface surface, ILogger logger)
        {
            this.surface = surface;
            this.logger = logger;
            engine = new YttEngine(surface);
        }

        public bool CanSetThrottle => !throttleFailed && surface?.CanSetThrottle == true;

        public bool CanWhistle => !whistleFailed && surface?.CanWhistle == true;

        /// <summary>
        /// The thermostat to a level's worth of the locomotive's safe maximum. Through the
        /// private setter, then the engine is asked to write itself down, since it never does
        /// on its own (Y78).
        /// </summary>
        public ThrottleResult SetThrottle(Entity locomotive, int level)
        {
            if (!CanSetThrottle) return ThrottleResult.Unavailable;

            try
            {
                object controller = engine.ControllerOf(locomotive);
                if (controller == null) return ThrottleResult.Unavailable;

                ThrottleMembers throttle = surface.Throttle;

                if (throttle.Locked.GetValue(controller) is true) return ThrottleResult.Locked;

                object configuration = throttle.Configuration.GetValue(controller);
                if (ThrottleMembers.Read(throttle.MaxTemperature, configuration) is not double maximum) return ThrottleResult.Unavailable;

                int target = SignalmanRules.TargetTemperature(level, maximum);

                if (throttle.Limit.GetValue(controller) is double current && (int)Math.Round(current) == target)
                {
                    return ThrottleResult.Applied;
                }

                throttle.Limit.SetValue(controller, (double)target);
                engine.Save(locomotive);

                return ThrottleResult.Applied;
            }
            catch (Exception e)
            {
                throttleFailed = true;
                (locomotive?.World?.Logger ?? logger)?.Error("[SignalsLink.YTT] could not set a locomotive's power: " + e
                    + " Cargo and the fire are unaffected; signalmen set no power for the rest of this session.");

                return ThrottleResult.Unavailable;
            }
        }

        /// <summary>One whistle, if the locomotive has one and its cooldown has passed.</summary>
        public bool Whistle(Entity locomotive)
        {
            if (!CanWhistle) return false;

            try
            {
                EntityBehavior behavior = locomotive?.GetBehavior(YttSurface.SteamBehavior);
                if (behavior == null) return false;

                return surface.WhistleMethod.Invoke(behavior, null) is true;
            }
            catch (Exception e)
            {
                whistleFailed = true;
                (locomotive?.World?.Logger ?? logger)?.Error("[SignalsLink.YTT] could not make a locomotive whistle: " + e
                    + " Cargo and the fire are unaffected; signalmen whistle no more this session.");

                return false;
            }
        }
    }
}
