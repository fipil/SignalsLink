using System;
using Vintagestory.API.Datastructures;

namespace SignalsLink.src.signals.hose
{
    public static class LiquidCredit
    {
        public static decimal Read(ITreeAttribute tree)
        {
            double value = tree.GetDouble("remainingLitres", tree.GetInt("remaining", 0));
            return double.IsFinite(value) ? (decimal)Math.Clamp(value, 0, int.MaxValue) : 0;
        }
        public static void Write(ITreeAttribute tree, decimal value)
        {
            tree.SetDouble("remainingLitres", (double)value);
            tree.SetInt("remaining", (int)Math.Min(int.MaxValue, value));
        }
    }
}
