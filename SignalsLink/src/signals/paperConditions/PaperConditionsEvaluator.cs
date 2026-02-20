using System;
using System.Collections.Generic;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

public class PaperConditionsEvaluator
{
    private string conditionsText;
    private string lastParsedText;
    private CompiledConditions compiled;
    private readonly List<string> errors = new List<string>();

    public IReadOnlyList<string> Errors => errors;

    /// <summary>
    /// Nastaví nový conditions text. Při změně invaliduje cache.
    /// </summary>
    public void SetConditionsText(string text)
    {
        // normalizuj null/whitespace
        text = string.IsNullOrWhiteSpace(text) ? null : text;

        if (string.Equals(conditionsText, text, StringComparison.Ordinal))
        {
            // stejný text, nic se nemění
            return;
        }

        conditionsText = text;
        Invalidate();
    }

    public bool HasConditions => !string.IsNullOrWhiteSpace(conditionsText);

    public void ClearCache()
    {
        lastParsedText = null;
        compiled = null;
        errors.Clear();
    }

    public void Invalidate()
    {
        ClearCache();
    }

    /// <summary>
    /// Vyhodnotí aktuální conditionsText pro daný stack a ctx.
    /// </summary>
    public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx = null)
    {
        byte _;
        return Evaluate(stack, ctx, out _);
    }

    /// <summary>
    /// Vyhodnotí aktuální conditionsText a vrátí i index/blokový výstup (viz parser).
    /// </summary>
    public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx, out byte matchedBlockIndex)
    {
        matchedBlockIndex = 0;

        if (string.IsNullOrWhiteSpace(conditionsText))
        {
            errors.Clear();
            compiled = null;
            lastParsedText = null;
            // žádné podmínky -> všechno projde, ale nemáme konkrétní blok
            return true;
        }

        if (compiled == null || !string.Equals(lastParsedText, conditionsText, StringComparison.Ordinal))
        {
            ParseInternal(conditionsText);
        }

        if (compiled == null) return false;

        return compiled.Evaluate(stack, ctx, out matchedBlockIndex);
    }

    /// <summary>
    /// Vyhodnotí aktuální conditionsText pro blok na dané pozici.
    /// Vytvoří syntetický ItemStack z bloku, aby bylo možné používat code/glob/regex
    /// podmínky i pro bloky. Další stav bloku může být předán přes ctx.
    /// </summary>
    public bool Evaluate(ICoreAPI api, BlockPos pos)
    {
        byte _;
        return Evaluate(api, pos, out _);
    }

    /// <summary>
    /// Vyhodnotí aktuální conditionsText pro blok na dané pozici a vrátí i výstup.
    /// Do ctx doplní základní aliasy pro block code.
    /// </summary>
    public bool Evaluate(ICoreAPI api, BlockPos pos, out byte matchedBlockIndex)
    {
        matchedBlockIndex = 0;

        if (api == null || pos == null)
        {
            return false;
        }

        var block = api.World.BlockAccessor.GetBlock(pos);
        if (block == null)
        {
            return false;
        }

        // Syntetický stack jen kvůli CodeGlob/CodeRegex podmínkám
        var dummyStack = new ItemStack(block);

        var ctx = ItemConditionContextUtil.BuildContext(api.World, dummyStack);

        // Pokud volající nepřipravil vlastní ctx, použij prázdný slovník
        if (ctx == null)
        {
            ctx = new Dictionary<string, object>();
        }

        return Evaluate(dummyStack, ctx, out matchedBlockIndex);
    }

    private void ParseInternal(string text)
    {
        errors.Clear();
        lastParsedText = text;
        compiled = PaperConditionsParser.Parse(text, errors);
    }
}