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
    private readonly List<PaperConditionError> errors = new List<PaperConditionError>();

    /// <summary>Mistakes found in the current paper, with line numbers. Empty when it is clean.</summary>
    public IReadOnlyList<PaperConditionError> Errors
    {
        get
        {
            // Errors are filled while parsing, so a paper that has not been looked at yet would
            // report none at all - which is exactly when the player is asking.
            if (!string.IsNullOrWhiteSpace(conditionsText)
                && (compiled == null || !string.Equals(lastParsedText, conditionsText, StringComparison.Ordinal)))
            {
                ParseInternal(conditionsText);
            }

            return errors;
        }
    }

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

    /// <summary>True if the current conditions contain any <c>output</c> action (compiles if needed).</summary>
    public bool HasAnyOutput
    {
        get
        {
            if (string.IsNullOrWhiteSpace(conditionsText)) return false;
            if (compiled == null || !string.Equals(lastParsedText, conditionsText, StringComparison.Ordinal))
            {
                ParseInternal(conditionsText);
            }
            return compiled?.HasAnyOutput ?? false;
        }
    }

    /// <summary>
    /// The compiled blocks for the unified driver, or null when there is no paper at all.
    /// Compiles on demand, the same way every other entry point here does.
    /// </summary>
    public IReadOnlyList<ConditionBlock> GetBlocks()
    {
        if (string.IsNullOrWhiteSpace(conditionsText))
        {
            errors.Clear();
            compiled = null;
            lastParsedText = null;
            return null;
        }

        if (compiled == null || !string.Equals(lastParsedText, conditionsText, StringComparison.Ordinal))
        {
            ParseInternal(conditionsText);
        }

        return compiled?.Blocks;
    }

    /// <summary>
    /// One evaluation pass for a sensor: its whole purpose is the output signal, so every block
    /// counts as an output block even without an explicit <c>output</c> directive, and there is no
    /// action rail at all. Returns false when no block held, which means a pin of 0.
    ///
    /// The value can be the <c>output .</c> sentinel (255) - "the value this block computed" -
    /// which the caller resolves into whatever it measures.
    /// </summary>
    public bool RunSensorPass(ItemStack stack, IDictionary<string, object> ctx, out byte output)
    {
        output = 0;

        IReadOnlyList<ConditionBlock> blocks = GetBlocks();
        if (blocks == null || blocks.Count == 0) return false;

        DriverResult result = ConditionDriver.Run(
            blocks,
            true,                                       // a sensor has no action rail
            block => block.ConditionsHold(stack, ctx),
            null,
            everyBlockIsOutput: true);

        output = result.GetOutput();
        return result.HasOutput();
    }

    /// <summary>The paper's sections, or null when there is no paper at all.</summary>
    public IReadOnlyList<ConditionSection> GetSections()
    {
        if (string.IsNullOrWhiteSpace(conditionsText)) return null;

        if (compiled == null || !string.Equals(lastParsedText, conditionsText, StringComparison.Ordinal))
        {
            ParseInternal(conditionsText);
        }

        return compiled?.Sections;
    }

    /// <summary>True if the player wrote a section header.</summary>
    public bool HasExplicitSections
    {
        get
        {
            if (string.IsNullOrWhiteSpace(conditionsText)) return false;

            if (compiled == null || !string.Equals(lastParsedText, conditionsText, StringComparison.Ordinal))
            {
                ParseInternal(conditionsText);
            }

            return compiled?.HasExplicitSections ?? false;
        }
    }

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
        byte matchedBlockIndex;
        PaperConditionDirectives directives;
        return Evaluate(stack, ctx, out matchedBlockIndex, out directives);
    }

    /// <summary>
    /// Vyhodnotí aktuální conditionsText a vrátí i index/blokový výstup (viz parser).
    /// </summary>
    public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx, out byte matchedBlockIndex)
    {
        return Evaluate(stack, ctx, out matchedBlockIndex, out _);
    }

    public bool Evaluate(ItemStack stack, IDictionary<string, object> ctx, out byte matchedBlockIndex, out PaperConditionDirectives directives)
    {
        matchedBlockIndex = 0;
        directives = PaperConditionDirectives.Empty;

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

        return compiled.Evaluate(stack, ctx, out matchedBlockIndex, out directives);
    }

    public bool TryMatch(ItemStack stack, IDictionary<string, object> ctx, out PaperConditionMatchResult matchResult)
    {
        matchResult = PaperConditionMatchResult.NoMatch;

        if (string.IsNullOrWhiteSpace(conditionsText))
        {
            errors.Clear();
            compiled = null;
            lastParsedText = null;
            return false;
        }

        if (compiled == null || !string.Equals(lastParsedText, conditionsText, StringComparison.Ordinal))
        {
            ParseInternal(conditionsText);
        }

        if (compiled == null) return false;

        return compiled.TryMatch(stack, ctx, out matchResult);
    }

    public IReadOnlyList<IConditionAction> GetMatchingActions(ItemStack stack, IDictionary<string, object> ctx)
    {
        if (string.IsNullOrWhiteSpace(conditionsText))
        {
            return Array.Empty<IConditionAction>();
        }

        if (compiled == null || !string.Equals(lastParsedText, conditionsText, StringComparison.Ordinal))
        {
            ParseInternal(conditionsText);
        }

        if (compiled == null) return Array.Empty<IConditionAction>();

        return compiled.GetMatchingActions(stack, ctx);
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

        // For a sensor the watched block IS the target, so block-state conditions (isBurning)
        // resolve in either scope.
        ctx["world"] = api.World;
        ctx["blockPos"] = pos;
        ctx["targetBlockPos"] = pos;

        return RunSensorPass(dummyStack, ctx, out matchedBlockIndex);
    }

    private void ParseInternal(string text)
    {
        errors.Clear();
        lastParsedText = text;
        compiled = PaperConditionsParser.Parse(text, errors);
    }
}