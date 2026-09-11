using System.Collections.Generic;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.cargo
{
    /// <summary>One part of a holder that goods can be put into or taken out of.</summary>
    public interface ICargoHold
    {
        /// <summary>For diagnostics only. Nothing is selected by it.</summary>
        string Code { get; }

        IInventory Inventory { get; }

        /// <summary>Where it is, when it is a place in the world. Null for an inventory.</summary>
        BlockPos Pos { get; }

        /// <summary>
        /// Are the slots the goods themselves, so a transfer may write straight into them?
        /// The dock's crate is both a position and a container, so the two answers differ there.
        /// </summary>
        bool IsContainer => Pos == null;

        /// <summary>Asked before anything else, so it has to be cheap.</summary>
        bool IsEmpty => Inventory?.Empty ?? true;

        void MarkDirty();
    }

    /// <summary>The other party a device exchanges goods with: a train, a storage yard.</summary>
    public interface ICargoHolder
    {
        /// <summary>Ordered from the device outwards; the order is part of the contract.</summary>
        IReadOnlyList<ICargoHold> Holds { get; }

        /// <summary>Ready to exchange, which is not the same as present. A train has to have stopped.</summary>
        bool IsReady { get; }

        /// <summary>Drops whatever was read off the world, keeping the holder itself.</summary>
        void Refresh()
        {
        }
    }

    /// <summary>What a header said, in whatever form the finder that parsed it finds useful.</summary>
    public interface ICargoSelector
    {
    }

    /// <summary>Knows one kind of holder: how to read it out of a header, and how to find it.</summary>
    public interface ICargoHolderFinder
    {
        /// <summary>The word that picks this finder in a header: <c>yard</c>, <c>train</c>.</summary>
        string Keyword { get; }

        /// <summary>
        /// Reads the rest of the header. Must work with no game and no instance of anything, so
        /// that a typo can be reported while the paper is being written.
        /// </summary>
        bool TryParseHeader(IReadOnlyList<string> tokens, PaperErrorSink errors, out ICargoSelector selector);

        bool TryFind(IWorldAccessor world, BlockPos devicePos, ICargoSelector selector, out ICargoHolder holder);

        /// <summary>
        /// May a device hold on to what this found? False for anything that can drive away.
        /// </summary>
        bool Cacheable => true;
    }
}
