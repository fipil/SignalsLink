using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace SignalsLink.src.signals.paperConditions
{
    public static class PaperTextUtil
    {
        public static string GetPaperText(ItemStack stack)
        {
            return stack.Attributes?.GetString("text");
        }

        public static void SetPaperText(ItemStack stack, string text)
        {
            stack.Attributes ??= new TreeAttribute();
            stack.Attributes.SetString("text", text);
        }
    }

}
