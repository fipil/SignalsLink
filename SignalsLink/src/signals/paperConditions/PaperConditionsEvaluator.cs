using System;
using System.Collections.Generic;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;

public class PaperConditionsEvaluator
{
    private string conditionsText;
    private string lastParsedText;
    private CompiledConditions compiled;
    private readonly List<string> errors = new List<string>();

    public IReadOnlyList<string> Errors => errors;

    /// <summary>
    /// Nastaví nový conditions text. Pøi zmìnì invaliduje cache.
    /// </summary>
    public void SetConditionsText(string text)
    {
        // normalizuj null/whitespace
        text = string.IsNullOrWhiteSpace(text) ? null : text;

        if (string.Equals(conditionsText, text, StringComparison.Ordinal))
        {
            // stejný text, nic se nemìní
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
    /// Vyhodnotí aktuální conditionsText a vrátí i index prvního splnìného bloku (1..N, 0 = žádný).
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

    private void ParseInternal(string text)
    {
        errors.Clear();
        lastParsedText = text;
        compiled = PaperConditionsParser.Parse(text, errors);
    }
}