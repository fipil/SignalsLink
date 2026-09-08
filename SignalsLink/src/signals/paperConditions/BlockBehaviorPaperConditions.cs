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
                    ReportPaperErrors(world, byPlayer, paperText);
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
        private static IReadOnlyList<PaperConditionError> FindErrors(string conditionsText)
        {
            if (string.IsNullOrWhiteSpace(conditionsText)) return Array.Empty<PaperConditionError>();

            var errors = new List<PaperConditionError>();
            PaperConditionsParser.Parse(conditionsText, errors);
            return errors;
        }

        /// <summary>
        /// Says what is wrong the moment the paper goes on the block. Without this the player finds
        /// out by watching a device do nothing, which is the worst possible way to be told about a
        /// typo. The paper is still accepted - only the bad lines never hold.
        /// </summary>
        private static void ReportPaperErrors(IWorldAccessor world, IPlayer byPlayer, string conditionsText)
        {
            IReadOnlyList<PaperConditionError> errors = FindErrors(conditionsText);
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
        private static void AppendPaperErrors(StringBuilder dsc, string conditionsText)
        {
            IReadOnlyList<PaperConditionError> errors = FindErrors(conditionsText);
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

            AppendPaperErrors(dsc, be.ConditionsText);

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