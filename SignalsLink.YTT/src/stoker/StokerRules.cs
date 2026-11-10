using System;

namespace SignalsLink.YTT.src.stoker
{
    /// <summary>What the input pin asks for. Levels, not events.</summary>
    public enum StokerCommand
    {
        /// <summary>0: do nothing, the fire lives its own life.</summary>
        Leave,

        /// <summary>1: keep it burning - light it whenever it is out.</summary>
        Burn,

        /// <summary>2: keep it out - put it out whenever it burns.</summary>
        Quench,
    }

    public enum StokerAction
    {
        None,
        Ignite,
        Extinguish,
    }

    /// <summary>
    /// The stoker's decisions, as plain arithmetic - no world, no entity, so they can be checked
    /// on a table.
    /// </summary>
    public static class StokerRules
    {
        public const int MinReach = 1;
        public const int MaxReach = 10;

        /// <summary>One block of platform, then the track.</summary>
        public const int DefaultReach = 2;

        /// <summary>Anything above 2 is nothing: a value this block does not know is not a command.</summary>
        public static StokerCommand CommandFor(byte input)
        {
            return input switch
            {
                1 => StokerCommand.Burn,
                2 => StokerCommand.Quench,
                _ => StokerCommand.Leave,
            };
        }

        /// <summary>
        /// Act only on a DIFFERENCE between what is wanted and what is. That is what makes a held
        /// 1 "keep the fire" rather than "light it once": it lights again when the fuel runs out,
        /// and it never pays twice for a fire that already burns.
        /// </summary>
        public static StokerAction Decide(StokerCommand command, bool burning)
        {
            return command switch
            {
                StokerCommand.Burn when !burning => StokerAction.Ignite,
                StokerCommand.Quench when burning => StokerAction.Extinguish,
                _ => StokerAction.None,
            };
        }

        /// <summary>
        /// What the Extinguish button in the locomotive's own dialog takes: a litre of water per
        /// hundred degrees, in items, rounded up. MIRRORED from the other mod (SteamEngineController,
        /// packet CutFuel) because that code is inline in a packet handler and cannot be called;
        /// if the author changes it, this is the one place to follow.
        /// </summary>
        public static int WaterItemsFor(double temperatureC, double itemsPerLitre)
        {
            if (temperatureC <= 0) return 0;
            if (itemsPerLitre <= 0) itemsPerLitre = 1;

            return (int)Math.Ceiling(temperatureC / 100.0 * itemsPerLitre);
        }

        public static int ClampReach(int reach) => Math.Clamp(reach, MinReach, MaxReach);

        /// <summary>The wrench cycles 1..10 and round again.</summary>
        public static int NextReach(int reach)
        {
            return reach >= MaxReach || reach < MinReach ? MinReach : reach + 1;
        }

        /// <summary>The output pin: burning or not, nothing else, whatever the input says.</summary>
        public static byte OutputFor(bool burning) => burning ? (byte)1 : (byte)0;

        /// <summary>The window glows while the stoker is asked to keep the fire.</summary>
        public static bool WindowLit(byte input) => CommandFor(input) == StokerCommand.Burn;
    }
}
