using System;
using System.Collections.Generic;
using System.Reflection;
using SignalsLink.src.signals.bridges.iw;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.bridges.beehives
{
    /// <summary>
    /// What this bridge needs to find in OrekiWoof's Beehives, looked up by name so SignalsLink
    /// never references that mod: the hive's block entity, which must be a plain container.
    /// Nothing private is touched; the frames sit in its public inventory.
    /// </summary>
    public sealed class BeehiveSurface
    {
        public const string ModId = "orekiwoofsbeehives";
        public const string HiveTypeName = "OrekiWoofsBeehives.BlockEntities.BlockEntityReusableBeehive";

        private static readonly HashSet<string> KnownGood = new HashSet<string>
        {
            // OrekiWoof's Beehives 2.0.0.
            "8a573641690c",
        };

        public bool CanReachFrames { get; private set; }
        public string Surface { get; private set; } = "";
        public string Complaint { get; private set; } = "";

        public Type HiveType { get; private set; }

        public static BeehiveSurface Probe(ICoreAPI api)
        {
            Type hive = null;

            try
            {
                foreach (ModSystem system in api.ModLoader.Systems)
                {
                    Assembly assembly = system?.GetType().Assembly;
                    hive = assembly?.GetType(HiveTypeName);
                    if (hive != null) break;
                }
            }
            catch (Exception e)
            {
                return new BeehiveSurface { Complaint = "looking for the hive failed: " + e.Message };
            }

            return Probe(hive);
        }

        public static BeehiveSurface Probe(Type hive)
        {
            BeehiveSurface surface = new BeehiveSurface();

            try
            {
                surface.Look(hive);
            }
            catch (Exception e)
            {
                surface.CanReachFrames = false;
                surface.Complaint = "the probe itself failed: " + e.Message;
            }

            return surface;
        }

        private void Look(Type hive)
        {
            if (hive == null) { Complaint = HiveTypeName + " is not there"; return; }

            if (!typeof(BlockEntityContainer).IsAssignableFrom(hive))
            {
                Complaint = hive.Name + " is no longer a BlockEntityContainer";
                return;
            }

            HiveType = hive;
            CanReachFrames = true;
            Surface = IwSurface.Fingerprint(hive.Name + ":" + hive.BaseType?.Name);
        }

        /// <summary>Says in the log what was found, and how surprising it was.</summary>
        public void Report(ICoreAPI api, string version)
        {
            if (!CanReachFrames)
            {
                api.Logger.Warning("[SignalsLink.Beehives] standing down: " + Complaint
                    + ". Chutes and dampers will not touch the hive until this is sorted out.");
                return;
            }

            if (KnownGood.Contains(Surface))
            {
                api.Logger.Notification("[SignalsLink.Beehives] hive surface " + Surface + " as expected.");
            }
            else
            {
                api.Logger.Warning("[SignalsLink.Beehives] OrekiWoof's Beehives " + version
                    + " has a hive surface this was not written against (" + Surface + "). It looks usable and"
                    + " will be used, but if the hive behaves oddly with a chute or damper, this line is the first thing to quote.");
            }
        }
    }
}
