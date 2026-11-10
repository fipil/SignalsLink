using System.Reflection;
using SignalsLink.YTT.src.probe;
using SignalsLink.YTT.src.stoker;
using Vintagestory.API.Common;

namespace SignalsLink.Tests
{
    /// <summary>
    /// The stoker's decisions, checked on a table: what a pin value asks for, when it acts, what
    /// putting a fire out costs, and what the probe accepts as a fire to tend.
    /// </summary>
    public class StokerRulesTests
    {
        // ---------------------------------------------------------------- the input pin

        [Theory]
        [InlineData(0, StokerCommand.Leave)]
        [InlineData(1, StokerCommand.Burn)]
        [InlineData(2, StokerCommand.Quench)]
        [InlineData(3, StokerCommand.Leave)]
        [InlineData(15, StokerCommand.Leave)]
        public void A_pin_value_is_a_command_or_nothing(byte input, StokerCommand expected)
        {
            Assert.Equal(expected, StokerRules.CommandFor(input));
        }

        // ---------------------------------------------------------------- levels, not events

        [Theory]
        [InlineData(StokerCommand.Burn, true, StokerAction.None)]
        [InlineData(StokerCommand.Burn, false, StokerAction.Ignite)]
        [InlineData(StokerCommand.Quench, true, StokerAction.Extinguish)]
        [InlineData(StokerCommand.Quench, false, StokerAction.None)]
        [InlineData(StokerCommand.Leave, true, StokerAction.None)]
        [InlineData(StokerCommand.Leave, false, StokerAction.None)]
        public void It_acts_only_on_a_difference(StokerCommand command, bool burning, StokerAction expected)
        {
            Assert.Equal(expected, StokerRules.Decide(command, burning));
        }

        [Fact]
        public void The_output_says_only_whether_it_burns()
        {
            Assert.Equal(1, StokerRules.OutputFor(true));
            Assert.Equal(0, StokerRules.OutputFor(false));
        }

        [Fact]
        public void The_window_glows_only_while_asked_to_keep_the_fire()
        {
            Assert.True(StokerRules.WindowLit(1));
            Assert.False(StokerRules.WindowLit(0));
            Assert.False(StokerRules.WindowLit(2));
        }

        // ---------------------------------------------------------------- the price of water

        [Theory]
        [InlineData(400, 1, 4)]      // a litre per hundred degrees
        [InlineData(400, 100, 400)]  // in items: water portions are a hundred to the litre
        [InlineData(350, 1, 4)]      // rounded up, as the locomotive's own button does
        [InlineData(20, 100, 20)]
        [InlineData(0, 100, 0)]
        [InlineData(-5, 100, 0)]     // nothing below zero
        public void Putting_out_costs_a_litre_per_hundred_degrees(double temperature, double itemsPerLitre, int expected)
        {
            Assert.Equal(expected, StokerRules.WaterItemsFor(temperature, itemsPerLitre));
        }

        [Fact]
        public void An_unknown_liquid_density_counts_as_one_item_per_litre()
        {
            Assert.Equal(4, StokerRules.WaterItemsFor(400, 0));
        }

        // ---------------------------------------------------------------- the wrench

        [Fact]
        public void The_wrench_cycles_one_to_ten_and_round_again()
        {
            Assert.Equal(3, StokerRules.NextReach(2));
            Assert.Equal(10, StokerRules.NextReach(9));
            Assert.Equal(1, StokerRules.NextReach(10));
            Assert.Equal(1, StokerRules.NextReach(0));
        }

        [Fact]
        public void A_saved_reach_outside_the_range_is_pulled_back_in()
        {
            Assert.Equal(1, StokerRules.ClampReach(-3));
            Assert.Equal(10, StokerRules.ClampReach(42));
            Assert.Equal(2, StokerRules.ClampReach(2));
        }

        // ---------------------------------------------------------------- height

        [Theory]
        [InlineData(100.0, 100, true)]   // level with the platform
        [InlineData(99.0, 100, true)]    // a sunken track
        [InlineData(98.0, 100, true)]    // two blocks down is still this platform's track
        [InlineData(102.3, 100, true)]   // two up, a little above its rail
        [InlineData(97.4, 100, false)]   // three down is another line
        [InlineData(104.0, 100, false)]  // a bridge over the station
        public void Only_a_track_within_two_blocks_of_the_floor_counts(double vehicleY, int deviceY, bool expected)
        {
            Assert.Equal(expected, LocomotiveFinder.WithinHeight(vehicleY, deviceY));
        }

        // ---------------------------------------------------------------- the probe

        /// <summary>What the other mod's controller looks like today, as far as the stoker cares.</summary>
        private sealed class ControllerLikeToday
        {
            public bool IsBurning => true;
            public double TemperatureC { get; private set; } = 20;
            public ItemSlot FuelSlot => null;
            public ItemSlot WaterSlot => null;
            public void TryIgniteNow() { }
            public void ForceExtinguishBoiler() { }
            public static bool IsIgnitionSource(ItemStack stack) => stack != null;
        }

        private sealed class ControllerWithoutBurning
        {
            public double TemperatureC => 20;
            public ItemSlot FuelSlot => null;
            public ItemSlot WaterSlot => null;
            public void TryIgniteNow() { }
            public void ForceExtinguishBoiler() { }
            public static bool IsIgnitionSource(ItemStack stack) => true;
        }

        private sealed class ControllerWithRetypedTemperature
        {
            public bool IsBurning => true;
            public float TemperatureC => 20;
            public ItemSlot FuelSlot => null;
            public ItemSlot WaterSlot => null;
            public void TryIgniteNow() { }
            public void ForceExtinguishBoiler() { }
            public static bool IsIgnitionSource(ItemStack stack) => true;
        }

        private sealed class ControllerWhoseLightCheckWentInstance
        {
            public bool IsBurning => true;
            public double TemperatureC => 20;
            public ItemSlot FuelSlot => null;
            public ItemSlot WaterSlot => null;
            public void TryIgniteNow() { }
            public void ForceExtinguishBoiler() { }
            public bool IsIgnitionSource(ItemStack stack) => true;
        }

        [Fact]
        public void Todays_controller_offers_a_fire_to_tend()
        {
            FireMembers fire = FireMembers.On(typeof(ControllerLikeToday));

            Assert.NotNull(fire);
            Assert.True(fire.IsIgnitionSource.IsStatic);
            Assert.Equal("TryIgniteNow", fire.Ignite.Name);
        }

        [Fact]
        public void A_private_setter_does_not_hide_the_property()
        {
            // TemperatureC has a private setter on the real controller; reading is all that matters.
            FireMembers fire = FireMembers.On(typeof(ControllerLikeToday));

            Assert.NotNull(fire?.Temperature);
            Assert.True(fire.Temperature.CanRead);
        }

        [Fact]
        public void A_missing_member_means_no_fire()
        {
            Assert.Null(FireMembers.On(typeof(ControllerWithoutBurning)));
        }

        [Fact]
        public void A_retyped_member_means_no_fire()
        {
            // The dangerous kind: the name still resolves, and the cast blows up at the player.
            Assert.Null(FireMembers.On(typeof(ControllerWithRetypedTemperature)));
        }

        [Fact]
        public void The_light_check_has_to_stay_static()
        {
            Assert.Null(FireMembers.On(typeof(ControllerWhoseLightCheckWentInstance)));
        }

        [Fact]
        public void Nothing_to_probe_means_no_fire()
        {
            Assert.Null(FireMembers.On(null));
        }
    }
}
