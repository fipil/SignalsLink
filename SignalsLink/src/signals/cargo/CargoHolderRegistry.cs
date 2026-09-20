using System;
using System.Collections.Generic;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;

namespace SignalsLink.src.signals.cargo
{
    /// <summary>What a section header asked for: which finder, and what it made of the rest.</summary>
    public sealed class CargoHolderRequest
    {
        public ICargoHolderFinder Finder { get; }
        public ICargoSelector Selector { get; }

        public CargoHolderRequest(ICargoHolderFinder finder, ICargoSelector selector)
        {
            Finder = finder;
            Selector = selector;
        }
    }

    /// <summary>
    /// The kinds of holder this game knows about. A registry rather than an enumeration, because
    /// the interesting ones live in other mods.
    /// </summary>
    public class CargoHolderRegistry : ModSystem
    {
        private readonly List<ICargoHolderFinder> finders = new List<ICargoHolderFinder>();

        public override bool ShouldLoad(EnumAppSide forSide) => true;

        public IReadOnlyList<ICargoHolderFinder> Finders => finders;

        /// <summary>Adds a finder, or replaces the one already answering to that keyword.</summary>
        public void Register(ICargoHolderFinder finder)
        {
            if (finder == null || string.IsNullOrEmpty(finder.Keyword)) return;

            finders.RemoveAll(f => f.Keyword.Equals(finder.Keyword, StringComparison.OrdinalIgnoreCase));
            finders.Add(finder);
        }

        public ICargoHolderFinder Get(string keyword)
        {
            if (string.IsNullOrEmpty(keyword)) return null;

            foreach (ICargoHolderFinder finder in finders)
            {
                if (finder.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase)) return finder;
            }

            return null;
        }

        /// <summary>
        /// Works out which holder one END of a section header means. <paramref name="header"/> is
        /// only what an error quotes back.
        /// </summary>
        public bool TryResolve(IReadOnlyList<string> tokens, string header, PaperErrorSink errors, out CargoHolderRequest request)
        {
            request = null;
            if (tokens == null) return false;

            ICargoHolderFinder finder = tokens.Count > 0 ? Get(tokens[0]) : null;

            IReadOnlyList<string> rest;

            if (finder != null)
            {
                rest = Skip(tokens, 1);
            }
            else if (finders.Count == 1)
            {
                // No keyword, or a first token that is part of the specification. The two cannot be
                // told apart and need not be while there is only one finder.
                finder = finders[0];
                rest = tokens;
            }
            else
            {
                errors?.Add(header, finders.Count == 0 ? "holdernone" : "holderunknown");
                return false;
            }

            if (!finder.TryParseHeader(rest, errors, out ICargoSelector selector)) return false;

            request = new CargoHolderRequest(finder, selector);
            return true;
        }

        private static IReadOnlyList<string> Skip(IReadOnlyList<string> tokens, int count)
        {
            List<string> rest = new List<string>();
            for (int i = count; i < tokens.Count; i++) rest.Add(tokens[i]);
            return rest;
        }
    }
}
