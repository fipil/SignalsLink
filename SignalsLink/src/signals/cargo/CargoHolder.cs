using System.Collections.Generic;
using SignalsLink.src.signals.paperConditions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.src.signals.cargo
{
    /// <summary>
    /// One part of a holder that goods can be put into or taken out of - a bay of a wagon, a
    /// column of piles on a paved tile.
    ///
    /// Deliberately not "endpoint": that word already belongs to the link system
    /// (<c>BlockLinkEndpointBase</c>), and holder / hold reads as a pair.
    /// </summary>
    public interface ICargoHold
    {
        /// <summary>Names this hold for diagnostics only. Nothing is ever selected by it.</summary>
        string Code { get; }

        /// <summary>
        /// What is in it, for the paper to READ. For some holders these are the live slots; for a
        /// column of ground piles it is a reading of what stands there.
        /// </summary>
        IInventory Inventory { get; }

        /// <summary>
        /// Where it is, when the hold is a place in the world rather than an inventory - a paved
        /// tile with goods stacked on it.
        ///
        /// This is the difference that decides how goods actually MOVE. A wagon has an inventory
        /// and slots can be written to; a stack of piles on the ground has to be built and taken
        /// apart block by block, which only the world transfers can do. A hold that names a
        /// position gets those, and everything they already know - growing a column, taking from
        /// the top of it, layered piles - comes with them.
        ///
        /// Null for an inventory-backed hold.
        /// </summary>
        BlockPos Pos { get; }

        /// <summary>
        /// Are the slots the goods themselves, so that a transfer may write straight into them?
        ///
        /// Usually the same question as <see cref="Pos"/> being null, and the default says so. The
        /// exception is a container that stands somewhere - the dock's own crate - which has both a
        /// position and slots of its own; a paved tile has a position and only a READING of the
        /// piles on it. Which of the two it is decides how a pair of holds moves goods.
        /// </summary>
        bool IsContainer => Pos == null;

        /// <summary>
        /// Nothing to take from here. Asked before anything else, so it has to be CHEAP - most of a
        /// yard is empty most of the time, and the honest answer means walking a column of block
        /// entities.
        /// </summary>
        bool IsEmpty => Inventory?.Empty ?? true;

        void MarkDirty();
    }

    /// <summary>
    /// The other party a device exchanges goods with: a train standing at the platform, a storage
    /// yard next to it.
    /// </summary>
    public interface ICargoHolder
    {
        /// <summary>The holds, ordered from the device outwards. The order is what fills the
        /// nearest wagon or the nearest column first, so it is part of the contract.</summary>
        IReadOnlyList<ICargoHold> Holds { get; }

        /// <summary>
        /// Ready to exchange goods - which is not the same as "present". A train has to have
        /// stopped; a storage yard is always ready.
        /// </summary>
        bool IsReady { get; }

        /// <summary>
        /// Drops whatever was read off the world, keeping the holder itself.
        ///
        /// Finding a holder is the expensive half and its answer keeps for a while; what is IN it
        /// changes with every transfer. A device therefore holds on to the instance and calls this
        /// each time round. Nothing to do for a holder that reads nothing ahead of time.
        /// </summary>
        void Refresh()
        {
        }
    }

    /// <summary>
    /// What a header said about which holder is meant, in whatever form the finder that parsed it
    /// finds useful. Nothing outside the finder ever looks inside.
    /// </summary>
    public interface ICargoSelector
    {
    }

    /// <summary>
    /// Knows one kind of holder: how to read it out of a section header, and how to find it in the
    /// world.
    ///
    /// The keyword belongs to the FINDER, not to a holder instance. The parser has to be able to
    /// judge <c>load yard</c> while reading the paper, when no instance exists anywhere - otherwise
    /// a typo cannot be reported as a paper error, and it must be.
    /// </summary>
    public interface ICargoHolderFinder
    {
        /// <summary>The word that picks this finder in a header: <c>yard</c>, <c>train</c>, ...</summary>
        string Keyword { get; }

        /// <summary>
        /// Reads the rest of the header. What <c>north</c> or <c>3</c> mean is known here and
        /// nowhere else, which is what keeps a new kind of vehicle a pure addition.
        ///
        /// Anything not understood goes into the same sink as every other paper error, so the
        /// player sees it on the right line.
        /// </summary>
        bool TryParseHeader(IReadOnlyList<string> tokens, PaperErrorSink errors, out ICargoSelector selector);

        /// <summary>Finds what the selector asked for, seen from the device at <paramref name="devicePos"/>.</summary>
        bool TryFind(IWorldAccessor world, BlockPos devicePos, ICargoSelector selector, out ICargoHolder holder);

        /// <summary>
        /// May a device hold on to what this found?
        ///
        /// True for something that does not move — paving stays where it was put, and searching for
        /// it every tick is waste. False for anything that can drive away: a train's inventories
        /// must not outlive the tick they were read in, and whether it has come to a stop can only
        /// be answered by looking afresh.
        /// </summary>
        bool Cacheable => true;
    }
}
