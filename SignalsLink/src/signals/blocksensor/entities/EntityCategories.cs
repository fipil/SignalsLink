using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

public static class EntityClassifier
{
    // Temporal / „creatures“
    private static readonly string[] CreaturePrefixes =
    {
        "drifter-",
        "locust-",
        "shiver-",
        "bowtorn-",
        "bell-"      // bell-normal apod.
    };

    // Všechna zvířata (domácí i divoká)
    private static readonly string[] AnimalPrefixes =
    {
        // domestikovatelná/farm
        "chicken-",
        "sheep-",
        "pig-",
        "cow-",
        "bison-",
        "yak-",
        "goat-",

        // divoká
        "wolf-",
        "fox-",
        "hyena-",
        "bear-",
        "hare-",
        "gazelle-",
        "deer-",
        "raccoon-",
        "boar-",
        "crab-",
        "salmon",
        "grub"
    };

    // Divoká zvířata (podmnožina animals, jen „wild“)
    private static readonly string[] WildAnimalPrefixes =
    {
        "wolf-",
        "fox-",
        "hyena-",
        "bear-",
        "hare-",
        "gazelle-",
        "deer-",
        "raccoon-",
        "boar-",
        "crab-",
        "salmon",
        "grub",
        // případně i „pig-wild-“ atd., když chceš
        "pig-wild-"
    };

    private static string GetPath(Entity entity)
    {
        if (entity is EntityAgent agent && agent.Code != null)
        {
            // např. "drifter-normal", "wolf-male", "chicken-rooster"
            return agent.Code.Path ?? string.Empty;
        }
        return string.Empty;
    }

    private static bool MatchesPrefix(string path, string[] prefixes)
    {
        if (string.IsNullOrEmpty(path)) return false;

        for (int i = 0; i < prefixes.Length; i++)
        {
            if (path.StartsWith(prefixes[i], StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    // ====== Public API ======

    /// <summary>Temporal bytost (drifter, locust, shiver, bowtorn, bell…)</summary>
    public static bool IsCreature(Entity entity)
    {
        if (!(entity is EntityAgent)) return false;
        return MatchesPrefix(GetPath(entity), CreaturePrefixes);
    }

    /// <summary>Jakékoliv zvíře (domácí i divoké).</summary>
    public static bool IsAnimal(Entity entity)
    {
        if (!(entity is EntityAgent)) return false;
        return MatchesPrefix(GetPath(entity), AnimalPrefixes);
    }

    /// <summary>Divoké zvíře / zvěř.</summary>
    public static bool IsWildAnimal(Entity entity)
    {
        if (!(entity is EntityAgent)) return false;
        return MatchesPrefix(GetPath(entity), WildAnimalPrefixes);
    }

    /// <summary>Hráč.</summary>
    public static bool IsPlayer(Entity entity)
    {
        return entity is EntityPlayer;
    }
}
