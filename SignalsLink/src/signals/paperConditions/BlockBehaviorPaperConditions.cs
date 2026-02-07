using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
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
                string text = ItemConditionContextUtil.BuildHintText(world, stack);
                if (string.IsNullOrWhiteSpace(text)) return false;

                be.ConditionsText = text;
                handling = EnumHandling.PreventDefault;
                return true;
            }

            return false;
        }

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

            foreach (var line in escaped.Split('\n'))
            {
                dsc.AppendLine("  " + line);
            }
            if (dsc.Length > 0)
            {
                dsc.Insert(0, $"{Lang.Get("signalslink:managedchute-conditions")}:\n");
                return dsc.ToString();
            }
            return null;
        }

        private bool IsPaper(ItemStack stack)
        {
            // TODO: adjust to your paper item code
            return stack.Collectible.Code.Path.Contains("paper");
        }
    }

}