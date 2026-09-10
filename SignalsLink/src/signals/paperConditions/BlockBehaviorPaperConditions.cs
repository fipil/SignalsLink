using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.paperConditions
{
    // ============================================================
    // BlockEntity mixin – blocks using this behavior MUST inherit
    // or delegate storage to something equivalent
    // ============================================================
    public interface IPaperConditionsHost
    {
        string ConditionsText { get; set; }
        int SignalInputsCount { get; }

        /// <summary>
        /// True for a device that picks what it carries out of a source inventory, and therefore
        /// needs every transfer block to say WHAT to carry. False where there is nothing to pick:
        /// a valve whose source is the far end of a hose, or a sensor, which carries nothing at
        /// all. It decides only whether a block without a source-scoped condition is reported as
        /// a mistake in the paper.
        /// </summary>
        bool RequiresTransferSelector => true;

        /// <summary>
        /// True for a device that has two ends of its own and therefore needs the player to say
        /// which way a block is going — a header of <c>unload</c> or <c>load</c>. False everywhere
        /// else, where a header would have nothing to mean and is reported as a mistake.
        /// </summary>
        bool SupportsSections => false;

        /// <summary>
        /// True for a device where a block without a header has nowhere to go: it knows two ends
        /// and neither of them is the obvious one. Reading such a paper as <c>unload</c> would be a
        /// silent guess at what the player meant, which is exactly the kind of thing nobody ever
        /// debugs.
        /// </summary>
        bool RequiresSections => false;
    }


    // ============================================================
    // BlockBehavior – interaction + tooltip glue
    // ============================================================
    public class BlockBehaviorPaperConditions : BlockBehavior
    {
        public BlockBehaviorPaperConditions(Block block) : base(block) { }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref EnumHandling handling)
        {
            // Let base / other behaviors do their thing first
            base.OnBlockInteractStart(world, byPlayer, blockSel, ref handling);

            // If already handled/prevented, don't do anything here
            if (handling != EnumHandling.PassThrough)
            {
                return false;
            }

            var be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as IPaperConditionsHost;
            if (be == null) return false;

            var slot = byPlayer.InventoryManager.ActiveHotbarSlot;
            if (slot?.Itemstack == null) return false;

            var stack = slot.Itemstack;
            bool sneaking = byPlayer.Entity.Controls.ShiftKey;
            bool control = byPlayer.Entity.Controls.CtrlKey;

            // 1) Paper interaction
            if (IsPaper(stack))
            {
                string paperText = PaperTextUtil.GetPaperText(stack);

                // Shift + empty paper = clear
                if (string.IsNullOrWhiteSpace(paperText) && sneaking)
                {
                    be.ConditionsText = null;
                    slot.MarkDirty();
                    handling = EnumHandling.PreventDefault;
                    return true;
                }

                // Non-empty paper -> store conditions
                if (!string.IsNullOrWhiteSpace(paperText))
                {
                    be.ConditionsText = paperText;
                    slot.MarkDirty();
                    ReportPaperErrors(world, byPlayer, paperText, be);
                    handling = EnumHandling.PreventDefault;
                    return true;
                }

                // Empty paper, NOT sneaking = copy out
                if (string.IsNullOrWhiteSpace(paperText) && !sneaking && !string.IsNullOrWhiteSpace(be.ConditionsText))
                {
                    PaperTextUtil.SetPaperText(stack, be.ConditionsText!);
                    slot.MarkDirty();
                    handling = EnumHandling.PreventDefault;
                    return true;
                }

                return false;
            }

            // 2) Other item + Ctrl = export attributes into ConditionsText
            if (control)
            {
                // ...except a wrench: there Ctrl means "cycle this block's mode" (see
                // WrenchActionsBehavior), and overwriting the conditions instead would be both
                // surprising and destructive. Asking for the behavior rather than for an item code
                // keeps every wrench material covered, and anything else we patch it onto later.
                if (stack.Collectible?.GetBehavior<WrenchActionsBehavior>() != null) return false;

                string text = ItemConditionContextUtil.BuildHintText(world, stack);
                if (string.IsNullOrWhiteSpace(text)) return false;

                be.ConditionsText = text;
                handling = EnumHandling.PreventDefault;
                return true;
            }

            return false;
        }

        /// <summary>
        /// The mistakes in a paper, or an empty list. Parsed on the spot: this is asked for by a
        /// player looking at a block, not on a tick.
        /// </summary>
        public static IReadOnlyList<PaperConditionError> FindErrors(string conditionsText, IPaperConditionsHost host)
        {
            if (string.IsNullOrWhiteSpace(conditionsText)) return Array.Empty<PaperConditionError>();

            var errors = new List<PaperConditionError>();
            CompiledConditions compiled = PaperConditionsParser.Parse(conditionsText, errors);

            AddBlocksWithNothingToSelect(compiled, host, errors);
            AddUnsupportedSections(compiled, host, errors);
            AddMissingSections(compiled, host, errors);
            AddHeadersNamingNoEnd(compiled, host, errors);
            return errors;
        }

        /// <summary>
        /// A transfer block has to say what to carry. One built only from `in target` conditions
        /// never picks anything, so it quietly does nothing forever — which is the hardest kind of
        /// mistake to find, because the paper looks perfectly reasonable.
        ///
        /// Only reported where it is actually a mistake: a valve needs no selector (the far end of
        /// the hose is its source) and a sensor carries nothing, so both are left alone.
        /// </summary>
        /// <summary>
        /// A header on a device that has only one way to move things says nothing, so it is
        /// reported rather than ignored - an ignored header would look like it was doing something.
        /// </summary>
        private static void AddUnsupportedSections(CompiledConditions compiled, IPaperConditionsHost host, List<PaperConditionError> errors)
        {
            if (compiled == null || host == null || host.SupportsSections) return;
            if (!compiled.HasExplicitSections) return;

            foreach (ConditionSection section in compiled.Sections)
            {
                if (section.IsImplicit) continue;
                errors.Add(new PaperConditionError(section.FirstLine, section.Header, "sectionunsupported"));
            }
        }

        /// <summary>
        /// The other way round: a device that knows two ends cannot do anything with a block that
        /// does not say which way it goes. Reported once, on the first such block, rather than on
        /// every one of them - the paper has one thing wrong with it, not five.
        /// </summary>
        private static void AddMissingSections(CompiledConditions compiled, IPaperConditionsHost host, List<PaperConditionError> errors)
        {
            if (compiled == null || host == null || !host.RequiresSections) return;
            if (compiled.HasExplicitSections || compiled.Blocks.Count == 0) return;

            errors.Add(new PaperConditionError(compiled.Blocks[0].FirstLine, "", "sectionmissing"));
        }

        /// <summary>
        /// A header has to name at least one other party. <c>unload</c> on its own, or <c>from</c>
        /// with nothing after <c>to</c>, says "from me to me" — which is nothing at all, and would
        /// otherwise sit there doing nothing with no explanation.
        /// </summary>
        private static void AddHeadersNamingNoEnd(CompiledConditions compiled, IPaperConditionsHost host, List<PaperConditionError> errors)
        {
            if (compiled == null || host == null || !host.SupportsSections) return;

            foreach (ConditionSection section in compiled.Sections)
            {
                if (section.IsImplicit || section.EndsAreComplete) continue;

                errors.Add(new PaperConditionError(section.FirstLine, section.Header, "sectionends"));
            }
        }

        private static void AddBlocksWithNothingToSelect(CompiledConditions compiled, IPaperConditionsHost host, List<PaperConditionError> errors)
        {
            if (compiled == null || host == null || !host.RequiresTransferSelector) return;

            foreach (ConditionBlock block in compiled.Blocks)
            {
                if (block.IsOutputBlock) continue;   // an output block carries nothing anyway
                if (block.CanSelectSource) continue;

                errors.Add(new PaperConditionError(block.FirstLine, "", "noselector"));
            }
        }

        /// <summary>
        /// Says what is wrong the moment the paper goes on the block. Without this the player finds
        /// out by watching a device do nothing, which is the worst possible way to be told about a
        /// typo. The paper is still accepted - only the bad lines never hold.
        /// </summary>
        private static void ReportPaperErrors(IWorldAccessor world, IPlayer byPlayer, string conditionsText, IPaperConditionsHost be)
        {
            IReadOnlyList<PaperConditionError> errors = FindErrors(conditionsText, be);
            if (errors.Count == 0) return;

            var message = new StringBuilder(Lang.Get("signalslink:papererror-header"));
            for (int i = 0; i < errors.Count && i < MaxReportedErrors; i++)
            {
                message.Append(' ').Append(errors[i].Describe());
            }

            if (errors.Count > MaxReportedErrors)
            {
                message.Append(' ').Append(Lang.Get("signalslink:papererror-more", errors.Count - MaxReportedErrors));
            }

            if (world.Api is ICoreServerAPI sapi && byPlayer is IServerPlayer serverPlayer)
            {
                sapi.SendIngameError(serverPlayer, "papercondition", message.ToString());
            }
            else if (world.Api is ICoreClientAPI capi)
            {
                capi.TriggerIngameError(capi, "papercondition", message.ToString());
            }
        }

        /// <summary>
        /// And keeps saying it, at the very top of the block info, so it is still findable long
        /// after the message that appeared when the paper went on.
        /// </summary>
        private static void AppendPaperErrors(StringBuilder dsc, string conditionsText, IPaperConditionsHost host)
        {
            IReadOnlyList<PaperConditionError> errors = FindErrors(conditionsText, host);
            if (errors.Count == 0) return;

            dsc.AppendLine("<font color=\"#ff8080\">" + Lang.Get("signalslink:papererror-header") + "</font>");

            for (int i = 0; i < errors.Count && i < MaxReportedErrors; i++)
            {
                dsc.AppendLine("<font color=\"#ff8080\">  " + errors[i].Describe().Replace("<", "&lt;").Replace(">", "&gt;") + "</font>");
            }

            if (errors.Count > MaxReportedErrors)
            {
                dsc.AppendLine("<font color=\"#ff8080\">  " + Lang.Get("signalslink:papererror-more", errors.Count - MaxReportedErrors) + "</font>");
            }
        }

        private const int MaxReportedErrors = 4;

        public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
        {
            var be = world.BlockAccessor.GetBlockEntity(pos) as IPaperConditionsHost;
            if (string.IsNullOrWhiteSpace(be?.ConditionsText)) return "";

            StringBuilder dsc = new StringBuilder();

            var sel = forPlayer?.CurrentBlockSelection;
            if (sel?.SelectionBoxIndex < be.SignalInputsCount)
            {
                return null;
            }

            // Escape < and > for VS rich text so they don't look like tags
            var escaped = be.ConditionsText
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");

            string[] lines = escaped.Split('\n');

            AppendPaperErrors(dsc, be.ConditionsText, be);

            dsc.AppendLine($"{Lang.Get("signalslink:managedchute-conditions")}:");

            // A long conditions text grows the block-info box down over the crosshair, which blocks
            // interacting with the block. So collapse to the first few lines unless the player is
            // sneaking; sneaking expands to the full text on demand.
            bool sneaking = forPlayer?.Entity?.Controls?.ShiftKey == true;
            const int MaxCollapsedLines = 8;

            if (!sneaking && lines.Length > MaxCollapsedLines)
            {
                for (int i = 0; i < MaxCollapsedLines; i++) dsc.AppendLine("  " + lines[i]);
                dsc.AppendLine("  " + Lang.Get("signalslink:conditions-collapsed", lines.Length - MaxCollapsedLines));
            }
            else
            {
                foreach (var line in lines) dsc.AppendLine("  " + line);
            }

            return dsc.ToString();
        }

        private bool IsPaper(ItemStack stack)
        {
            // TODO: adjust to your paper item code
            return stack.Collectible.Code.Path.Contains("paper");
        }
    }

}