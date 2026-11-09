using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SignalsLink.src.signals.managedchute.transporting
{
    /// <summary>
    /// The firepit under a boiler. It is not a block but a stage number inside the boiler's
    /// block entity, so <c>target firepit</c> builds it by mirroring BlockEntityBoiler.OnInteract:
    /// tinder at stage 0, firewood through stages 1-4, and from stage 5 on firewood is fuel,
    /// taken while there is less than six hours of it.
    /// </summary>
    public static class BoilerFirepit
    {
        public const int Bare = 0;
        public const int Cold = 5;
        public const int Lit = 6;
        public const int Extinct = 7;

        public const float RefuelBelowHours = 6f;
        public const float HoursPerLog = 2f;

        public static bool TakesTinder(int stage) => stage == Bare;

        public static bool TakesFirewood(int stage, float fuelHours)
            => (stage >= 1 && stage < Cold) || (stage >= Cold && fuelHours <= RefuelBelowHours);

        /// <summary>Anything left to build or to feed? Decides whether a `target firepit` block holds.</summary>
        public static bool WantsSomething(int stage, float fuelHours)
            => TakesTinder(stage) || TakesFirewood(stage, fuelHours);

        public static bool Matches(int stage, float fuelHours, ItemStack stack)
        {
            CollectibleObject item = stack?.Collectible;
            if (item is ItemDryGrass) return TakesTinder(stage);
            if (item is ItemFirewood) return TakesFirewood(stage, fuelHours);
            return false;
        }

        /// <summary>One item applied to the stage machine. False when it is not wanted.</summary>
        public static bool Step(int stage, float fuelHours, bool firewood, out int nextStage, out float nextFuelHours)
        {
            nextStage = stage;
            nextFuelHours = fuelHours;

            if (!firewood)
            {
                if (!TakesTinder(stage)) return false;
                nextStage = stage + 1;
                return true;
            }

            if (!TakesFirewood(stage, fuelHours)) return false;

            if (stage < Cold) nextStage = stage + 1;
            else if (stage == Extinct) nextStage = Cold;

            nextFuelHours = System.Math.Max(HoursPerLog, fuelHours + HoursPerLog);
            return true;
        }

        public static bool Advance(IWorldAccessor world, BlockEntityBoiler boiler, ItemStack stack)
        {
            if (boiler == null || !Matches(boiler.firepitStage, boiler.fuelHours, stack)) return false;
            if (!Step(boiler.firepitStage, boiler.fuelHours, stack.Collectible is ItemFirewood, out int stage, out float fuel)) return false;

            boiler.firepitStage = stage;
            boiler.fuelHours = fuel;
            boiler.MarkDirty(true);

            Block shape = world.GetBlock(BlockEntityBoiler.firepitShapeBlockCodes[stage]);
            if (shape?.Sounds?.Place != null) world.PlaySoundAt(shape.Sounds.Place, boiler.Pos, 0.0);

            return true;
        }
    }
}
