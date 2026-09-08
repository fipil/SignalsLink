using System;
using System.Collections.Generic;

namespace SignalsLink.src.signals.paperConditions
{
    /// <summary>
    /// What the driver needs to know about a condition block. Deliberately tiny: the driver
    /// decides the ORDER in which things happen, never what a condition means, so it sees no
    /// ItemStack and no IInventory and can be tested without a running game.
    /// </summary>
    public interface IDriverBlock
    {
        /// <summary>True if the block carries an <c>output</c> directive.</summary>
        bool IsOutputBlock { get; }

        /// <summary>
        /// Which output pin the block writes to. Every device has exactly one pin today, so this
        /// is always 0. It exists so that a device with more than one (EntitySensor) would not
        /// force a rewrite of the driver and of every test written against it.
        /// </summary>
        int OutputPin { get; }

        /// <summary>
        /// The value to report. 255 is the <c>output .</c> sentinel — "the value this block
        /// computed" — which the driver carries through untouched for the host to resolve.
        /// </summary>
        byte OutputValue { get; }
    }

    /// <summary>What one pass produced.</summary>
    public sealed class DriverResult
    {
        /// <summary>A pass that computed nothing: every pin reads 0 and no action ran.</summary>
        public static readonly DriverResult Nothing = new DriverResult(null, false);

        private readonly Dictionary<int, byte> outputs;

        /// <summary>True if some action block actually did work in this pass.</summary>
        public bool ActionPerformed { get; }

        internal DriverResult(Dictionary<int, byte> outputs, bool actionPerformed)
        {
            this.outputs = outputs;
            ActionPerformed = actionPerformed;
        }

        /// <summary>
        /// The value for a pin. A pin that no output block claimed reads 0 — that is the rule, not
        /// a fallback: the pin is a function of the current state, so nothing holding means zero.
        /// </summary>
        public byte GetOutput(int pin = 0)
        {
            return outputs != null && outputs.TryGetValue(pin, out byte value) ? value : (byte)0;
        }

        /// <summary>True if an output block claimed this pin during the pass.</summary>
        public bool HasOutput(int pin = 0)
        {
            return outputs != null && outputs.ContainsKey(pin);
        }
    }

    /// <summary>
    /// The single evaluation pass shared by every device (see paper-conditions-rules-v2.md).
    ///
    /// One walk over the blocks in the order they stand on the paper, carrying two rails at once:
    /// the output rail decides the pin, the action rail moves things. That both rails are walked
    /// together is the point — an action changes the state of the target, so the same output block
    /// answers differently above an action than below it, and it is the player who decides where
    /// to write it.
    /// </summary>
    public static class ConditionDriver
    {
        /// <param name="blocks">The compiled paper, in the order it was written.</param>
        /// <param name="actionsBlocked">
        /// Closes the action rail for this pass: backoff, no arbitration token, no signal credit.
        /// It does NOT stop the pass — that is what makes the pin behave like a sensor.
        /// </param>
        /// <param name="outputHolds">Do this output block's conditions hold?</param>
        /// <param name="tryAct">
        /// Run this action block. Returns whether it actually did work — a block whose transfer
        /// moves nothing (source empty, target full, <c>ifEmpty</c> no longer true) must fall
        /// through to the next block, which is what a chain of <c>target N ifEmpty</c> blocks is
        /// built on.
        /// </param>
        /// <param name="everyBlockIsOutput">
        /// The BlockSensor exception: its whole purpose is the output signal, so a block without
        /// an explicit <c>output</c> is an output block too and it has no action rail at all.
        /// </param>
        public static DriverResult Run<TBlock>(
            IReadOnlyList<TBlock> blocks,
            bool actionsBlocked,
            Func<TBlock, bool> outputHolds,
            Func<TBlock, bool> tryAct,
            bool everyBlockIsOutput = false) where TBlock : IDriverBlock
        {
            if (blocks == null || blocks.Count == 0) return DriverResult.Nothing;

            // Nothing to compute and nothing to do: no output block on the paper and the action
            // rail closed. This is the one case where the pass is skipped for performance; with an
            // output block on the paper it runs on every tick, whatever the input pin says.
            if (actionsBlocked && !HasAnyOutputBlock(blocks, everyBlockIsOutput)) return DriverResult.Nothing;

            Dictionary<int, byte> outputs = null;
            bool actionPerformed = false;

            for (int i = 0; i < blocks.Count; i++)
            {
                TBlock block = blocks[i];

                if (everyBlockIsOutput || block.IsOutputBlock)
                {
                    // First one wins, per pin. Later output blocks for the same pin are not even
                    // asked, so a paper reads top-down like a list of rules.
                    if (outputs != null && outputs.ContainsKey(block.OutputPin)) continue;
                    if (outputHolds == null || !outputHolds(block)) continue;

                    outputs ??= new Dictionary<int, byte>();
                    outputs[block.OutputPin] = block.OutputValue;
                    continue;
                }

                if (actionsBlocked) continue;
                if (tryAct == null || !tryAct(block)) continue;

                // Only a block that really did work closes the rail. An attempt does not.
                actionPerformed = true;
                actionsBlocked = true;
            }

            return new DriverResult(outputs, actionPerformed);
        }

        private static bool HasAnyOutputBlock<TBlock>(IReadOnlyList<TBlock> blocks, bool everyBlockIsOutput)
            where TBlock : IDriverBlock
        {
            if (everyBlockIsOutput) return true;

            for (int i = 0; i < blocks.Count; i++)
            {
                if (blocks[i].IsOutputBlock) return true;
            }

            return false;
        }
    }
}
