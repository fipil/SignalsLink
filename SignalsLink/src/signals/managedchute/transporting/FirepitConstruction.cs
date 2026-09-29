using Vintagestory.API.Common;
using Vintagestory.GameContent;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.managedchute.transporting
{
    /// <summary>
    /// Building a firepit, one stage at a time — the shared half of <c>target firepit</c>.
    ///
    /// A firepit is built in steps, and the steps are readable straight off the vanilla block's
    /// drop table: <c>construct1</c> gives back one dry grass, <c>construct2</c> one grass and one
    /// firewood, and so on to <c>construct4</c> with three. One more piece of firewood turns that
    /// into the finished <c>extinct</c> firepit — one grass and four firewood in total, which is
    /// exactly what the handbook asks a player for.
    ///
    /// The game's own <c>BlockFirepit.TryConstruct</c> needs an <c>IPlayer</c>, which no machine
    /// has. This is the third place that has come up (placed containers and ground piles were the
    /// first two) and it is handled the same way: set the block directly, because the variant code
    /// of the next stage is something we can work out ourselves.
    /// </summary>
    public static class FirepitConstruction
    {
        public const string FirepitCode = "firepit";
        private const string BurnStateVariant = "burnstate";

        /// <summary>Item that starts a firepit off on bare ground.</summary>
        public const string TinderItemCode = "drygrass";

        /// <summary>Item every further stage is built from.</summary>
        public const string FuelItemCode = "firewood";

        /// <summary>Stage codes in build order; the last one is the finished firepit.</summary>
        private static readonly string[] Stages = { "construct1", "construct2", "construct3", "construct4", "extinct" };

        /// <summary>
        /// Is there anything to build at this position? True on open ground with a footing under
        /// it, and on a firepit that is not finished yet — false once the firepit stands.
        ///
        /// This is what makes <c>target firepit</c> stop applying the moment the firepit is done:
        /// the block on paper becomes invalid, evaluation falls through to the next one, and
        /// loading fuel is left to a block that says so. Without it the same block would go on
        /// shovelling grass and firewood into the finished firepit's slots.
        /// </summary>
        public static bool CanBuildAt(IWorldAccessor world, BlockPos pos)
        {
            if (world?.BlockAccessor == null || pos == null) return false;

            Block block = world.BlockAccessor.GetBlock(pos);
            if (block == null) return false;

            if (IsUnderConstruction(block)) return true;

            return block.Replaceable >= 6000 && GroundSupport.HasFooting(world, pos);
        }

        /// <summary>Is this block a firepit that is still being built?</summary>
        public static bool IsUnderConstruction(Block block)
        {
            return StageIndex(block) >= 0 && StageIndex(block) < Stages.Length - 1;
        }

        /// <summary>Index into <see cref="Stages"/>, or -1 when this is not a firepit being built.</summary>
        private static int StageIndex(Block block)
        {
            if (block?.Code == null || block.FirstCodePart() != FirepitCode) return -1;

            string state = block.Variant?[BurnStateVariant];
            if (state == null) return -1;

            // The finished states are not stages we build through.
            for (int i = 0; i < Stages.Length - 1; i++)
            {
                if (Stages[i] == state) return i;
            }
            return -1;
        }

        /// <summary>Which item the next step needs, or null when there is no next step here.</summary>
        public static string NeededItemCode(Block blockAtTarget)
        {
            if (blockAtTarget == null) return null;

            // Nothing there yet: the first step is laying the tinder.
            if (blockAtTarget.Replaceable >= 6000) return TinderItemCode;

            return StageIndex(blockAtTarget) >= 0 ? FuelItemCode : null;
        }

        /// <summary>Does this stack satisfy the next step at the target?</summary>
        public static bool Matches(Block blockAtTarget, ItemStack stack)
        {
            string needed = NeededItemCode(blockAtTarget);
            string code = stack?.Collectible?.Code?.Path;

            return needed != null && code != null && code == needed;
        }

        /// <summary>
        /// Lays the tinder, or moves the firepit on by one stage. Returns false when nothing at
        /// this position is waiting for that item.
        /// </summary>
        public static bool Advance(IWorldAccessor world, BlockPos pos, ItemStack stack)
        {
            Block current = world.BlockAccessor.GetBlock(pos);
            if (!Matches(current, stack)) return false;

            // Prefer the game's own construction step. Moving the stage on is only part of what it
            // does — a firepit completed on top of firewood in a sealed pit turns into a charcoal
            // pit, and that decision lives in the game, not in a variant code. It wants an IPlayer
            // for the parts a machine has no answer for, so a missing one is expected to fail here
            // rather than be worked around.
            if (current is BlockFirepit firepit)
            {
                try
                {
                    if (firepit.TryConstruct(world, pos, stack.Collectible, null)) return true;
                }
                catch (System.NullReferenceException)
                {
                    // Needed the player after all; fall through and set the block ourselves.
                }
            }

            string nextState = current.Replaceable >= 6000
                ? Stages[0]
                : Stages[StageIndex(current) + 1];

            Block next = world.GetBlock(new AssetLocation(FirepitCode + "-" + nextState));
            if (next == null) return false;

            world.BlockAccessor.SetBlock(next.BlockId, pos);

            // Setting a block is not the same as placing one, and the difference matters here:
            // whatever the firepit does when it lands somewhere — including noticing that it is
            // sitting on firewood in a sealed pit and becoming a charcoal pit — happens in
            // OnBlockPlaced. Without this the firepit is built but never converts.
            next.OnBlockPlaced(world, pos);

            world.BlockAccessor.MarkBlockDirty(pos);
            return true;
        }
    }
}
