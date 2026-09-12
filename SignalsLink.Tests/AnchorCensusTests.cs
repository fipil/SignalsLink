using SignalsLink.src.signals.chunkanchor;
using Xunit;

namespace SignalsLink.Tests
{
    /// <summary>
    /// What an anchor costs.
    ///
    /// The numbers here are a design decision, not an implementation detail: they are what makes a
    /// remote workshop cheap and a home base absurd. So the two cases the design was argued from
    /// are written down as tests - if someone tunes a constant and the gap between them collapses,
    /// the anchor has quietly stopped doing the one thing it exists to do.
    /// </summary>
    public class AnchorCensusTests
    {
        /// <summary>The remote workshop the price is meant to allow.</summary>
        private const int WorkshopBlocks = 300;

        /// <summary>
        /// The home base it is meant to discourage - MEASURED, not imagined. Seven chunk columns
        /// of a real castle came to this, and the first calibration (a reference load of 50, from
        /// guesswork) priced it at five gears a day.
        /// </summary>
        private const int BaseBlocks = 6141;
        private const int BaseCreatures = 81;

        // What the shared charge behaviour uses out of the box.
        private const float GearCharge = 100f * 24000f;
        private const float ReferenceVolume = 100f;
        private const float Factor = 1f;

        [Fact]
        public void An_animal_counts_for_ten_active_blocks()
        {
            Assert.Equal(10f, AnchorCensus.Units(0, 1));
            Assert.Equal(20f, AnchorCensus.Units(10, 1));
        }

        [Fact]
        public void An_empty_area_costs_nothing()
        {
            Assert.Equal(0f, AnchorCensus.Units(0, 0));
            Assert.Equal(0f, AnchorCensus.EffectiveVolume(0, ReferenceVolume));
            Assert.Equal(double.PositiveInfinity, AnchorCensus.DaysPerGear(0, GearCharge, ReferenceVolume, Factor));
        }

        [Fact]
        public void Nonsense_counts_do_not_produce_nonsense_prices()
        {
            Assert.Equal(0f, AnchorCensus.Units(-5, -5));
            Assert.Equal(0f, AnchorCensus.EffectiveVolume(-1, ReferenceVolume));
        }

        [Fact]
        public void A_workshop_pays_about_the_base_rate()
        {
            // A workshop should be something a player sets up and forgets about for a season.
            double days = AnchorCensus.DaysPerGear(AnchorCensus.Units(WorkshopBlocks, 0),
                GearCharge, ReferenceVolume, Factor);

            Assert.InRange(days, 50, 120);
        }

        [Fact]
        public void And_a_home_base_pays_dozens_of_times_that()
        {
            // The whole reason the curve is superlinear. If this ratio ever collapses towards the
            // plain ratio of the raw unit counts, the exponent has been lost somewhere. It must
            // also stay finite: a castle should be expensive, not impossible.
            double workshop = AnchorCensus.DaysPerGear(AnchorCensus.Units(WorkshopBlocks, 0),
                GearCharge, ReferenceVolume, Factor);

            double home = AnchorCensus.DaysPerGear(AnchorCensus.Units(BaseBlocks, BaseCreatures),
                GearCharge, ReferenceVolume, Factor);

            Assert.InRange(workshop / home, 20, 80);
            Assert.InRange(home, 1.0, 5.0);
        }

        [Fact]
        public void In_a_real_base_it_is_the_buildings_that_cost_not_the_herd()
        {
            // Written down because it contradicts what the design assumed. Animals were expected to
            // be the thing that marks a base out, and at ten times a block each they are the
            // dearest single thing a player can own - but a built castle turned out to hold 6141
            // active blocks against 81 animals, so the herd is barely a tenth of the bill.
            //
            // Which means the creature weight is NOT the knob to turn when a base comes out too
            // cheap or too dear. The reference load is.
            float herd = AnchorCensus.Units(0, BaseCreatures);
            float everything = AnchorCensus.Units(BaseBlocks, BaseCreatures);

            Assert.True(herd < everything * 0.2f,
                "the herd is " + herd + " of " + everything + " units - the assumption held after all");
        }

        [Fact]
        public void More_held_is_never_cheaper()
        {
            double previous = double.PositiveInfinity;

            for (int blocks = 10; blocks <= 500; blocks += 10)
            {
                double days = AnchorCensus.DaysPerGear(AnchorCensus.Units(blocks, 0),
                    GearCharge, ReferenceVolume, Factor);

                Assert.True(days < previous, "price did not rise at " + blocks + " active blocks");
                previous = days;
            }
        }
    }
}
