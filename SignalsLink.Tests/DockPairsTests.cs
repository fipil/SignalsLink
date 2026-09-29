using System.Collections.Generic;
using System.Linq;
using SignalsLink.src.signals.cargo;
using SignalsLink.src.signals.manageddock;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.Tests
{
    /// <summary>
    /// Which part of one holder is tried against which part of the other, and in what order.
    ///
    /// With a holder at BOTH ends there is no longer one obvious pair to work on, and this is where
    /// it would quietly go wrong: the wrong order gives half-filled columns all over the yard, and
    /// no ceiling at all gives a tick that walks four hundred columns times ten wagons.
    /// </summary>
    public class DockPairsTests
    {
        [Fact]
        public void The_nearest_source_is_emptied_into_the_nearest_target_first()
        {
            // Source-major: it fills the near column to its height before starting another, and it
            // empties the near wagon before reaching into a distant one.
            var pairs = Pairs(sources: 2, targets: 2);

            Assert.Equal(new[] { "s0>t0", "s0>t1", "s1>t0", "s1>t1" }, pairs);
        }

        [Fact]
        public void An_empty_source_is_skipped_without_being_tried()
        {
            // Most of a yard is empty most of the time, and asking a column what is in it means
            // walking up it block by block.
            var sources = new List<ICargoHold> { Hold("s0", empty: true), Hold("s1", empty: false) };
            var targets = new List<ICargoHold> { Hold("t0", empty: true) };

            Assert.Equal(new[] { "s1>t0" }, Names(DockPairs.Order(sources, targets, 100)));
        }

        [Fact]
        public void An_empty_target_is_still_tried()
        {
            // Empty is exactly where goods want to go.
            var sources = new List<ICargoHold> { Hold("s0", empty: false) };
            var targets = new List<ICargoHold> { Hold("t0", empty: true) };

            Assert.Equal(new[] { "s0>t0" }, Names(DockPairs.Order(sources, targets, 100)));
        }

        [Fact]
        public void Nothing_to_take_from_means_no_pairs_at_all()
        {
            var sources = new List<ICargoHold> { Hold("s0", empty: true), Hold("s1", empty: true) };

            Assert.Empty(DockPairs.Order(sources, new List<ICargoHold> { Hold("t0", empty: true) }, 100));
        }

        [Fact]
        public void Nowhere_to_put_it_means_no_pairs_at_all()
        {
            var sources = new List<ICargoHold> { Hold("s0", empty: false) };

            Assert.Empty(DockPairs.Order(sources, new List<ICargoHold>(), 100));
        }

        [Fact]
        public void The_work_one_tick_may_do_is_capped()
        {
            // A yard of four hundred columns against a ten wagon train is four thousand attempts.
            // A tick has to come to an end whether or not anything was found.
            var sources = Enumerable.Range(0, 20).Select(i => Hold("s" + i, empty: false)).ToList<ICargoHold>();
            var targets = Enumerable.Range(0, 20).Select(i => Hold("t" + i, empty: false)).ToList<ICargoHold>();

            Assert.Equal(32, DockPairs.Order(sources, targets, 32).Count());
        }

        [Fact]
        public void The_cap_counts_attempts_not_sources()
        {
            var sources = Enumerable.Range(0, 5).Select(i => Hold("s" + i, empty: false)).ToList<ICargoHold>();
            var targets = Enumerable.Range(0, 5).Select(i => Hold("t" + i, empty: false)).ToList<ICargoHold>();

            var pairs = Names(DockPairs.Order(sources, targets, 7)).ToArray();

            Assert.Equal(7, pairs.Length);
            Assert.Equal("s0>t0", pairs[0]);
            Assert.Equal("s1>t1", pairs[6]);   // five targets for s0, then two for s1
        }

        [Fact]
        public void Skipped_sources_do_not_eat_the_cap()
        {
            // Otherwise a yard whose near end is empty would spend every tick walking past it.
            var sources = new List<ICargoHold>();
            for (int i = 0; i < 50; i++) sources.Add(Hold("s" + i, empty: i < 49));
            var targets = new List<ICargoHold> { Hold("t0", empty: false) };

            Assert.Equal(new[] { "s49>t0" }, Names(DockPairs.Order(sources, targets, 4)));
        }

        // ---------------------------------------------------------------- plumbing

        private static string[] Pairs(int sources, int targets)
        {
            var s = Enumerable.Range(0, sources).Select(i => Hold("s" + i, empty: false)).ToList<ICargoHold>();
            var t = Enumerable.Range(0, targets).Select(i => Hold("t" + i, empty: false)).ToList<ICargoHold>();

            return Names(DockPairs.Order(s, t, 100)).ToArray();
        }

        private static IEnumerable<string> Names(IEnumerable<(ICargoHold Source, ICargoHold Target)> pairs)
        {
            return pairs.Select(p => p.Source.Code + ">" + p.Target.Code);
        }

        private static ICargoHold Hold(string code, bool empty)
        {
            return new FakeHold(code, empty);
        }

        private sealed class FakeHold : ICargoHold
        {
            private readonly bool empty;

            public FakeHold(string code, bool empty)
            {
                Code = code;
                this.empty = empty;
            }

            public string Code { get; }
            public IInventory Inventory => null;
            public BlockPos Pos => null;
            public bool IsEmpty => empty;
            public void MarkDirty() { }
        }
    }
}
