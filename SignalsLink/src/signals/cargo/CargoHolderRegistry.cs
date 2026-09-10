using System;
using System.Collections.Generic;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;

namespace SignalsLink.src.signals.cargo
{
    /// <summary>
    /// What a section header asked for: which finder, and what it made of the rest of the line.
    /// </summary>
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
    /// The kinds of holder this game knows about.
    ///
    /// A registry rather than an enumeration, because the interesting ones live in other mods: the
    /// base registers the storage yard, a bridge mod adds trains, and neither knows about the
    /// other. Resolving a header is therefore a lookup, never a switch.
    ///
    /// It is a ModSystem so that a mod can reach it with <c>api.ModLoader.GetModSystem</c>, but it
    /// holds nothing that needs a running game - the parsing half can be exercised on its own.
    /// </summary>
    public class CargoHolderRegistry : ModSystem
    {
        private readonly List<ICargoHolderFinder> finders = new List<ICargoHolderFinder>();

        public override bool ShouldLoad(EnumAppSide forSide) => true;

        public IReadOnlyList<ICargoHolderFinder> Finders => finders;

        /// <summary>
        /// Adds a finder, or replaces the one that already answers to that keyword. Reloading a mod
        /// must not end up with the same kind of holder registered twice.
        /// </summary>
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
        /// Works out which holder a section header means.
        ///
        /// A missing keyword falls back to the only registered finder - with one kind of holder in
        /// the game there is nothing to say. With several it is a paper error rather than a guess:
        /// picking one for the player would be picking the wrong one half the time.
        /// </summary>
        /// <summary>
        /// Works out which holder one END of a section header means.
        ///
        /// One end, not a section: a header names two of them, and each is resolved on its own.
        /// <paramref name="header"/> is only what an error quotes back.
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
                // No keyword - or a first token that is part of the specification rather than a
                // keyword, which cannot be told apart and does not need to be while there is only
                // one finder to hand it to.
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
