using System.Collections.Generic;
using System.Linq;
using SignalsLink.src.signals.cargo;
using SignalsLink.src.signals.paperConditions;
using SignalsLink.src.signals.yard;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SignalsLink.Tests
{
    /// <summary>
    /// The layer between a section header and the thing at the other end of the exchange.
    ///
    /// Only the half that can be reasoned about without a world is tested here: which finder a
    /// header picks and what it makes of the rest of the line. Finding the yard itself needs blocks
    /// in a world and belongs to the rig.
    /// </summary>
    public class CargoHolderTests
    {
        [Fact]
        public void A_keyword_picks_its_finder_and_the_rest_of_the_line_goes_to_it()
        {
            var registry = new CargoHolderRegistry();
            var yard = new SpyFinder("yard");
            registry.Register(yard);
            registry.Register(new SpyFinder("train"));

            Assert.True(registry.TryResolve(Tokens("yard north pile"), "load yard north pile", Sink(out _), out CargoHolderRequest request));

            Assert.Same(yard, request.Finder);
            Assert.Equal(new[] { "north", "pile" }, yard.SeenTokens);
        }

        [Fact]
        public void With_one_kind_of_holder_the_keyword_can_be_left_out()
        {
            // There is nothing to say while only one thing in the game can be loaded.
            var registry = new CargoHolderRegistry();
            var only = new SpyFinder("yard");
            registry.Register(only);

            Assert.True(registry.TryResolve(Tokens(""), "load", Sink(out _), out CargoHolderRequest request));

            Assert.Same(only, request.Finder);
            Assert.Empty(only.SeenTokens);
        }

        [Fact]
        public void With_one_kind_of_holder_the_whole_line_is_its_specification()
        {
            var registry = new CargoHolderRegistry();
            var only = new SpyFinder("yard");
            registry.Register(only);

            Assert.True(registry.TryResolve(Tokens("north pile"), "load north pile", Sink(out _), out _));

            Assert.Equal(new[] { "north", "pile" }, only.SeenTokens);
        }

        [Fact]
        public void With_several_kinds_a_missing_keyword_is_a_paper_error()
        {
            // Choosing one for the player would be choosing the wrong one half the time.
            var registry = new CargoHolderRegistry();
            registry.Register(new SpyFinder("yard"));
            registry.Register(new SpyFinder("train"));

            Assert.False(registry.TryResolve(Tokens(""), "load", Sink(out var errors), out _));

            Assert.Equal("holderunknown", Assert.Single(errors).Reason);
        }

        [Fact]
        public void An_error_carries_the_line_the_header_stands_on()
        {
            var registry = new CargoHolderRegistry();
            registry.Register(new SpyFinder("yard"));
            registry.Register(new SpyFinder("train"));

            PaperErrorSink sink = Sink(out var errors);
            sink.CurrentLine = 7;

            registry.TryResolve(Tokens("somewhere"), "load somewhere", sink, out _);

            Assert.Equal(7, Assert.Single(errors).Line);
        }

        [Fact]
        public void Registering_the_same_keyword_again_replaces_it()
        {
            // Reloading a mod must not leave two of the same kind of holder answering.
            var registry = new CargoHolderRegistry();
            registry.Register(new SpyFinder("yard"));
            var second = new SpyFinder("yard");
            registry.Register(second);

            Assert.Same(second, Assert.Single(registry.Finders));
        }

        [Fact]
        public void A_finder_that_rejects_the_header_rejects_the_section()
        {
            var registry = new CargoHolderRegistry();
            registry.Register(new SpyFinder("yard") { Accepts = false });

            Assert.False(registry.TryResolve(Tokens("yard sideways"), "load yard sideways", Sink(out _), out CargoHolderRequest request));
            Assert.Null(request);
        }

        // ---------------------------------------------------------------- the yard finder

        [Fact]
        public void A_yard_header_without_a_name_means_whichever_yard_is_there()
        {
            YardSelector selector = ParseYard();

            Assert.False(selector.HasName);
        }

        [Fact]
        public void The_rest_of_a_yard_header_is_the_name_spaces_and_all()
        {
            // It has to read the way it is written on the sign. Which tokens the finder takes for
            // itself first is its own business - see YardSearchTests.
            YardSelector selector = ParseYard("coal", "pile");

            Assert.Equal("coal pile", selector.Name);
        }

        [Fact]
        public void A_name_longer_than_a_sign_can_hold_is_cut_to_what_fits()
        {
            // Otherwise a header could name a yard that no sign could ever be given.
            YardSelector selector = ParseYard(new string('x', BEYardSign.MaxNameLength + 10));

            Assert.Equal(BEYardSign.MaxNameLength, selector.Name.Length);
        }

        // ---------------------------------------------------------------- composite inventory

        [Fact]
        public void The_holds_are_laid_out_one_after_another_in_the_order_given()
        {
            // The whole point: filling starts at the near end and works outwards.
            var composite = new CompositeInventory(null);
            composite.Append(Hold("near", 2));
            composite.Append(Hold("far", 3));

            Assert.Equal(5, composite.Count);
            Assert.Equal("near", composite.HoldOf(0).Code);
            Assert.Equal("near", composite.HoldOf(1).Code);
            Assert.Equal("far", composite.HoldOf(2).Code);
            Assert.Equal("far", composite.HoldOf(4).Code);
        }

        [Fact]
        public void The_slots_are_the_ones_the_holds_carry_not_copies()
        {
            FakeHold hold = Hold("near", 1);
            var composite = new CompositeInventory(null);
            composite.Append(hold);

            Assert.Same(hold.Inventory[0], composite[0]);
        }

        [Fact]
        public void An_empty_hold_adds_nothing()
        {
            var composite = new CompositeInventory(null);
            composite.Append(Hold("near", 0));

            Assert.Equal(0, composite.Count);
            Assert.Null(composite.HoldOf(0));
        }

        // ---------------------------------------------------------------- plumbing

        /// <summary>One end of a header, which is what the registry resolves.</summary>
        private static string[] Tokens(string text)
        {
            return text.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        }

        private static PaperErrorSink Sink(out List<PaperConditionError> errors)
        {
            errors = new List<PaperConditionError>();
            return new PaperErrorSink(errors);
        }

        private static YardSelector ParseYard(params string[] tokens)
        {
            Assert.True(new YardCargoHolderFinder().TryParseHeader(tokens, null, out ICargoSelector selector));
            return Assert.IsType<YardSelector>(selector);
        }

        private static FakeHold Hold(string code, int slots)
        {
            return new FakeHold(code, new InventoryGeneric(slots, "signalslink-fake" + code, null));
        }

        private sealed class FakeHold : ICargoHold
        {
            public FakeHold(string code, IInventory inventory)
            {
                Code = code;
                Inventory = inventory;
            }

            public string Code { get; }
            public IInventory Inventory { get; }
            public BlockPos Pos => null;      // an inventory-backed hold: a wagon, not a place
            public void MarkDirty() { }
        }

        private sealed class SpyFinder : ICargoHolderFinder
        {
            public SpyFinder(string keyword)
            {
                Keyword = keyword;
            }

            public string Keyword { get; }
            public bool Accepts { get; set; } = true;
            public string[] SeenTokens { get; private set; }

            public bool TryParseHeader(IReadOnlyList<string> tokens, PaperErrorSink errors, out ICargoSelector selector)
            {
                SeenTokens = tokens.ToArray();
                selector = null;
                return Accepts;
            }

            public bool TryFind(IWorldAccessor world, BlockPos devicePos, ICargoSelector selector, out ICargoHolder holder)
            {
                holder = null;
                return false;
            }
        }
    }
}
