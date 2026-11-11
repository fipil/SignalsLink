using System;

namespace SignalsLink.YTT.src.signalman
{
    /// <summary>What the input pin asks for. The role is the VALUE on the wire, not a switch.</summary>
    public enum SignalmanCommand
    {
        /// <summary>0, and anything this block does not know: just a sensor.</summary>
        Nothing,

        /// <summary>1 to 10: the locomotive's power, in tenths of its safe maximum.</summary>
        Throttle,

        /// <summary>15, what a plain lever gives: one whistle per train.</summary>
        Whistle,
    }

    /// <summary>Which trains the signalman is for, by the way they travel.</summary>
    public enum SignalmanMode
    {
        Both,

        /// <summary>Trains going to the RIGHT as seen from the plate looking at the track.</summary>
        Right,

        Left,
    }

    /// <summary>Which way a train is going, as seen from the plate looking at the track.</summary>
    public enum TravelDirection
    {
        /// <summary>Never seen moving: it was already standing when first seen.</summary>
        Unknown,

        Right,
        Left,
    }

    /// <summary>The signalman's arithmetic, with no world behind it.</summary>
    public static class SignalmanRules
    {
        public const int MaxLevel = 10;
        public const byte WhistleValue = 15;

        /// <summary>The output pin can say no more than fifteen.</summary>
        public const int MaxPlayers = 15;

        public static SignalmanMode ModeFor(string variant)
        {
            return variant switch
            {
                "right" => SignalmanMode.Right,
                "left" => SignalmanMode.Left,
                _ => SignalmanMode.Both,
            };
        }

        public static string VariantOf(SignalmanMode mode)
        {
            return mode switch
            {
                SignalmanMode.Right => "right",
                SignalmanMode.Left => "left",
                _ => "both",
            };
        }

        /// <summary>The wrench: both, right, left, and round again.</summary>
        public static SignalmanMode NextMode(SignalmanMode mode)
        {
            return mode switch
            {
                SignalmanMode.Both => SignalmanMode.Right,
                SignalmanMode.Right => SignalmanMode.Left,
                _ => SignalmanMode.Both,
            };
        }

        /// <summary>
        /// Is this train the signalman's business? A train never seen moving has no direction,
        /// and only "both" takes it: a one-way plate must not act on a train it cannot place.
        /// </summary>
        public static bool ModeMatches(SignalmanMode mode, TravelDirection direction)
        {
            return mode switch
            {
                SignalmanMode.Both => true,
                SignalmanMode.Right => direction == TravelDirection.Right,
                SignalmanMode.Left => direction == TravelDirection.Left,
                _ => false,
            };
        }

        /// <summary>
        /// Which way a movement goes, from its projection on the plate's "right" axis. Physics
        /// never puts a body down at exactly the same place twice, so a centimetre is nothing.
        /// </summary>
        public static TravelDirection DirectionOf(double alongRight)
        {
            if (alongRight > 0.01) return TravelDirection.Right;
            if (alongRight < -0.01) return TravelDirection.Left;

            return TravelDirection.Unknown;
        }

        public static byte PlayersOutput(int players) => (byte)Math.Clamp(players, 0, MaxPlayers);

        /// <summary>The locomotive's own dialog: a slider from 0 to 1500 in steps of 50. MIRRORED.</summary>
        public const int TargetMinimum = 0;
        public const int TargetMaximum = 1500;
        public const int TargetStep = 50;

        public static SignalmanCommand CommandFor(byte input)
        {
            if (input >= 1 && input <= MaxLevel) return SignalmanCommand.Throttle;
            if (input == WhistleValue) return SignalmanCommand.Whistle;

            return SignalmanCommand.Nothing;
        }

        /// <summary>0 nothing, 1 moving, 2 standing - whatever the input says.</summary>
        public static byte OutputFor(bool present, bool standing)
        {
            if (!present) return 0;

            return standing ? (byte)2 : (byte)1;
        }

        /// <summary>
        /// The target temperature for a level: that many tenths of the locomotive's safe maximum,
        /// snapped to the dialog's steps. Ten is exactly "as hot as is safe": above the maximum
        /// the boiler may blow.
        /// </summary>
        public static int TargetTemperature(int level, double maximumCelsius)
        {
            level = Math.Clamp(level, 1, MaxLevel);

            double wanted = level / (double)MaxLevel * Math.Max(0, maximumCelsius);
            int snapped = (int)Math.Round(wanted / TargetStep) * TargetStep;

            return Math.Clamp(snapped, TargetMinimum, TargetMaximum);
        }
    }

    /// <summary>
    /// A command is applied ONCE to each train that turns up, and again only when the value on
    /// the wire changes while that train is still there. Forgotten when the train has gone, so
    /// the next one gets it fresh.
    /// </summary>
    public sealed class SignalmanMemory
    {
        private long convoy;
        private byte value;
        private bool holding;

        public bool ShouldApply(long convoyHead, byte input)
        {
            return !holding || convoy != convoyHead || value != input;
        }

        public void Applied(long convoyHead, byte input)
        {
            holding = true;
            convoy = convoyHead;
            value = input;
        }

        public void Forget()
        {
            holding = false;
        }
    }
}
