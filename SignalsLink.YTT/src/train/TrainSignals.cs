using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace SignalsLink.YTT.src.train
{
    /// <summary>
    /// What a timetabled train says about itself. YTT writes its automation state onto every
    /// vehicle of the convoy as an ordinary entity attribute, so no reflection is needed to read
    /// it - only the key and the two values that matter here.
    /// </summary>
    public static class TrainSignals
    {
        /// <summary>Set on every vehicle of an automated convoy; absent on a hand-driven train.</summary>
        public const string ActionAttribute = "yangtransport.locustActionCode";

        /// <summary>Under way to the next station.</summary>
        public const int Going = 1;

        /// <summary>Arrived and sitting out its dwell time - YTT's own word for "ready to be worked".</summary>
        public const int WaitingAtStation = 2;

        /// <summary>The item that makes a convoy drive itself. It sits in a vanilla attachable slot.</summary>
        public const string ConductorLocust = "conductorlocust";

        /// <summary>
        /// Is the vehicle standing still for cargo?
        ///
        /// The action code is believed only on an automated convoy. The attribute is written by
        /// the automation and cleared by it - but not always: a convoy whose conductor was taken
        /// off after a restart keeps its last code for ever, and a stale "waiting at station" on a
        /// hand-driven train would have goods loaded into it while it moves. So without a
        /// conductor on board the code is ignored and only the position samples count.
        ///
        /// With one: waiting at a station is standing, whatever a sample says - the dwell has
        /// begun. Everything else is judged by watching whether it moves, "going" included: a
        /// train that is going but not moving has run out of steam, and the coal it needs comes
        /// off exactly the dock that would otherwise refuse it.
        /// </summary>
        public static bool IsStanding(int actionCode, bool automated, Func<bool> sampled)
        {
            if (automated && actionCode == WaitingAtStation) return true;

            return sampled != null && sampled();
        }

        /// <summary>Does this vehicle carry a conductor locust on one of its hooks?</summary>
        public static bool HasConductorLocust(Entity entity)
        {
            EntityBehaviorAttachable attachable = entity?.GetBehavior<EntityBehaviorAttachable>();
            if (attachable?.Inventory == null) return false;

            for (int i = 0; i < attachable.Inventory.Count; i++)
            {
                AssetLocation code = attachable.Inventory[i]?.Itemstack?.Collectible?.Code;
                if (code != null && code.Domain == TrainCargoHolderFinder.Domain && code.Path == ConductorLocust) return true;
            }

            return false;
        }
    }
}
